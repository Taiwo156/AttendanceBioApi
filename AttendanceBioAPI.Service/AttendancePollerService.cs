using System.Collections.Concurrent;
using System.Globalization;
using AttendanceBioApi.Data.Database;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service;

public class AttendancePollerService : BackgroundService
{
    private const string CheckIn = "0";
    private const string CheckOut = "1";
    private const string BreakIn = "3";
    private const string OtIn = "4";
    private const string OtOut = "5";

    private readonly ILogger<AttendancePollerService> _logger;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly IAttendanceEventsPublisher _publisher;
    private readonly IZkbioService _zkbioService;
    private readonly ICompanyResolutionService _companyResolutionService;
    private readonly IEmployeeResolutionService _employeeResolutionService;
    private readonly IBiometricDeviceRepository _biometricDeviceRepository;
    private readonly IBiometricDeviceFactory _biometricDeviceFactory;
    private readonly ConcurrentDictionary<string, DateTime> _directDeviceLastPunch = new();
    private readonly int _pollIntervalSeconds;
    private readonly string _siteId;

    private readonly ConcurrentDictionary<string, ZkbioEmployee> _employeeCache = new();
    private readonly ConcurrentDictionary<string, byte> _seenPunches = new();

    private DateTime _lastCheckedTime = DateTime.UtcNow.AddMinutes(-1);

    public AttendancePollerService(
        ILogger<AttendancePollerService> logger,
        IZkbioService zkbioService,
        ICompanyResolutionService companyResolutionService,
        IEmployeeResolutionService employeeResolutionService,
        ISqlConnectionFactory sqlConnectionFactory,
        IAttendanceEventsPublisher publisher,
        IBiometricDeviceRepository biometricDeviceRepository,
        IBiometricDeviceFactory biometricDeviceFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _zkbioService = zkbioService;
        _companyResolutionService = companyResolutionService;
        _employeeResolutionService = employeeResolutionService;
        _sqlConnectionFactory = sqlConnectionFactory;
        _publisher = publisher;
        _biometricDeviceRepository = biometricDeviceRepository;
        _biometricDeviceFactory = biometricDeviceFactory;
        _pollIntervalSeconds = int.TryParse(configuration["AttendancePolling:IntervalSeconds"], out var secs) ? secs : 30;
        _siteId = configuration["Attendance:SiteId"] ?? "site-1";
    }

    public IReadOnlyCollection<ZkbioEmployee> GetCachedEmployees() => _employeeCache.Values.ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await _zkbioService.LoginAsync(stoppingToken))
            {
                _logger.LogError("Unable to start AttendancePollerService because ZKBio login failed.");
                return;
            }

            await RefreshEmployeeCacheAsync(stoppingToken);
            _logger.LogInformation("Waiting for CompanyResolutionService to complete its first refresh...");

            await _companyResolutionService.WaitUntilReadyAsync(stoppingToken);

            _logger.LogInformation("CompanyResolutionService ready — starting punch polling.");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollIntervalSeconds));

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // Existing ZKBioTime flow
                    await PollForNewPunchesAsync(stoppingToken);

                    // New direct biometric-device flow
                    await PollDirectDevicesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Attendance poll iteration failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AttendancePollerService is stopping.");
        }
    }

    private async Task RefreshEmployeeCacheAsync(CancellationToken ct)
    {
        var employees = await _zkbioService.GetEmployeesAsync(ct);

        _employeeCache.Clear();
        foreach (var employee in employees)
        {
            _employeeCache[employee.EmpCode] = employee;
        }

        _logger.LogInformation("Cached {Count} ZKBio employees", _employeeCache.Count);
    }

    private async Task PollForNewPunchesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        _logger.LogInformation(
            "Polling ZKBio transactions. LastCheckedTime={LastCheckedTime:O}, CurrentUtc={CurrentUtc:O}",
            _lastCheckedTime,
            DateTime.UtcNow);

        var transactions =
            await _zkbioService.GetTransactionsAsync(_lastCheckedTime, ct);

        _logger.LogInformation(
            "Polling returned {Count} transaction(s)",
            transactions.Count);
        var processedCount = 0;
        foreach (var txn in transactions)
        {
            var punch = new AttendancePunch
            {
                BiometricId = txn.EmpCode,
                PunchTime = DateTime.Parse(
                    txn.PunchTime,
                    CultureInfo.InvariantCulture),
                PunchState = txn.PunchState,
                DeviceId = null,
                SiteId = _siteId
            };

            var key = $"{punch.BiometricId}:{punch.PunchTime}:{punch.PunchState}";

            if (!_seenPunches.TryAdd(key, 0))
                continue;

            await ProcessPunchAsync(punch, ct);
            processedCount++;
        }

        if (processedCount > 0)
        {
            _logger.LogInformation("Processed {Count} new punch(es)", processedCount);
        }

        _lastCheckedTime = now;
    }

    private async Task ProcessPunchAsync(
        AttendancePunch punch,
        CancellationToken ct)
    {
        if (!_employeeCache.TryGetValue(
                punch.BiometricId,
                out var employee))
        {
            _logger.LogWarning(
                "Employee {EmpCode} was not found in ZKBio employee cache",
                punch.BiometricId);

            return;
        }

        if (employee.Department is null)
        {
            _logger.LogWarning(
                "Employee {EmpCode} has no department assigned in ZKBio",
                punch.BiometricId);

            return;
        }

        var companyId =
            await _companyResolutionService.ResolveCompanyIdAsync(
                employee,
                ct);

        if (string.IsNullOrWhiteSpace(companyId))
        {
            _logger.LogError(
                "Cannot process punch for employee {EmpCode}: CompanyID could not be resolved.",
                punch.BiometricId);

            await _publisher.PublishAlertAsync(
                new AttendanceProcessingAlert
                {
                    EmployeeCode = punch.BiometricId,
                    EmployeeName = employee.FullName,
                    Reason = "CompanyNotResolved",
                    Message =
                        $"{employee.FullName ?? punch.BiometricId}'s punch couldn't be matched to a company. " +
                        "Please contact IT — this usually means a setup step is missing.",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

            return;
        }

        var payrollEmployee =
            await _employeeResolutionService
                .ResolveByBiometricIdAsync(
                    punch.BiometricId,
                    ct);

        if (payrollEmployee is null)
        {
            _logger.LogWarning(
                "Ignoring punch for biometric ID {BiometricId}: payroll employee could not be resolved.",
                punch.BiometricId);

            await _publisher.PublishAlertAsync(
                new AttendanceProcessingAlert
                {
                    EmployeeCode = punch.BiometricId,
                    EmployeeName = employee.FullName,
                    Reason = "EmployeeNotResolved",
                    Message =
                        $"{employee.FullName ?? punch.BiometricId}'s biometric ID could not be matched " +
                        "to a payroll employee. Please check PayrollEmployees.EmployeeBiometricID.",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

            return;
        }

        var employeeName =
            string.IsNullOrWhiteSpace(payrollEmployee.EmployeeName)
                ? employee.FullName ?? punch.BiometricId
                : payrollEmployee.EmployeeName;

        await ProcessResolvedPunchAsync(
            punch,
            payrollEmployee,
            employeeName,
            companyId,
            _siteId,
            ct);
    }
    private async Task HandleCheckInAsync(
        System.Data.IDbConnection connection,
        AttendancePunch punch,
        PayrollEmployeeBiometric payrollEmployee,
        string companyId,
        string siteId,
        DateTime punchTime,
        DateTime attendanceDate)
    {
        var existing = await connection.QueryFirstOrDefaultAsync<(bool? ClockedIn, bool? ClockedOut)>(@"
            SELECT [ClockedIn], [ClockedOut] FROM [dbo].[PayrollHRPayrollAttDetail]
            WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
              AND [AttendanceDate] = @AttendanceDate AND [EmployeeID] = @EmployeeId",
            new { CompanyId = companyId, AttendanceDate = attendanceDate, EmployeeId = payrollEmployee.EmployeeId });

        if (existing.ClockedIn == true)
        {
            _logger.LogWarning("Ignored duplicate check-in for employee {EmpCode} — already clocked in today", payrollEmployee.EmployeeId);
            return;
        }

        await connection.ExecuteAsync(@"
            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[PayrollHRPayrollAttHeader]
                WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
                  AND [AttendanceDate] = @AttendanceDate
            )
            BEGIN
                INSERT INTO [dbo].[PayrollHRPayrollAttHeader]
                ([CompanyID], [DivisionID], [DepartmentID], [AttendanceDate], [EnteredDate], [Status])
                VALUES (@CompanyId, 'DEFAULT', 'DEFAULT', @AttendanceDate, GETDATE(), 'Open')
            END",
            new { CompanyId = companyId, AttendanceDate = attendanceDate });

        var graceTime = await connection.QueryFirstOrDefaultAsync<DateTime?>(@"
            SELECT TOP 1 [GraceTime] FROM [dbo].[PayrollHRPayrollCompanyStandard]
            WHERE [CompanyID] = @CompanyId",
            new { CompanyId = companyId });

        var isLate = false;
        var latePeriodMinutes = 0;

        if (graceTime.HasValue)
        {
            var configuredGraceTime = graceTime.Value.TimeOfDay;
            var punchTimeOfDay = punchTime.TimeOfDay;

            if (punchTimeOfDay > configuredGraceTime)
            {
                isLate = true;
                latePeriodMinutes = (int)(punchTimeOfDay - configuredGraceTime).TotalMinutes;
            }
        }
        else
        {
            _logger.LogWarning("GraceTime is not configured for CompanyID {CompanyId}. Employee {EmpCode} will not be marked late.", companyId, payrollEmployee.EmployeeId);
        }

        var payrollEmployeeName = await connection.QueryFirstOrDefaultAsync<string?>(@"
            SELECT [EmployeeName] FROM [dbo].[PayrollEmployees]
            WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT' AND [EmployeeID] = @EmployeeId",
            new { CompanyId = companyId, EmployeeId = payrollEmployee.EmployeeId });

        var employeeName =
            payrollEmployeeName
            ?? payrollEmployee.EmployeeName
            ?? $"Employee {payrollEmployee.EmployeeId}";
        await connection.ExecuteAsync(@"
            INSERT INTO [dbo].[PayrollHRPayrollAttDetail]
            ([CompanyID], [DivisionID], [DepartmentID], [AttendanceDate], [EmployeeID], [EmployeeName], [TimeIn], [ClockedIn], [ClockedOut], [LatePeriod], [AttendanceStatus], [BranchCode], [SystemDate])
            VALUES (@CompanyId, 'DEFAULT', 'DEFAULT', @AttendanceDate, @EmployeeId, @EmployeeName, @TimeIn, 1, 0, @LatePeriod, @AttendanceStatus, @SiteId, GETDATE())",
            new
            {
                CompanyId = companyId,
                AttendanceDate = attendanceDate,
                EmployeeId = payrollEmployee.EmployeeId,
                EmployeeName = employeeName,
                TimeIn = punchTime,
                LatePeriod = latePeriodMinutes,
                AttendanceStatus = isLate ? "Late" : "Present",
                SiteId = siteId
            });
    }

    private async Task HandleCheckOutAsync(
        System.Data.IDbConnection connection,
        AttendancePunch punch,
        string employeeId,
        string companyId,
        DateTime punchTime,
        DateTime attendanceDate)
    {
        var existing = await connection.QueryFirstOrDefaultAsync<PayrollAttStatus>(@"
            SELECT [ClockedIn], [ClockedOut] FROM [dbo].[PayrollHRPayrollAttDetail]
            WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
              AND [AttendanceDate] = @AttendanceDate AND [EmployeeID] = @EmployeeId",
            new { CompanyId = companyId, AttendanceDate = attendanceDate, EmployeeId = employeeId });

        if (existing is null || existing.ClockedIn != true)
        {
            _logger.LogWarning("Ignored check-out for employee {EmpCode} — not currently clocked in", employeeId);
            return;
        }

        if (existing.ClockedOut == true)
        {
            _logger.LogWarning("Ignored duplicate check-out for employee {EmpCode} — already clocked out today", employeeId);
            return;
        }

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PayrollHRPayrollAttDetail]
            SET [ClockOutTimeOut] = @TimeOut, [ClockedOut] = 1, [SystemDate] = GETDATE()
            WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
              AND [AttendanceDate] = @AttendanceDate AND [EmployeeID] = @EmployeeId",
            new { CompanyId = companyId, AttendanceDate = attendanceDate, EmployeeId = employeeId, TimeOut = punchTime });

        _logger.LogInformation("Successfully completed check-out sequence for employee {EmpCode}, CompanyID {CompanyId}", employeeId, companyId);
    }

    private async Task HandleOvertimeInAsync(
    System.Data.IDbConnection connection,
    string employeeId,
    string companyId,
    DateTime punchTime,
    DateTime overtimeDate)
    {
        var overtime = await connection.QueryFirstOrDefaultAsync<Overtime>(@"
        SELECT TOP 1
            [OvertimeTypeID],
            [PlannedOvertime],
            [PlannedOvertimeStartTime],
            [PlannedOvertimeEndTime],
            [ActualOvertimeStartTime],
            [ActualOvertimeEndTime]
        FROM [dbo].[PayrollHRPayrollOvertimeDetail]
        WHERE [CompanyID] = @CompanyId
          AND [DivisionID] = 'DEFAULT'
          AND [DepartmentID] = 'DEFAULT'
          AND [EmployeeID] = @EmployeeId
          AND [OvertimeDate] = @OvertimeDate
        ORDER BY [PlannedOvertimeStartTime]",
            new
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                OvertimeDate = overtimeDate
            });

        if (overtime is null)
        {
            _logger.LogWarning(
                "OT IN ignored for employee {EmployeeId}: no overtime record found for {OvertimeDate}",
                employeeId,
                overtimeDate);

            return;
        }

        if (overtime.ActualOvertimeStartTime.HasValue)
        {
            _logger.LogWarning(
                "OT IN ignored for employee {EmployeeId}: actual overtime already started at {ActualStart}",
                employeeId,
                overtime.ActualOvertimeStartTime);

            return;
        }

        await connection.ExecuteAsync(@"
        UPDATE [dbo].[PayrollHRPayrollOvertimeDetail]
        SET [ActualOvertimeStartTime] = @ActualStart
        WHERE [CompanyID] = @CompanyId
          AND [DivisionID] = 'DEFAULT'
          AND [DepartmentID] = 'DEFAULT'
          AND [EmployeeID] = @EmployeeId
          AND [OvertimeDate] = @OvertimeDate",
            new
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                OvertimeDate = overtimeDate,
                ActualStart = punchTime
            });

        _logger.LogInformation(
            "OT IN recorded for employee {EmployeeId} at {PunchTime}",
            employeeId,
            punchTime);
    }

    private async Task HandleOvertimeOutAsync(
    System.Data.IDbConnection connection,
    string employeeId,
    string companyId,
    DateTime punchTime,
    DateTime overtimeDate)
    {
        var overtime = await connection.QueryFirstOrDefaultAsync<Overtime>(@"
        SELECT TOP 1
            [OvertimeTypeID],
            [PlannedOvertime],
            [PlannedOvertimeStartTime],
            [PlannedOvertimeEndTime],
            [ActualOvertimeStartTime],
            [ActualOvertimeEndTime]
        FROM [dbo].[PayrollHRPayrollOvertimeDetail]
        WHERE [CompanyID] = @CompanyId
          AND [DivisionID] = 'DEFAULT'
          AND [DepartmentID] = 'DEFAULT'
          AND [EmployeeID] = @EmployeeId
          AND [OvertimeDate] = @OvertimeDate
        ORDER BY [PlannedOvertimeStartTime]",
            new
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                OvertimeDate = overtimeDate
            });

        if (overtime is null)
        {
            _logger.LogWarning(
                "OT OUT ignored for employee {EmployeeId}: no overtime record found for {OvertimeDate}",
                employeeId,
                overtimeDate);

            return;
        }

        if (!overtime.ActualOvertimeStartTime.HasValue)
        {
            _logger.LogWarning(
                "OT OUT ignored for employee {EmployeeId}: no OT IN has been recorded",
                employeeId);

            return;
        }

        if (overtime.ActualOvertimeEndTime.HasValue)
        {
            _logger.LogWarning(
                "OT OUT ignored for employee {EmployeeId}: actual overtime already ended at {ActualEnd}",
                employeeId,
                overtime.ActualOvertimeEndTime);

            return;
        }

        if (punchTime <= overtime.ActualOvertimeStartTime.Value)
        {
            _logger.LogWarning(
                "OT OUT ignored for employee {EmployeeId}: punch time {PunchTime} is not after OT IN {ActualStart}",
                employeeId,
                punchTime,
                overtime.ActualOvertimeStartTime);

            return;
        }

        var actualDuration =
            (punchTime - overtime.ActualOvertimeStartTime.Value).TotalHours;

        await connection.ExecuteAsync(@"
        UPDATE [dbo].[PayrollHRPayrollOvertimeDetail]
        SET
            [ActualOvertimeEndTime] = @ActualEnd,
            [Duration] = @Duration
        WHERE [CompanyID] = @CompanyId
          AND [DivisionID] = 'DEFAULT'
          AND [DepartmentID] = 'DEFAULT'
          AND [EmployeeID] = @EmployeeId
          AND [OvertimeDate] = @OvertimeDate",
            new
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                OvertimeDate = overtimeDate,
                ActualEnd = punchTime,
                Duration = actualDuration
            });

        _logger.LogInformation(
            "OT OUT recorded for employee {EmployeeId} at {PunchTime}. Actual overtime: {Hours:0.00} hours",
            employeeId,
            punchTime,
            actualDuration);
    }

    private async Task<AttendanceStatusEvent> BuildAttendanceStatusEventAsync(
    System.Data.IDbConnection connection,
    AttendancePunch punch,
    PayrollEmployeeBiometric payrollEmployee,
    string employeeName,
    string companyId,
    string siteId,
    DateTime punchTime,
    DateTime attendanceDate)
    {
        var dayRecord =
            await connection.QueryFirstOrDefaultAsync<PayrollAttStatus>(@"
        SELECT
            [TimeIn],
            [ClockOutTimeOut],
            [ClockedIn],
            [ClockedOut]
        FROM [dbo].[PayrollHRPayrollAttDetail]
        WHERE [CompanyID] = @CompanyId
          AND [DivisionID] = 'DEFAULT'
          AND [DepartmentID] = 'DEFAULT'
          AND [AttendanceDate] = @AttendanceDate
          AND [EmployeeID] = @EmployeeId",
            new
            {
                CompanyId = companyId,
                AttendanceDate = attendanceDate,
                EmployeeId = payrollEmployee.EmployeeId
            });

        var overtime =
            await connection.QueryFirstOrDefaultAsync<Overtime>(@"
        SELECT TOP 1
            [OvertimeTypeID],
            [PlannedOvertime],
            [PlannedOvertimeStartTime],
            [PlannedOvertimeEndTime],
            [ActualOvertimeStartTime],
            [ActualOvertimeEndTime],
            [Duration]
        FROM [dbo].[PayrollHRPayrollOvertimeDetail]
        WHERE [CompanyID] = @CompanyId
          AND [DivisionID] = 'DEFAULT'
          AND [DepartmentID] = 'DEFAULT'
          AND [EmployeeID] = @EmployeeId
          AND [OvertimeDate] = @OvertimeDate
        ORDER BY [PlannedOvertimeStartTime]",
            new
            {
                CompanyId = companyId,
                EmployeeId = payrollEmployee.EmployeeId,
                OvertimeDate = attendanceDate
            });

        var isIn =
            punch.PunchState is CheckIn
            or BreakIn
            or OtIn;

        return new AttendanceStatusEvent
        {
            EmployeeId = payrollEmployee.EmployeeId,

            EmployeeName =
                employeeName ?? $"Employee {payrollEmployee.EmployeeId}",

            Department = "DEFAULT",

            SiteId = siteId,

            Status = isIn ? "in" : "out",

            ClockinTime =
                dayRecord?.TimeIn?
                    .ToUniversalTime()
                    .ToString("o"),

            ClockoutTime =
                dayRecord?.ClockOutTimeOut?
                    .ToUniversalTime()
                    .ToString("o"),

            PlannedOtStartTime =
                overtime?.PlannedOvertimeStartTime?
                    .ToUniversalTime()
                    .ToString("o"),

            PlannedOtEndTime =
                overtime?.PlannedOvertimeEndTime?
                    .ToUniversalTime()
                    .ToString("o"),

            ActualOtStartTime =
                overtime?.ActualOvertimeStartTime?
                    .ToUniversalTime()
                    .ToString("o"),

            ActualOtEndTime =
                overtime?.ActualOvertimeEndTime?
                    .ToUniversalTime()
                    .ToString("o"),

            OverTime =
                overtime?.Duration.HasValue == true
                    ? overtime.Duration.Value
                        .ToString(CultureInfo.InvariantCulture)
                    : overtime?.PlannedOvertime.HasValue == true
                        ? overtime.PlannedOvertime.Value
                            .ToString(CultureInfo.InvariantCulture)
                        : null
        };
    }

    private async Task PollDirectDevicesAsync(CancellationToken ct)
    {
        var devices = await _biometricDeviceRepository.GetAllAsync(ct);

        var activeDevices = devices
            .Where(d => d.Enabled && d.AutomaticSync)
            .ToList();

        if (activeDevices.Count == 0)
        {
            _logger.LogDebug(
                "No enabled biometric devices configured for automatic synchronization.");

            return;
        }

        _logger.LogInformation(
            "Polling {Count} direct biometric device(s).",
            activeDevices.Count);

        foreach (var deviceConfig in activeDevices)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await PollDirectDeviceAsync(deviceConfig, ct);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed polling biometric device {DeviceName} ({DeviceId})",
                    deviceConfig.DeviceName,
                    deviceConfig.DeviceId);
            }
        }
    }

    private async Task PollDirectDeviceAsync(
    BiometricDevice deviceConfig,
    CancellationToken ct)
    {
        _logger.LogInformation(
            "Polling direct biometric device {DeviceName} ({IpAddress}:{Port})",
            deviceConfig.DeviceName,
            deviceConfig.IpAddress,
            deviceConfig.Port);

        var device =
            await _biometricDeviceFactory.CreateAsync(
                deviceConfig.DeviceId,
                ct);

        if (device is null)
        {
            _logger.LogWarning(
                "Could not create biometric device {DeviceId}",
                deviceConfig.DeviceId);

            return;
        }

        try
        {
            var connected =
                await device.ConnectAsync(ct);

            if (!connected)
            {
                _logger.LogWarning(
                    "Could not connect to biometric device {DeviceName} ({IpAddress}:{Port})",
                    deviceConfig.DeviceName,
                    deviceConfig.IpAddress,
                    deviceConfig.Port);

                return;
            }

            _logger.LogInformation(
                "Connected to biometric device {DeviceName}",
                deviceConfig.DeviceName);

            var punches =
                await device.GetAttendancePunchesAsync(ct);

            _logger.LogInformation(
                "Biometric device {DeviceName} returned {Count} attendance punch(es)",
                deviceConfig.DeviceName,
                punches.Count);

            var orderedPunches = punches
    .OrderBy(x => x.PunchTime)
    .ToList();

            if (orderedPunches.Count == 0)
            {
                _logger.LogDebug(
                    "No attendance records returned from device {DeviceName}.",
                    deviceConfig.DeviceName);

                return;
            }

            var deviceKey = deviceConfig.DeviceId;

            if (!_directDeviceLastPunch.TryGetValue(
                    deviceKey,
                    out var lastPunchTime))
            {
                lastPunchTime = orderedPunches[^1].PunchTime;

                _directDeviceLastPunch[deviceKey] = lastPunchTime;

                _logger.LogInformation(
                    "Initialized direct sync watermark for device {DeviceName} at {LastPunchTime:O}. " +
                    "Existing historical punches will not be reprocessed.",
                    deviceConfig.DeviceName,
                    lastPunchTime);

                return;
            }

            var newPunches = orderedPunches
                .Where(x => x.PunchTime > lastPunchTime)
                .ToList();

            _logger.LogInformation(
                "Device {DeviceName}: found {Count} new punch(es) after {LastPunchTime:O}.",
                deviceConfig.DeviceName,
                newPunches.Count,
                lastPunchTime);

            foreach (var punch in newPunches)
            {
                if (ct.IsCancellationRequested)
                    break;

                await ProcessDirectPunchAsync(
                    punch,
                    deviceConfig,
                    ct);

                _directDeviceLastPunch[deviceKey] = punch.PunchTime;
            }
        }
        finally
        {
            try
            {
                await device.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed disconnecting biometric device {DeviceName}",
                    deviceConfig.DeviceName);
            }
        }
    }

    private async Task ProcessDirectPunchAsync(
    AttendancePunch punch,
    BiometricDevice deviceConfig,
    CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing direct biometric punch. " +
            "Device={DeviceName}, BiometricId={BiometricId}, " +
            "PunchTime={PunchTime:O}, PunchState={PunchState}",
            deviceConfig.DeviceName,
            punch.BiometricId,
            punch.PunchTime,
            punch.PunchState);

        var payrollEmployee =
            await _employeeResolutionService
                .ResolveByBiometricIdAsync(
                    punch.BiometricId,
                    ct);

        if (payrollEmployee is null)
        {
            _logger.LogWarning(
                "Ignoring direct punch for biometric ID {BiometricId}: " +
                "payroll employee could not be resolved.",
                punch.BiometricId);

            await _publisher.PublishAlertAsync(
                new AttendanceProcessingAlert
                {
                    EmployeeCode = punch.BiometricId,
                    EmployeeName = punch.BiometricId,
                    Reason = "EmployeeNotResolved",
                    Message =
                        $"Biometric ID {punch.BiometricId} from device " +
                        $"{deviceConfig.DeviceName} could not be matched " +
                        "to a payroll employee. Please check " +
                        "PayrollEmployees.EmployeeBiometricID.",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

            return;
        }

        var companyId = deviceConfig.CompanyId;

        if (string.IsNullOrWhiteSpace(companyId))
        {
            _logger.LogError(
                "Cannot process direct punch for biometric ID {BiometricId}: " +
                "CompanyID is missing from biometric device {DeviceId}.",
                punch.BiometricId,
                deviceConfig.DeviceId);

            await _publisher.PublishAlertAsync(
                new AttendanceProcessingAlert
                {
                    EmployeeCode = punch.BiometricId,
                    EmployeeName = payrollEmployee.EmployeeName,
                    Reason = "CompanyNotResolved",
                    Message =
                        $"Punch from device {deviceConfig.DeviceName} " +
                        $"couldn't be matched to a company because " +
                        "the biometric device has no CompanyID configured.",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

            return;
        }

        var siteId =
            string.IsNullOrWhiteSpace(deviceConfig.BranchCode)
                ? _siteId
                : deviceConfig.BranchCode;

        var employeeName =
            string.IsNullOrWhiteSpace(payrollEmployee.EmployeeName)
                ? punch.BiometricId
                : payrollEmployee.EmployeeName;

        await ProcessResolvedPunchAsync(
            punch,
            payrollEmployee,
            employeeName,
            companyId,
            siteId,
            ct);
    }

    private async Task ProcessResolvedPunchAsync(
    AttendancePunch punch,
    PayrollEmployeeBiometric payrollEmployee,
    string employeeName,
    string companyId,
    string siteId,
    CancellationToken ct)
    {
        var punchTime = punch.PunchTime;
        var attendanceDate = punchTime.Date;

        var dbWriteSucceeded = true;

        using var connection =
            _sqlConnectionFactory.Create();

        await connection.OpenAsync(ct);

        try
        {
            switch (punch.PunchState)
            {
                case CheckIn:

                    await HandleCheckInAsync(
                        connection,
                        punch,
                        payrollEmployee,
                        companyId,
                        siteId,
                        punchTime,
                        attendanceDate);

                    break;

                case CheckOut:

                    await HandleCheckOutAsync(
                        connection,
                        punch,
                        payrollEmployee.EmployeeId,
                        companyId,
                        punchTime,
                        attendanceDate);

                    break;

                case OtIn:

                    await HandleOvertimeInAsync(
                        connection,
                        payrollEmployee.EmployeeId,
                        companyId,
                        punchTime,
                        attendanceDate);

                    break;

                case OtOut:

                    await HandleOvertimeOutAsync(
                        connection,
                        payrollEmployee.EmployeeId,
                        companyId,
                        punchTime,
                        attendanceDate);

                    break;

                case BreakIn:

                    _logger.LogInformation(
                        "Break-in punch received for biometric ID {BiometricId} at {PunchTime}",
                        punch.BiometricId,
                        punchTime);

                    break;

                default:

                    _logger.LogWarning(
                        "Unknown punch state {PunchState} received for biometric ID {BiometricId}",
                        punch.PunchState,
                        punch.BiometricId);

                    break;
            }
        }
        catch (Exception ex)
        {
            dbWriteSucceeded = false;

            _logger.LogError(
                ex,
                "Failed writing punch for biometric ID {BiometricId}, " +
                "CompanyID {CompanyId}, PunchState {PunchState}",
                punch.BiometricId,
                companyId,
                punch.PunchState);
        }

        if (!dbWriteSucceeded)
            return;

        var evt =
            await BuildAttendanceStatusEventAsync(
                connection,
                punch,
                payrollEmployee,
                employeeName,
                companyId,
                siteId,
                punchTime,
                attendanceDate);

        await _publisher.PublishAsync(evt);
    }
}
