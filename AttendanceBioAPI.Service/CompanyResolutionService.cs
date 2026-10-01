using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using AttendanceBioApi.Data;
using AttendanceBioApi.Data.Database;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.Interfaces;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service;

public class CompanyResolutionService : BackgroundService, ICompanyResolutionService
{
    private readonly ILogger<CompanyResolutionService> _logger;
    private readonly IZkbioService _zkbioService;
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly int _refreshIntervalSeconds;

    private volatile Dictionary<int, ZkbioDepartment> _departmentCache = new();
    private volatile Dictionary<string, string> _companyNameMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _readyTcs =
    new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CompanyResolutionService(
        ILogger<CompanyResolutionService> logger,
        IZkbioService zkbioService,
        ISqlConnectionFactory sqlConnectionFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _zkbioService = zkbioService;
        _sqlConnectionFactory = sqlConnectionFactory;
        _refreshIntervalSeconds = int.TryParse(configuration["DepartmentPolling:IntervalSeconds"], out var secs) ? secs : 300;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_refreshIntervalSeconds));
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RefreshAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Department/company mapping refresh failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CompanyResolutionService is stopping.");
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var departments = await _zkbioService.GetDepartmentsAsync(ct);

        var newDepartmentCache = new Dictionary<int, ZkbioDepartment>();
        foreach (var dept in departments)
        {
            newDepartmentCache[dept.Id] = dept;
        }
        _departmentCache = newDepartmentCache;
        _logger.LogInformation("Cached {Count} ZKBio departments", newDepartmentCache.Count);

        using var connection = _sqlConnectionFactory.Create();
        await connection.OpenAsync();

        var mappings = await connection.QueryAsync<BiometricCompanyMapping>(@"
        SELECT [CompanyID], [BiometricSiteName]
        FROM [dbo].[PayrollHRPayrollCompanyStandard]
        WHERE [BiometricSiteName] IS NOT NULL AND [BiometricSiteName] != ''");

        var newCompanyNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.BiometricSiteName))
            {
                newCompanyNameMap[mapping.BiometricSiteName.Trim()] = mapping.CompanyID;
            }
        }
        _companyNameMap = newCompanyNameMap;
        _logger.LogInformation("Cached {Count} biometric site to CompanyID mappings", newCompanyNameMap.Count);

        _readyTcs.TrySetResult();
    }
    public Task<string?> ResolveCompanyIdAsync(ZkbioEmployee employee, CancellationToken ct)
    {
        if (employee.Department is null || employee.Department.Id <= 0)
        {
            _logger.LogWarning("Employee {EmpCode} has no resolvable department ID", employee.EmpCode);
            return Task.FromResult<string?>(null);
        }

        var currentId = employee.Department.Id;
        var visited = new HashSet<int>();

        while (visited.Add(currentId))
        {
            if (!_departmentCache.TryGetValue(currentId, out var dept))
            {
                _logger.LogWarning("Department {DeptId} not found in cache while resolving employee {EmpCode}", currentId, employee.EmpCode);
                return Task.FromResult<string?>(null);
            }

            if (!string.IsNullOrWhiteSpace(dept.DeptName) &&
                _companyNameMap.TryGetValue(dept.DeptName.Trim(), out var companyId))
            {
                return Task.FromResult<string?>(companyId);
            }

            if (dept.ParentDeptId is null)
            {
                _logger.LogWarning(
                    "No CompanyID mapping found for department chain starting at {DeptId} (employee {EmpCode}) — reached root '{RootName}' with no match",
                    employee.Department.Id, employee.EmpCode, dept.DeptName);
                return Task.FromResult<string?>(null);
            }

            currentId = dept.ParentDeptId.Value;
        }

        _logger.LogWarning("Circular department parent chain detected while resolving employee {EmpCode}", employee.EmpCode);
        return Task.FromResult<string?>(null);
    }

    public Task WaitUntilReadyAsync(CancellationToken ct) => _readyTcs.Task.WaitAsync(ct);
}

