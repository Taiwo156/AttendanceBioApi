using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AttendanceBioApi.Data.Database;

public interface ISqlConnectionFactory
{
    SqlConnection Create();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");
    }

    public SqlConnection Create() => new SqlConnection(_connectionString);
}
