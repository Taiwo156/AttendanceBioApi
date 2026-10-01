using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioApi.Models;
using Dapper;
using AttendanceBioApi.Data.Database;

namespace AttendanceBioAPI.Data.Repo;

public class CompanyRepository : ICompanyRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public CompanyRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<Company>> GetCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT
                CompanyID AS CompanyId,
                CompanyID AS CompanyName
            FROM dbo.CompanyBranch
            WHERE CompanyID IS NOT NULL
              AND LTRIM(RTRIM(CompanyID)) <> ''
            ORDER BY CompanyID;
            """;

        using var connection = _connectionFactory.Create();

        var command = new CommandDefinition(
            sql,
            cancellationToken: cancellationToken);

        var result = await connection.QueryAsync<Company>(command);

        return result.ToList();
    }

    public async Task<IReadOnlyCollection<CompanyBranch>> GetBranchesAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                CompanyID AS CompanyId,
                DivisionID AS DivisionId,
                DepartmentID AS DepartmentId,
                BranchCode AS BranchCode,
                BranchName AS BranchName
            FROM dbo.CompanyBranch
            WHERE CompanyID = @CompanyId
            ORDER BY BranchName;
            """;

        using var connection = _connectionFactory.Create();

        var command = new CommandDefinition(
            sql,
            new { CompanyId = companyId },
            cancellationToken: cancellationToken);

        var result = await connection.QueryAsync<CompanyBranch>(command);

        return result.ToList();
    }
}



