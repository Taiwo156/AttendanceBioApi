using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Globalization;
using AttendanceBioApi.Data;
using AttendanceBioApi.Data.Database;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.Interfaces;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service;

public class OvertimePollerService : BackgroundService
{
    private readonly ILogger<OvertimePollerService> _logger;
    private readonly IZkbioService _zkbioService;
    private readonly ICompanyResolutionService _companyResolutionService;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly IAttendanceEventsPublisher _publisher;
    private readonly int _pollIntervalSeconds;

    private readonly ConcurrentDictionary<string, ZkbioEmployee> _employeeCache = new();
    private readonly ConcurrentDictionary<int, byte> _seenOvertimeIds = new();

    public OvertimePollerService(
        ILogger<OvertimePollerService> logger,
        IZkbioService zkbioService,
        ICompanyResolutionService companyResolutionService,
        ISqlConnectionFactory sqlConnectionFactory,
        IAttendanceEventsPublisher publisher,
        IConfiguration configuration)
    {
        _logger = logger;
        _zkbioService = zkbioService;
        _companyResolutionService = companyResolutionService;
        _sqlConnectionFactory = sqlConnectionFactory;
        _publisher = publisher;
        _pollIntervalSeconds = int.TryParse(configuration["OvertimePolling:IntervalSeconds"], out var secs) ? secs : 30;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollIntervalSeconds));

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RefreshEmployeeCacheAsync(stoppingToken);
                    await PollForApprovedOvertimeAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Overtime poll iteration failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OvertimePollerService is stopping.");
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
    }

    private async Task PollForApprovedOvertimeAsync(CancellationToken ct)
    {
        var records = await _zkbioService.GetOvertimesAsync(DateTime.UtcNow.Date, ct);

        foreach (var rec in records)
        {
            if (rec.ApprovalStatus != 2) continue;
            if (!_seenOvertimeIds.TryAdd(rec.Id, 0)) continue;
            await WritePlannedOvertimeAsync(rec, ct);
        }
    }

    //private async Task WritePlannedOvertimeAsync(ZkbioOvertime rec, CancellationToken ct)
    //{
    //    if (!_employeeCache.TryGetValue(rec.EmpCode, out var employee))
    //    {
    //        _logger.LogWarning("Employee {EmpCode} was not found in ZKBio employee cache — skipping overtime record {Id}", rec.EmpCode, rec.Id);
    //        return;
    //    }

    //    if (employee.Department is null)
    //    {
    //        _logger.LogWarning("Employee {EmpCode} has no department assigned in ZKBio — skipping overtime record {Id}", rec.EmpCode, rec.Id);
    //        return;
    //    }

    //    var companyId = await _companyResolutionService.ResolveCompanyIdAsync(employee, ct);
    //    if (string.IsNullOrWhiteSpace(companyId))
    //    {
    //        _logger.LogError("Cannot process overtime for employee {EmpCode}: CompanyID could not be resolved.", rec.EmpCode);
    //        return;
    //    }

    //    var start = DateTime.Parse(rec.StartTime);
    //    var end = DateTime.Parse(rec.EndTime);
    //    var plannedHours = Math.Max(0, (end - start).TotalHours);
    //    var overtimeDate = start.Date;
    //    var overtimeTypeId = rec.OvertimeType.ToString();
    //    const string payTypeId = "Overtime";

    //    using var connection = _sqlConnectionFactory.Create();
    //    await connection.OpenAsync();

    //    try
    //    {
    //        await connection.ExecuteAsync(@"
    //            IF NOT EXISTS (
    //                SELECT 1 FROM [dbo].[PayrollHRPayrollOvertimeHeader]
    //                WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
    //                  AND [EmployeeID] = @EmployeeId AND [PayTypeID] = @PayTypeId
    //            )
    //            BEGIN
    //                INSERT INTO [dbo].[PayrollHRPayrollOvertimeHeader]
    //                ([CompanyID], [DivisionID], [DepartmentID], [EmployeeID], [PayTypeID], [Total])
    //                VALUES (@CompanyId, 'DEFAULT', 'DEFAULT', @EmployeeId, @PayTypeId, 0)
    //            END",
    //            new { CompanyId = companyId, EmployeeId = rec.EmpCode, PayTypeId = payTypeId });

    //        await connection.ExecuteAsync(@"
    //            IF NOT EXISTS (
    //                SELECT 1 FROM [dbo].[PayrollHRPayrollOvertimeDetail]
    //                WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
    //                  AND [EmployeeID] = @EmployeeId AND [OvertimeTypeID] = @OvertimeTypeId AND [OvertimeDate] = @OvertimeDate
    //            )
    //            BEGIN
    //                INSERT INTO [dbo].[PayrollHRPayrollOvertimeDetail]
    //                ([CompanyID], [DivisionID], [DepartmentID], [EmployeeID], [OvertimeTypeID], [OvertimeDate], [Duration], [PlannedOvertime],[OvertimeStartTime])
    //                VALUES (@CompanyId, 'DEFAULT', 'DEFAULT', @EmployeeId, @OvertimeTypeId, @OvertimeDate, 0, @PlannedHours, @OtStartTime)
    //            END
    //            ELSE
    //            BEGIN
    //                UPDATE [dbo].[PayrollHRPayrollOvertimeDetail]
    //                SET [PlannedOvertime] = @PlannedHours
    //                WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
    //                  AND [EmployeeID] = @EmployeeId AND [OvertimeTypeID] = @OvertimeTypeId AND [OvertimeDate] = @OvertimeDate
    //            END",
    //            new { CompanyId = companyId, EmployeeId = rec.EmpCode, OvertimeTypeId = overtimeTypeId, OvertimeDate = overtimeDate, PlannedHours = plannedHours, OtStartTime = start });

    //        var dayRecord = await connection.QueryFirstOrDefaultAsync<(DateTime? TimeIn, DateTime? ClockOutTimeOut, bool? ClockedIn)?>(@"
    //            SELECT [TimeIn], [ClockOutTimeOut], [ClockedIn] FROM [dbo].[PayrollHRPayrollAttDetail]
    //            WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
    //              AND [AttendanceDate] = @OvertimeDate AND [EmployeeID] = @EmployeeId",
    //            new { CompanyId = companyId, OvertimeDate = overtimeDate, EmployeeId = rec.EmpCode });

    //        var otSum = await connection.QueryFirstOrDefaultAsync<double?>(@"
    //            SELECT SUM([Duration]) FROM [dbo].[PayrollHRPayrollOvertimeDetail]
    //            WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
    //              AND [OvertimeDate] = @OvertimeDate AND [EmployeeID] = @EmployeeId",
    //            new { CompanyId = companyId, OvertimeDate = overtimeDate, EmployeeId = rec.EmpCode });

    //        var evt = new AttendanceStatusEvent
    //        {
    //            EmployeeId = rec.EmpCode,
    //            EmployeeName = employee.FullName ?? $"Employee {rec.EmpCode}",
    //            Department = employee.Department?.DeptName ?? "DEFAULT",
    //            SiteId = "site-1",
    //            Status = dayRecord?.ClockedIn == true ? "in" : "out",
    //            ClockinTime = dayRecord?.TimeIn?.ToUniversalTime().ToString("o"),
    //            ClockoutTime = dayRecord?.ClockOutTimeOut?.ToUniversalTime().ToString("o"),
    //            OtStartTime = start.ToUniversalTime().ToString("o"),
    //            OverTime = otSum.HasValue ? otSum.Value.ToString(CultureInfo.InvariantCulture) : null,
    //        };

    //        await _publisher.PublishAsync(evt);
    //        _logger.LogInformation("Recorded planned overtime for emp {EmpCode}, CompanyID {CompanyId}: {Hours}h", rec.EmpCode, companyId, plannedHours.ToString("0.00"));
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Failed writing planned overtime for employee {EmpCode}, CompanyID {CompanyId}", rec.EmpCode, companyId);
    //    }
    //}

    private async Task WritePlannedOvertimeAsync(ZkbioOvertime rec, CancellationToken ct)
    {
        if (!_employeeCache.TryGetValue(rec.EmpCode, out var employee))
        {
            _logger.LogWarning("Employee {EmpCode} was not found in ZKBio employee cache — skipping overtime record {Id}", rec.EmpCode, rec.Id);
            return;
        }

        if (employee.Department is null)
        {
            _logger.LogWarning("Employee {EmpCode} has no department assigned in ZKBio — skipping overtime record {Id}", rec.EmpCode, rec.Id);
            return;
        }

        var companyId = await _companyResolutionService.ResolveCompanyIdAsync(employee, ct);
        if (string.IsNullOrWhiteSpace(companyId))
        {
            _logger.LogError("Cannot process overtime for employee {EmpCode}: CompanyID could not be resolved.", rec.EmpCode);
            return;
        }

        var start = DateTime.Parse(rec.StartTime);
        var end = DateTime.Parse(rec.EndTime);
        var plannedHours = Math.Max(0, (end - start).TotalHours);
        var overtimeDate = start.Date;
        var overtimeTypeId = rec.OvertimeType.ToString();
        const string payTypeId = "Overtime";

        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        try
        {
            await connection.ExecuteAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[PayrollHRPayrollOvertimeHeader]
                    WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
                      AND [EmployeeID] = @EmployeeId AND [PayTypeID] = @PayTypeId
                )
                BEGIN
                    INSERT INTO [dbo].[PayrollHRPayrollOvertimeHeader]
                    ([CompanyID], [DivisionID], [DepartmentID], [EmployeeID], [PayTypeID], [Total])
                    VALUES (@CompanyId, 'DEFAULT', 'DEFAULT', @EmployeeId, @PayTypeId, 0)
                END",
                new { CompanyId = companyId, EmployeeId = rec.EmpCode, PayTypeId = payTypeId });

            await connection.ExecuteAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[PayrollHRPayrollOvertimeDetail]
                    WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
                      AND [EmployeeID] = @EmployeeId AND [OvertimeTypeID] = @OvertimeTypeId AND [OvertimeDate] = @OvertimeDate
                )
                BEGIN
                    INSERT INTO [dbo].[PayrollHRPayrollOvertimeDetail]
                    ([CompanyID], [DivisionID], [DepartmentID], [EmployeeID], [OvertimeTypeID], [OvertimeDate], [Duration], [PlannedOvertime], [PlannedOvertimeStartTime], [PlannedOvertimeEndTime])
                    VALUES (@CompanyId, 'DEFAULT', 'DEFAULT', @EmployeeId, @OvertimeTypeId, @OvertimeDate, 0, @PlannedHours, @PlannedStart, @PlannedEnd)
                END
                ELSE
                BEGIN
                    UPDATE [dbo].[PayrollHRPayrollOvertimeDetail]
                    SET [PlannedOvertime] = @PlannedHours,
                        [PlannedOvertimeStartTime] = @PlannedStart,
                        [PlannedOvertimeEndTime] = @PlannedEnd
                    WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
                      AND [EmployeeID] = @EmployeeId AND [OvertimeTypeID] = @OvertimeTypeId AND [OvertimeDate] = @OvertimeDate
                END",
                new
                {
                    CompanyId = companyId,
                    EmployeeId = rec.EmpCode,
                    OvertimeTypeId = overtimeTypeId,
                    OvertimeDate = overtimeDate,
                    PlannedHours = plannedHours,
                    PlannedStart = start,
                    PlannedEnd = end
                });

            var dayRecord = await connection.QueryFirstOrDefaultAsync<(DateTime? TimeIn, DateTime? ClockOutTimeOut, bool? ClockedIn)?>(@"
                SELECT [TimeIn], [ClockOutTimeOut], [ClockedIn] FROM [dbo].[PayrollHRPayrollAttDetail]
                WHERE [CompanyID] = @CompanyId AND [DivisionID] = 'DEFAULT' AND [DepartmentID] = 'DEFAULT'
                  AND [AttendanceDate] = @OvertimeDate AND [EmployeeID] = @EmployeeId",
                new { CompanyId = companyId, OvertimeDate = overtimeDate, EmployeeId = rec.EmpCode });

            var overtimeSummary = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT
                    SUM([Duration]) AS TotalDuration,
                    MAX([ActualOvertimeStartTime]) AS ActualOvertimeStartTime,
                    MAX([ActualOvertimeEndTime]) AS ActualOvertimeEndTime
                FROM [dbo].[PayrollHRPayrollOvertimeDetail]
                WHERE [CompanyID] = @CompanyId
                  AND [DivisionID] = 'DEFAULT'
                  AND [DepartmentID] = 'DEFAULT'
                  AND [OvertimeDate] = @OvertimeDate
                  AND [EmployeeID] = @EmployeeId",
                new
                {
                    CompanyId = companyId,
                    OvertimeDate = overtimeDate,
                    EmployeeId = rec.EmpCode
                });

            var evt = new AttendanceStatusEvent
            {
                EmployeeId = rec.EmpCode,
                EmployeeName = employee.FullName ?? $"Employee {rec.EmpCode}",
                Department = employee.Department?.DeptName ?? "DEFAULT",
                SiteId = "site-1",
                Status = dayRecord?.ClockedIn == true ? "in" : "out",
                ClockinTime = dayRecord?.TimeIn?.ToUniversalTime().ToString("o"),
                ClockoutTime = dayRecord?.ClockOutTimeOut?.ToUniversalTime().ToString("o"),

                PlannedOtStartTime = start.ToUniversalTime().ToString("o"),
                PlannedOtEndTime = end.ToUniversalTime().ToString("o"),

                ActualOtStartTime =
                    overtimeSummary?.ActualOvertimeStartTime is DateTime actualStart
                        ? actualStart.ToUniversalTime().ToString("o")
                        : null,

                ActualOtEndTime =
                    overtimeSummary?.ActualOvertimeEndTime is DateTime actualEnd
                        ? actualEnd.ToUniversalTime().ToString("o")
                        : null,

                OverTime =
                    overtimeSummary?.TotalDuration != null
                        ? ((double)overtimeSummary.TotalDuration)
                            .ToString(CultureInfo.InvariantCulture)
                        : null,
            };

            await _publisher.PublishAsync(evt);
            _logger.LogInformation("Recorded planned overtime for emp {EmpCode}, CompanyID {CompanyId}: {Hours}h", rec.EmpCode, companyId, plannedHours.ToString("0.00"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed writing planned overtime for employee {EmpCode}, CompanyID {CompanyId}", rec.EmpCode, companyId);
        }
    }

}

