using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioApi.Models.Auth;
using Dapper;
using AttendanceBioApi.Data.Database;

namespace AttendanceBioAPI.Data.Auth;

public class UserRepository : IUserRepository
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public UserRepository(ISqlConnectionFactory sqlConnectionFactory)
    {
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public async Task<Users?> GetByCredentialsAsync(
    string companyId,
    string username)
    {
        const string sql = """
        SELECT
            CompanyID AS CompanyId,
            DivisionID AS DivisionId,
            DepartmentID AS DepartmentId,
            Username,
            Password,
            FirstName,
            MiddleName,
            LastName,
            Email,
            Phone,
            BranchCode,
            UserGroupID AS UserGroupId,
            RoleId,
            Active,
            PasswordExpires,
            PasswordExpiresDate,
            RefreshToken,
            RefreshTokenExpiry,
            Id
        FROM dbo.Users
        WHERE CompanyID = @CompanyId
          AND Username = @Username;
        """;

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync();

        return await connection.QuerySingleOrDefaultAsync<Users>(
            sql,
            new
            {
                CompanyId = companyId,
                Username = username
            });
    }

    public async Task<Users?> GetByRefreshTokenAsync(string refreshToken)
    {
        const string sql = """
            SELECT
                CompanyID AS CompanyId,
                DivisionID AS DivisionId,
                DepartmentID AS DepartmentId,
                Username,
                Password,
                FirstName,
                MiddleName,
                LastName,
                Email,
                Phone,
                BranchCode,
                UserGroupID AS UserGroupId,
                RoleId,
                Active,
                PasswordExpires,
                PasswordExpiresDate,
                RefreshToken,
                RefreshTokenExpiry,
                Id
            FROM dbo.Users
            WHERE RefreshToken = @RefreshToken;
            """;

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync();

        return await connection.QuerySingleOrDefaultAsync<Users>(
            sql,
            new
            {
                RefreshToken = refreshToken
            });
    }

    public async Task UpdateRefreshTokenAsync(
        string companyId,
        string divisionId,
        string departmentId,
        string username,
        string refreshToken,
        DateTime refreshTokenExpiry)
    {
        const string sql = """
            UPDATE dbo.Users
            SET
                RefreshToken = @RefreshToken,
                RefreshTokenExpiry = @RefreshTokenExpiry
            WHERE CompanyID = @CompanyId
              AND DivisionID = @DivisionId
              AND DepartmentID = @DepartmentId
              AND Username = @Username;
            """;

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync();

        await connection.ExecuteAsync(
            sql,
            new
            {
                CompanyId = companyId,
                DivisionId = divisionId,
                DepartmentId = departmentId,
                Username = username,
                RefreshToken = refreshToken,
                RefreshTokenExpiry = refreshTokenExpiry
            });
    }

    public async Task ClearRefreshTokenAsync(
        string companyId,
        string divisionId,
        string departmentId,
        string username)
    {
        const string sql = """
            UPDATE dbo.Users
            SET
                RefreshToken = NULL,
                RefreshTokenExpiry = NULL
            WHERE CompanyID = @CompanyId
              AND DivisionID = @DivisionId
              AND DepartmentID = @DepartmentId
              AND Username = @Username;
            """;

        using var connection = _sqlConnectionFactory.Create();

        await connection.OpenAsync();

        await connection.ExecuteAsync(
            sql,
            new
            {
                CompanyId = companyId,
                DivisionId = divisionId,
                DepartmentId = departmentId,
                Username = username
            });
    }
}


