using AttendanceBioApi.Data.Database;
using AttendanceBioAPI.Data;
using AttendanceBioAPI.Data.Interfaces;
using Dapper;

namespace AttendanceBioAPI.Data.Auth;

public class RoleLocationScopeRepository : IRoleLocationScopeRepository
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public RoleLocationScopeRepository(
        ISqlConnectionFactory sqlConnectionFactory)
    {
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public async Task<bool> HasAccessAsync(
        string baseCompanyId,
        string baseDivisionId,
        string baseDepartmentId,
        int roleId,
        string targetCompanyId,
        string targetDivisionId,
        string targetDepartmentId,
        string? targetBranchCode)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.RoleLocationScopes
            WHERE BaseCompanyId = @BaseCompanyId
              AND BaseDivisionId = @BaseDivisionId
              AND BaseDepartmentId = @BaseDepartmentId
              AND RoleId = @RoleId
              AND TargetCompanyId = @TargetCompanyId
              AND TargetDivisionId = @TargetDivisionId
              AND TargetDepartmentId = @TargetDepartmentId
              AND IsActive = 1
              AND (
                    TargetBranchCode IS NULL
                    OR TargetBranchCode = @TargetBranchCode
                  );
            """;

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync();

        var count = await connection.ExecuteScalarAsync<int>(
            sql,
            new
            {
                BaseCompanyId = baseCompanyId,
                BaseDivisionId = baseDivisionId,
                BaseDepartmentId = baseDepartmentId,
                RoleId = roleId,
                TargetCompanyId = targetCompanyId,
                TargetDivisionId = targetDivisionId,
                TargetDepartmentId = targetDepartmentId,
                TargetBranchCode = targetBranchCode
            });

        return count > 0;
    }
}


