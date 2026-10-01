using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using AttendanceBioApi.Data.Database;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Interfaces;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service;

public class DeviceSyncService : IDeviceSyncService
{
    private const string CheckIn = "0";
    private const string CheckOut = "1";
    private const string BreakIn = "3";
    private const string OtIn = "4";
    private const string OtOut = "5";

    private const string SchemaSql = @"
IF OBJECT_ID('[dbo].[BiometricDeviceSyncState]', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BiometricDeviceSyncState]
    (
        [DeviceId] NVARCHAR(64) NOT NULL PRIMARY KEY,
        [LastPunchTime] DATETIME NULL,
        [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE()
    )
END

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('[dbo].[BiometricDeviceSyncState]')
      AND name = 'LastPunchTime' AND is_nullable = 0)
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ALTER COLUMN [LastPunchTime] DATETIME NULL

IF COL_LENGTH('dbo.BiometricDeviceSyncState', 'LastAttemptAt') IS NULL
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ADD [LastAttemptAt] DATETIME NULL

IF COL_LENGTH('dbo.BiometricDeviceSyncState', 'LastSuccessAt') IS NULL
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ADD [LastSuccessAt] DATETIME NULL

IF COL_LENGTH('dbo.BiometricDeviceSyncState', 'LastStatus') IS NULL
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ADD [LastStatus] NVARCHAR(20) NULL

IF COL_LENGTH('dbo.BiometricDeviceSyncState', 'LastError') IS NULL
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ADD [LastError] NVARCHAR(500) NULL

IF COL_LENGTH('dbo.BiometricDeviceSyncState', 'LastFetched') IS NULL
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ADD [LastFetched] INT NULL

IF COL_LENGTH('dbo.BiometricDeviceSyncState', 'LastProcessed') IS NULL
    ALTER TABLE [dbo].[BiometricDeviceSyncState] ADD [LastProcessed] INT NULL
";

    private static readonly DateTime EmptyLogBaseline = new(2000, 1, 1);
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(1);
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WarningWindow = TimeSpan.FromMinutes(60);

    private readonly ILogger<DeviceSyncService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly IAttendanceEventsPublisher _publisher;
    private readonly IEmployeeResolutionService _employeeResolutionService;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public DeviceSyncService(
        ILogger<DeviceSyncService> logger,
        IServiceScopeFactory scopeFactory,
        ISqlConnectionFactory sqlConnectionFactory,
        IAttendanceEventsPublisher publisher,
        IEmployeeResolutionService employeeResolutionService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _sqlConnectionFactory = sqlConnectionFactory;
        _publisher = publisher;
        _employeeResolutionService = employeeResolutionService;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureSchemaAsync(cancellationToken);

    public async Task<IReadOnlyCollection<DeviceSyncResult>> SyncAutomaticDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IBiometricDeviceRepository>();

        var devices = await repository.GetAllAsync(cancellationToken);

        var results = new List<DeviceSyncResult>();

        foreach (var device in devices.Where(d => d.Enabled && d.AutomaticSync))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(
                    await SyncDeviceAsync(device.DeviceId, false, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Automatic sync failed for biometric device {DeviceName} ({DeviceId})",
                    device.DeviceName,
                    device.DeviceId);
            }
        }

        return results;
    }

    public async Task<DeviceSyncResult> SyncDeviceAsync(
        string deviceId,
        bool manual,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var result = new DeviceSyncResult
        {
            DeviceId = deviceId,
            StartedAt = DateTime.UtcNow
        };

        var gate = GetLock(deviceId);

        if (!await gate.WaitAsync(0, cancellationToken))
        {
            result.Status = "Busy";
            result.Message = "A sync is already running for this device.";
            result.FinishedAt = DateTime.UtcNow;

            return result;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<IBiometricDeviceRepository>();
            var factory = scope.ServiceProvider.GetRequiredService<IBiometricDeviceFactory>();

            var config = await repository.GetByIdAsync(deviceId, cancellationToken);

            if (config is null)
            {
                result.Status = "NotFound";
                result.Message = "Biometric device not found.";
                result.FinishedAt = DateTime.UtcNow;

                return result;
            }

            result.DeviceName = config.DeviceName;

            if (!config.Enabled)
            {
                result.Status = "Disabled";
                result.Message = "The device is disabled. Enable it to sync.";
                result.FinishedAt = DateTime.UtcNow;

                return result;
            }

            try
            {
                await RunSyncAsync(factory, config, result, cancellationToken);

                result.Status = "Success";

                if (string.IsNullOrWhiteSpace(result.Message))
                {
                    result.Message = result.NewPunches == 0
                        ? "No new punches."
                        : $"{result.Processed} of {result.NewPunches} new punch(es) processed.";
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Message = ToFriendlyError(ex);

                _logger.LogError(
                    ex,
                    "{Trigger} sync failed for device {DeviceName} ({DeviceId})",
                    manual ? "Manual" : "Automatic",
                    config.DeviceName,
                    config.DeviceId);
            }

            result.FinishedAt = DateTime.UtcNow;

            await SaveAttemptAsync(result);

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<DeviceSyncStatus>> GetStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IBiometricDeviceRepository>();

        var devices = await repository.GetAllAsync(cancellationToken);

        Dictionary<string, SyncStateRow> rows;

        using (var connection = _sqlConnectionFactory.Create())
        {
            await connection.OpenAsync(cancellationToken);

            var list = await connection.QueryAsync<SyncStateRow>(@"
                SELECT
                    [DeviceId], [LastPunchTime], [LastAttemptAt], [LastSuccessAt],
                    [LastStatus], [LastError], [LastFetched], [LastProcessed]
                FROM [dbo].[BiometricDeviceSyncState]");

            rows = list.ToDictionary(r => r.DeviceId, StringComparer.OrdinalIgnoreCase);
        }

        var utcNow = DateTime.UtcNow;

        var statuses = new List<DeviceSyncStatus>();

        foreach (var device in devices)
        {
            rows.TryGetValue(device.DeviceId, out var row);

            var lastSuccess = AsUtc(row?.LastSuccessAt);

            string connectionStatus;

            if (!device.Enabled)
            {
                connectionStatus = "Disabled";
            }
            else if (row?.LastStatus is null)
            {
                connectionStatus = "Unknown";
            }
            else if (row.LastStatus == "Success")
            {
                connectionStatus = "Online";
            }
            else if (lastSuccess.HasValue && utcNow - lastSuccess.Value < WarningWindow)
            {
                connectionStatus = "Warning";
            }
            else
            {
                connectionStatus = "Failed";
            }

            statuses.Add(new DeviceSyncStatus
            {
                DeviceId = device.DeviceId,
                ConnectionStatus = connectionStatus,
                IsSyncing = _locks.TryGetValue(device.DeviceId, out var gate) && gate.CurrentCount == 0,
                LastAttemptAt = AsUtc(row?.LastAttemptAt),
                LastSuccessAt = lastSuccess,
                LastPunchTime = row?.LastPunchTime,
                LastStatus = row?.LastStatus,
                LastError = row?.LastError,
                LastFetched = row?.LastFetched ?? 0,
                LastProcessed = row?.LastProcessed ?? 0
            });
        }

        return statuses;
    }

    private async Task RunSyncAsync(
        IBiometricDeviceFactory factory,
        BiometricDevice config,
        DeviceSyncResult result,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCts.CancelAfter(SyncTimeout);

        var device = await factory.CreateAsync(config.DeviceId, cancellationToken);

        if (device is null)
        {
            throw new InvalidOperationException("The device driver could not be created.");
        }

        if (!await device.ConnectAsync(timeoutCts.Token))
        {
            throw new InvalidOperationException(
                $"Could not connect to {config.IpAddress}:{config.Port}. Check that the device is powered on and reachable.");
        }

        IReadOnlyCollection<AttendancePunch> punches;

        try
        {
            punches = await device.GetAttendancePunchesAsync(timeoutCts.Token);
        }
        finally
        {
            await device.DisconnectAsync();
        }

        var latestAllowed = DateTime.Now.Add(FutureTolerance);

        var ordered = punches
            .Where(p => p.PunchTime <= latestAllowed)
            .OrderBy(p => p.PunchTime)
            .ToList();

        var ignoredFuture = punches.Count - ordered.Count;

        if (ignoredFuture > 0)
        {
            _logger.LogWarning(
                "Ignored {Count} punch(es) with future timestamps from device {DeviceId}. Check the device clock.",
                ignoredFuture,
                config.DeviceId);
        }

        result.Fetched = ordered.Count;

        var watermark = await GetLastPunchTimeAsync(config.DeviceId);

        if (watermark is null)
        {
            var baseline = ordered.Count > 0
                ? ordered[^1].PunchTime
                : EmptyLogBaseline;

            await SaveLastPunchTimeAsync(config.DeviceId, baseline);

            result.Message = $"First sync: {ordered.Count} historical punch(es) skipped. Only new punches will be processed from now on.";

            _logger.LogInformation(
                "First sync for device {DeviceName} ({DeviceId}). {Count} historical punch(es) skipped. Baseline set to {Baseline:yyyy-MM-dd HH:mm:ss}.",
                config.DeviceName,
                config.DeviceId,
                ordered.Count,
                baseline);

            return;
        }

        var fresh = ordered
            .Where(p => p.PunchTime > watermark.Value)
            .ToList();

        result.NewPunches = fresh.Count;

        if (fresh.Count == 0)
        {
            _logger.LogDebug(
                "No new punches on device {DeviceId}. Log size={Total}, watermark={Watermark:yyyy-MM-dd HH:mm:ss}",
                config.DeviceId,
                ordered.Count,
                watermark.Value);

            return;
        }

        _logger.LogInformation(
            "Device {DeviceName} ({DeviceId}): {New} new punch(es) since {Watermark:yyyy-MM-dd HH:mm:ss}",
            config.DeviceName,
            config.DeviceId,
            fresh.Count,
            watermark.Value);

        var lastProcessed = watermark.Value;
        var failedPunch = false;

        foreach (var punch in fresh)
        {
            var handled = await ProcessPunchAsync(config, punch, cancellationToken);

            if (!handled)
            {
                _logger.LogWarning(
                    "Stopping batch for device {DeviceId} at punch {BiometricId} {PunchTime:yyyy-MM-dd HH:mm:ss}. It will be retried on the next sync.",
                    config.DeviceId,
                    punch.BiometricId,
                    punch.PunchTime);

                failedPunch = true;

                break;
            }

            lastProcessed = punch.PunchTime;
            result.Processed++;
        }

        if (lastProcessed > watermark.Value)
        {
            await SaveLastPunchTimeAsync(config.DeviceId, lastProcessed);
        }

        result.Failed = fresh.Count - result.Processed;

        _logger.LogInformation(
            "Processed {Processed}/{Total} new punch(es) from device {DeviceId}",
            result.Processed,
            fresh.Count,
            config.DeviceId);

        if (failedPunch)
        {
            throw new InvalidOperationException(
                $"{result.Processed} of {fresh.Count} punch(es) saved. A punch could not be written to payroll and will be retried on the next sync. Check the server logs.");
        }
    }

    private SemaphoreSlim GetLock(string deviceId) =>
        _locks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken);

        try
        {
            if (_schemaReady)
            {
                return;
            }

            using var connection = _sqlConnectionFactory.Create();

            await connection.OpenAsync(cancellationToken);

            await connection.ExecuteAsync(SchemaSql);

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private async Task<DateTime?> GetLastPunchTimeAsync(string deviceId)
    {
        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(@"
            SELECT [LastPunchTime] FROM [dbo].[BiometricDeviceSyncState]
            WHERE [DeviceId] = @DeviceId",
            new { DeviceId = deviceId });
    }

    private async Task SaveLastPunchTimeAsync(string deviceId, DateTime lastPunchTime)
    {
        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[BiometricDeviceSyncState]
            SET [LastPunchTime] = @LastPunchTime, [UpdatedAt] = GETDATE()
            WHERE [DeviceId] = @DeviceId

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO [dbo].[BiometricDeviceSyncState] ([DeviceId], [LastPunchTime], [UpdatedAt])
                VALUES (@DeviceId, @LastPunchTime, GETDATE())
            END",
            new { DeviceId = deviceId, LastPunchTime = lastPunchTime });
    }

    private async Task SaveAttemptAsync(DeviceSyncResult result)
    {
        try
        {
            var success = result.Status == "Success";

            var error = success
                ? null
                : result.Message.Length > 500
                    ? result.Message[..500]
                    : result.Message;

            using var connection = _sqlConnectionFactory.Create();
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                UPDATE [dbo].[BiometricDeviceSyncState]
                SET [LastAttemptAt] = @Now,
                    [LastSuccessAt] = CASE WHEN @IsSuccess = 1 THEN @Now ELSE [LastSuccessAt] END,
                    [LastStatus] = @Status,
                    [LastError] = @Error,
                    [LastFetched] = @Fetched,
                    [LastProcessed] = @Processed,
                    [UpdatedAt] = GETDATE()
                WHERE [DeviceId] = @DeviceId

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO [dbo].[BiometricDeviceSyncState]
                    ([DeviceId], [LastPunchTime], [UpdatedAt], [LastAttemptAt], [LastSuccessAt], [LastStatus], [LastError], [LastFetched], [LastProcessed])
                    VALUES
                    (@DeviceId, NULL, GETDATE(), @Now, CASE WHEN @IsSuccess = 1 THEN @Now ELSE NULL END, @Status, @Error, @Fetched, @Processed)
                END",
                new
                {
                    DeviceId = result.DeviceId,
                    Now = DateTime.UtcNow,
                    IsSuccess = success,
                    Status = success ? "Success" : "Failed",
                    Error = error,
                    Fetched = result.Fetched,
                    Processed = result.Processed
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not record the sync attempt for device {DeviceId}",
                result.DeviceId);
        }
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;

    private static string ToFriendlyError(Exception ex) => ex switch
    {
        OperationCanceledException => "Connection timed out after 30 seconds.",
        SocketException => "The device is unreachable on the network.",
        IOException => "The device closed the connection or stopped responding.",
        _ => ex.Message
    };

    private async Task<bool> ProcessPunchAsync(
        BiometricDevice deviceConfig,
        AttendancePunch punch,
        CancellationToken ct)
    {
        var siteId = deviceConfig.BranchCode;

        var payrollEmployee = await _employeeResolutionService.ResolveByBiometricIdAsync(
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
                    EmployeeName = punch.BiometricId,
                    Reason = "EmployeeNotResolved",
                    Message =
                        $"Biometric ID {punch.BiometricId} on device '{deviceConfig.DeviceName}' could not be matched to exactly one active payroll employee. Please check PayrollEmployees.EmployeeBiometricID.",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

            return true;
        }

        var companyId = payrollEmployee.CompanyId?.Trim();

        if (string.IsNullOrWhiteSpace(companyId))
        {
            _logger.LogError(
                "Cannot process punch for biometric ID {BiometricId}: payroll employee {EmployeeId} has no CompanyID.",
                punch.BiometricId,
                payrollEmployee.EmployeeId);

            await _publisher.PublishAlertAsync(
                new AttendanceProcessingAlert
                {
                    EmployeeCode = punch.BiometricId,
                    EmployeeName = punch.BiometricId,
                    Reason = "CompanyNotResolved",
                    Message =
                        $"Biometric ID {punch.BiometricId}'s punch couldn't be matched to a company because the payroll employee record has no CompanyID. Please contact IT.",
                    Timestamp = DateTime.UtcNow.ToString("o")
                });

            return true;
        }

        var punchTime = punch.PunchTime;
        var attendanceDate = punchTime.Date;

        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        try
        {
            switch (punch.PunchState)
            {
                case CheckIn:
                    await HandleCheckInAsync(
                        connection,
                        payrollEmployee,
                        companyId,
                        siteId,
                        punchTime,
                        attendanceDate);

                    break;

                case CheckOut:
                    await HandleCheckOutAsync(
                        connection,
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
                        "Break-in punch received for employee {EmpCode} at {PunchTime}",
                        punch.BiometricId,
                        punchTime);

                    break;

                default:
                    _logger.LogWarning(
                        "Unknown punch state {PunchState} received for employee {EmpCode}",
                        punch.PunchState,
                        punch.BiometricId);

                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed writing punch for employee {EmpCode}, CompanyID {CompanyId}, PunchState {PunchState}",
                punch.BiometricId,
                companyId,
                punch.PunchState);

            return false;
        }

        try
        {
            var evt = await BuildAttendanceStatusEventAsync(
                connection,
                punch.PunchState,
                payrollEmployee,
                deviceConfig,
                companyId,
                attendanceDate);

            await _publisher.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Punch for employee {EmpCode} was saved but publishing the attendance event failed",
                punch.BiometricId);
        }

        return true;
    }

    private async Task HandleCheckInAsync(
        System.Data.IDbConnection connection,
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

        var employeeName = payrollEmployee.EmployeeName ?? $"Employee {payrollEmployee.EmployeeId}";

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
        string punchState,
        PayrollEmployeeBiometric payrollEmployee,
        BiometricDevice deviceConfig,
        string companyId,
        DateTime attendanceDate)
    {
        var dayRecord = await connection.QueryFirstOrDefaultAsync<PayrollAttStatus>(@"
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

        var overtime = await connection.QueryFirstOrDefaultAsync<Overtime>(@"
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

        var isIn = punchState is CheckIn or BreakIn or OtIn;

        return new AttendanceStatusEvent
        {
            EmployeeId = payrollEmployee.EmployeeId,

            EmployeeName =
                payrollEmployee.EmployeeName ?? $"Employee {payrollEmployee.EmployeeId}",

            Department =
                string.IsNullOrWhiteSpace(payrollEmployee.Department)
                    ? "DEFAULT"
                    : payrollEmployee.Department,

            SiteId = deviceConfig.BranchCode,

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
}
