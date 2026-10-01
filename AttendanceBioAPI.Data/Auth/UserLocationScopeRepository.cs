using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioApi.Models.Auth;
using Dapper;
using AttendanceBioApi.Data.Database;

namespace AttendanceBioAPI.Data.Auth;

public class UserLocationScopeRepository : IUserLocationScopeRepository
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public UserLocationScopeRepository(
        ISqlConnectionFactory sqlConnectionFactory)
    {
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public async Task<bool> HasAccessAsync(
        string username,
        string baseCompanyId,
        string baseDivisionId,
        string baseDepartmentId,
        string targetCompanyId,
        string targetDivisionId,
        string targetDepartmentId,
        string? targetBranchCode)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.UserLocationScopes
            WHERE Username = @Username
              AND BaseCompanyId = @BaseCompanyId
              AND BaseDivisionId = @BaseDivisionId
              AND BaseDepartmentId = @BaseDepartmentId
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
                Username = username,
                BaseCompanyId = baseCompanyId,
                BaseDivisionId = baseDivisionId,
                BaseDepartmentId = baseDepartmentId,
                TargetCompanyId = targetCompanyId,
                TargetDivisionId = targetDivisionId,
                TargetDepartmentId = targetDepartmentId,
                TargetBranchCode = targetBranchCode
            });

        return count > 0;
    }

    public async Task<IReadOnlyCollection<UserLocationScope>> GetScopesAsync(
        string username,
        string baseCompanyId)
    {
        const string sql = """
            SELECT
                LocationScopeId,
                BaseCompanyId,
                BaseDivisionId,
                BaseDepartmentId,
                TargetCompanyId,
                TargetDivisionId,
                TargetDepartmentId,
                TargetBranchCode,
                ScopeType,
                IsHome,
                IsActive
            FROM dbo.UserLocationScopes
            WHERE Username = @Username
              AND BaseCompanyId = @BaseCompanyId
              AND IsActive = 1
            ORDER BY IsHome DESC, TargetCompanyId;
            """;

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync();

        var result = await connection.QueryAsync<UserLocationScope>(
            sql,
            new
            {
                Username = username,
                BaseCompanyId = baseCompanyId
            });

        return result.ToList();
    }
}


