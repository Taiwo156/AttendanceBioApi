using AttendanceBioApi.Data;
using AttendanceBioApi.Data.Database;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service;

public class EmployeeResolutionService : IEmployeeResolutionService
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;
    private readonly ILogger<EmployeeResolutionService> _logger;

    public EmployeeResolutionService(
        ISqlConnectionFactory sqlConnectionFactory,
        ILogger<EmployeeResolutionService> logger)
    {
        _sqlConnectionFactory = sqlConnectionFactory;
        _logger = logger;
    }

    public async Task<PayrollEmployeeBiometric?> ResolveByBiometricIdAsync(
        string biometricId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(biometricId))
        {
            return null;
        }

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync(cancellationToken);

        var employees = await connection.QueryAsync<PayrollEmployeeBiometric>(@"
            SELECT
                [CompanyID] AS CompanyId,
                [DivisionID] AS DivisionId,
                [DepartmentID] AS DepartmentId,
                [EmployeeID] AS EmployeeId,
                [EmployeeName] AS EmployeeName,
                CAST([EmployeeBiometricID] AS nvarchar(100)) AS EmployeeBiometricId,
                [Department] AS Department,
                [BranchCode] AS BranchCode,

                CASE
                    WHEN LOWER(LTRIM(RTRIM(CAST([ActiveYN] AS nvarchar(20))))) 
                         IN ('1', 'yes', 'y', 'true')
                    THEN CAST(1 AS bit)
                    ELSE CAST(0 AS bit)
                END AS Active

            FROM [dbo].[PayrollEmployees]

            WHERE CAST([EmployeeBiometricID] AS nvarchar(100)) = @BiometricId

              AND LOWER(LTRIM(RTRIM(CAST([ActiveYN] AS nvarchar(20)))))
                  IN ('1', 'yes', 'y', 'true')
        ", new
        {
            BiometricId = biometricId
        });

        var matches = employees.ToList();

        if (matches.Count == 0)
        {
            _logger.LogWarning(
                "No active payroll employee found for biometric ID {BiometricId}",
                biometricId);

            return null;
        }

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "Multiple active payroll employees found for biometric ID {BiometricId}. Employee resolution requires a company/site context.",
                biometricId);
        }

        return matches[0];
    }
}
