using AttendanceBioApi.Data.Database;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.ViewModels;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Interfaces;
using AttendanceBioAPI.Service.ViewModels;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace AttendanceBioAPI.Service;

public class AttendanceQueryService : IAttendanceQueryService
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly AttendancePollerService _poller;
    private readonly IZkbioService _zkbioService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AttendanceQueryService> _logger;
    private readonly IBiometricDeviceRepository _biometricDeviceRepository;
    private readonly string _zkbioBaseUrl;

    public AttendanceQueryService(
        ISqlConnectionFactory sqlConnectionFactory,
        AttendancePollerService poller,
        IZkbioService zkbioService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AttendanceQueryService> logger,
        IBiometricDeviceRepository biometricDeviceRepository
        )
    {
        _sqlConnectionFactory = sqlConnectionFactory;
        _poller = poller;
        _zkbioService = zkbioService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _biometricDeviceRepository = biometricDeviceRepository;
        _zkbioBaseUrl = configuration["Zkbio:BaseUrl"] ?? "http://localhost:8088";
    }

    public async Task<CurrentStatusResponse> GetCurrentStatusAsync()
    {
        var today = DateTime.UtcNow.Date;
        var todayStr = today.ToString("yyyy-MM-dd");
        var devices = await _biometricDeviceRepository.GetAllAsync();

        var companyId = devices
            .Where(d => d.Enabled)
            .Select(d => d.CompanyId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        if (string.IsNullOrWhiteSpace(companyId))
        {
            throw new InvalidOperationException(
                "No active biometric device with a valid CompanyId was found.");
        }
        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        var detailSql = @"
            SELECT [EmployeeID], [EmployeeName], [TimeIn], [ClockOutTimeOut], [ClockedIn], [ClockedOut], [LatePeriod]
            FROM [dbo].[PayrollHRPayrollAttDetail]
            WHERE [CompanyID] = @CompanyId AND [AttendanceDate] = @AttendanceDate";

        var overtimeSql = @"
            SELECT [EmployeeID], 
                   SUM([Duration]) AS TotalActual, 
                   SUM([PlannedOvertime]) AS TotalPlanned
            FROM [dbo].[PayrollHRPayrollOvertimeDetail]
            WHERE [CompanyID] = @CompanyId AND [OvertimeDate] = @AttendanceDate
            GROUP BY [EmployeeID]";

        var masterHeadcountSql = @"
            SELECT COUNT(DISTINCT [EmployeeID])
            FROM [dbo].[PayrollEmployees]
            WHERE [ActiveYN] = 1 AND [EmployeeID] IS NOT NULL AND [EmployeeID] != ''";

        var queryParams = new { CompanyId = companyId, AttendanceDate = today };

        var detailRows = (await connection.QueryAsync(detailSql, queryParams)).ToList();
        var overtimeRows = (await connection.QueryAsync(overtimeSql, queryParams)).ToList();

        var totalEmployeesFiltered = await connection.ExecuteScalarAsync<int>(masterHeadcountSql, queryParams);

        var overtimeMap = overtimeRows.ToDictionary(
            r => (string)r.EmployeeID,
            r => (Actual: (double)(r.TotalActual ?? 0.0), Planned: (double)(r.TotalPlanned ?? 0.0))
        );

        var cachedEmployees = _poller.GetCachedEmployees();
        var employeeDeptMap = cachedEmployees.ToDictionary(e => e.EmpCode, e => e.Department?.DeptName ?? "DEFAULT");

        var events = detailRows.Select(r => {
            bool isPresent = Convert.ToInt32(r.ClockedIn) == 1 && Convert.ToInt32(r.ClockedOut) != 1;
            var empIdStr = Convert.ToString(r.EmployeeID);

            double actualOt = 0.0;
            double plannedOt = 0.0;
            if (overtimeMap.TryGetValue(empIdStr, out (double Actual, double Planned) otData))
            {
                actualOt = otData.Actual;
                plannedOt = otData.Planned;
            }

            return new AttendanceEventDto
            {
                EmployeeId = r.EmployeeID,
                EmployeeName = r.EmployeeName,
                Department = employeeDeptMap.TryGetValue((string)r.EmployeeID, out var dept) ? dept! : "DEFAULT",
                Status = isPresent ? "in" : "out",
                ClockInTime = r.TimeIn is not null ? ((DateTime)r.TimeIn).ToUniversalTime().ToString("o") : null,
                ClockOutTime = r.ClockOutTimeOut is not null ? ((DateTime)r.ClockOutTimeOut).ToUniversalTime().ToString("o") : null,
                Timestamp = (r.ClockOutTimeOut ?? r.TimeIn) is not null
                    ? ((DateTime)(r.ClockOutTimeOut ?? r.TimeIn)).ToUniversalTime().ToString("o")
                    : null,
                OverTime = actualOt,
                ActualOvertimeHours = actualOt,
                PlannedOvertimeHours = plannedOt
            };
        }).ToList();

        var totalPresent = detailRows.Count(r => Convert.ToInt32(r.ClockedIn) == 1 && Convert.ToInt32(r.ClockedOut) != 1);
        var lateArrivals = detailRows.Count(r => (r.LatePeriod ?? 0) > 0);

        var absentToday = Math.Max(0, totalEmployeesFiltered - totalPresent);

        var onLeaveCount = 0;
        try
        {
            var token = _zkbioService.GetAuthToken();
            if (token is not null)
            {
                var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{_zkbioBaseUrl}/att/api/leaves/?start_date={todayStr}&end_date={todayStr}");
                request.Headers.Add("Authorization", $"Token {token}");
                var leaveResponse = await client.SendAsync(request);
                if (leaveResponse.IsSuccessStatusCode)
                {
                    var leaveData = await leaveResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
                    if (leaveData is not null && leaveData.TryGetValue("count", out var count) && count is JsonElement countElement)
                    {
                        onLeaveCount = countElement.GetInt32();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Leave lookup failed");
        }

        return new CurrentStatusResponse
        {
            Metrics = new CurrentStatusMetrics
            {
                TotalPresent = totalPresent,
                LateArrivals = lateArrivals,
                AbsentToday = absentToday,
                OnLeave = onLeaveCount,
            },
            Events = events,
        };
    }

    public async Task<IEnumerable<EmployeeReportDto>> GetReportAsync(DateTime from, DateTime to, string? employeeId)
    {
        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        var sql = @"
            SELECT [id] AS Id, [employeeId] AS EmployeeId, [employeeName] AS EmployeeName,
                   [department] AS Department, [siteId] AS SiteId, [status] AS Status,
                   [timestamp] AS Timestamp, [createdAt] AS CreatedAt
            FROM [dbo].[v_attendance_transactions]
            WHERE [timestamp] BETWEEN @From AND @To" +
            (string.IsNullOrEmpty(employeeId) ? "" : " AND [employeeId] = @EmployeeId") +
            " ORDER BY [timestamp] ASC";

        var rows = (await connection.QueryAsync<AttendanceTransactionRow>(
            sql, new { From = from, To = to, EmployeeId = employeeId })).ToList();

        var employeeDeptMap = _poller.GetCachedEmployees()
            .ToDictionary(e => e.EmpCode, e => e.Department?.DeptName ?? "DEFAULT");

        var byEmployee = new Dictionary<string, (
            string EmployeeId,
            string EmployeeName,
            string Department,
            HashSet<string> DaysPresent,
            DateTime? FirstIn,
            DateTime? LastOut,
            int PunchCount)>();

        foreach (var row in rows)
        {
            var dayKey = row.Timestamp.ToString("yyyy-MM-dd");

            if (!byEmployee.TryGetValue(row.EmployeeId, out var entry))
            {
                entry = (
                    row.EmployeeId,
                    row.EmployeeName,
                    employeeDeptMap.TryGetValue(row.EmployeeId, out var dept) ? dept! : row.Department,
                    new HashSet<string>(),
                    null,
                    null,
                    0);
            }

            entry.DaysPresent.Add(dayKey);
            entry.PunchCount++;

            if (row.Status == "in" && (entry.FirstIn is null || row.Timestamp < entry.FirstIn))
            {
                entry.FirstIn = row.Timestamp;
            }
            if (row.Status == "out" && (entry.LastOut is null || row.Timestamp > entry.LastOut))
            {
                entry.LastOut = row.Timestamp;
            }

            byEmployee[row.EmployeeId] = entry;
        }

        return byEmployee.Values.Select(e => new EmployeeReportDto
        {
            EmployeeId = e.EmployeeId,
            EmployeeName = e.EmployeeName,
            Department = e.Department,
            DaysPresent = e.DaysPresent.Count,
            FirstIn = e.FirstIn,
            LastOut = e.LastOut,
            PunchCount = e.PunchCount,
        });
    }

    public async Task<IEnumerable<EmployeeSummaryDto>> GetEmployeesAsync()
    {
        var currentStatus = await GetCurrentStatusAsync();

        var statusMap = currentStatus.Events
            .Where(e => !string.IsNullOrEmpty(e.EmployeeId))
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        var employeeSql = @"
        SELECT DISTINCT 
               [EmployeeID] AS EmployeeId, 
               [EmployeeName] AS Name, 
               ISNULL([Department], 'Unassigned') AS Department
        FROM [dbo].[PayrollEmployees]
        WHERE [ActiveYN] = 1 AND [EmployeeID] IS NOT NULL AND [EmployeeID] != ''"
            + " ORDER BY [EmployeeName] ASC";

        var employeesList = (await connection.QueryAsync<PayrollEmployee>(
            employeeSql)).ToList();

        var uniqueEmployees = employeesList
            .GroupBy(emp => emp.EmployeeId)
            .Select(group => group.First())
            .ToList();

        return uniqueEmployees.Select(emp =>
        {
            statusMap.TryGetValue(emp.EmployeeId, out var status);
            return new EmployeeSummaryDto
            {
                EmployeeId = emp.EmployeeId,
                Name = emp.Name,
                Department = emp.Department,
                Status = status?.Status ?? "out",
                LastSeen = status?.Timestamp
            };
        }).ToList();
    }

    public async Task<string> GetLeavesAsync(string? startDate, string? endDate)
    {
        var token = _zkbioService.GetAuthToken();
        if (token is null) return "{\"count\":0,\"data\":[]}";

        var query = new List<string>();
        if (!string.IsNullOrEmpty(startDate)) query.Add($"start_date={Uri.EscapeDataString(startDate)}");
        if (!string.IsNullOrEmpty(endDate)) query.Add($"end_date={Uri.EscapeDataString(endDate)}");
        var queryString = query.Count > 0 ? "?" + string.Join("&", query) : "";

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_zkbioBaseUrl}/att/api/leaves/{queryString}");
        request.Headers.Add("Authorization", $"Token {token}");
        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Leaves fetch returned {Status}", response.StatusCode);
            return "{\"count\":0,\"data\":[]}";
        }

        return await response.Content.ReadAsStringAsync();
    }
}
