using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioApi.Models;
using Dapper;
using AttendanceBioApi.Data.Database;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Data.Repo
{
    public class BiometricDeviceRepository : IBiometricDeviceRepository
    {
        private readonly ISqlConnectionFactory _sqlConnectionFactory;
        private readonly ILogger<BiometricDeviceRepository> _logger;

        public BiometricDeviceRepository(
            ISqlConnectionFactory sqlConnectionFactory,
            ILogger<BiometricDeviceRepository> logger)
        {
            _sqlConnectionFactory = sqlConnectionFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<BiometricDevice>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            using var connection = _sqlConnectionFactory.Create();

            await connection.OpenAsync(cancellationToken);

            var devices = await connection.QueryAsync<BiometricDevice>(@"
            SELECT
                d.DeviceID AS DeviceId,

                d.CompanyID AS CompanyId,
                d.DivisionID AS DivisionId,
                d.DepartmentID AS DepartmentId,
                d.BranchCode AS BranchCode,

                ISNULL(b.BranchName, '') AS BranchName,

                d.DeviceName AS DeviceName,
                d.Manufacturer AS Manufacturer,
                ISNULL(d.Model, '') AS Model,
                ISNULL(d.SerialNumber, '') AS SerialNumber,

                d.DeviceNumber AS DeviceNumber,
                ISNULL(d.DeviceType, '') AS DeviceType,

                d.IPAddress AS IpAddress,
                d.Port AS Port,
                d.CommKey AS CommKey,

                d.Enabled AS Enabled,
                d.AutomaticSync AS AutomaticSync,

                d.CreatedAt AS CreatedAt,
                d.UpdatedAt AS UpdatedAt

            FROM dbo.BiometricDevice d

            LEFT JOIN dbo.CompanyBranch b
                ON b.CompanyID = d.CompanyID
                AND b.DivisionID = d.DivisionID
                AND b.DepartmentID = d.DepartmentID
                AND b.BranchCode = d.BranchCode

            ORDER BY d.DeviceName
        ");

            return devices.ToList();
        }

        public async Task<BiometricDevice?> GetByIdAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            using var connection = _sqlConnectionFactory.Create();

            await connection.OpenAsync(cancellationToken);

            return await connection.QueryFirstOrDefaultAsync<BiometricDevice>(@"
            SELECT
                d.DeviceID AS DeviceId,

                d.CompanyID AS CompanyId,
                d.DivisionID AS DivisionId,
                d.DepartmentID AS DepartmentId,
                d.BranchCode AS BranchCode,

                ISNULL(b.BranchName, '') AS BranchName,

                d.DeviceName AS DeviceName,
                d.Manufacturer AS Manufacturer,
                ISNULL(d.Model, '') AS Model,
                ISNULL(d.SerialNumber, '') AS SerialNumber,

                d.DeviceNumber AS DeviceNumber,
                ISNULL(d.DeviceType, '') AS DeviceType,

                d.IPAddress AS IpAddress,
                d.Port AS Port,
                d.CommKey AS CommKey,

                d.Enabled AS Enabled,
                d.AutomaticSync AS AutomaticSync,

                d.CreatedAt AS CreatedAt,
                d.UpdatedAt AS UpdatedAt

            FROM dbo.BiometricDevice d

            LEFT JOIN dbo.CompanyBranch b
                ON b.CompanyID = d.CompanyID
                AND b.DivisionID = d.DivisionID
                AND b.DepartmentID = d.DepartmentID
                AND b.BranchCode = d.BranchCode

            WHERE d.DeviceID = @DeviceId
        ",
            new { DeviceId = deviceId });
        }

        public async Task<BiometricDevice> CreateAsync(
            BiometricDevice device,
            CancellationToken cancellationToken = default)
        {
            using var connection = _sqlConnectionFactory.Create();

            await connection.OpenAsync(cancellationToken);

            await connection.ExecuteAsync(@"
            INSERT INTO dbo.BiometricDevice
            (
                DeviceID,

                CompanyID,
                DivisionID,
                DepartmentID,
                BranchCode,

                DeviceName,
                Manufacturer,
                Model,
                SerialNumber,

                DeviceNumber,
                DeviceType,

                IPAddress,
                Port,
                CommKey,

                Enabled,
                AutomaticSync,

                CreatedAt,
                UpdatedAt
            )
            VALUES
            (
                @DeviceId,

                @CompanyId,
                @DivisionId,
                @DepartmentId,
                @BranchCode,

                @DeviceName,
                @Manufacturer,
                @Model,
                @SerialNumber,

                @DeviceNumber,
                @DeviceType,

                @IpAddress,
                @Port,
                @CommKey,

                @Enabled,
                @AutomaticSync,

                GETDATE(),
                NULL
            )
        ", device);

            return device;
        }

        public async Task<bool> UpdateAsync(
            BiometricDevice device,
            CancellationToken cancellationToken = default)
        {
            using var connection = _sqlConnectionFactory.Create();

            await connection.OpenAsync(cancellationToken);

            var affected = await connection.QuerySingleAsync<int>(@"
            UPDATE dbo.BiometricDevice
            SET
                CompanyID = @CompanyId,
                DivisionID = @DivisionId,
                DepartmentID = @DepartmentId,
                BranchCode = @BranchCode,

                DeviceName = @DeviceName,
                Manufacturer = @Manufacturer,
                Model = @Model,
                SerialNumber = @SerialNumber,

                DeviceNumber = @DeviceNumber,
                DeviceType = @DeviceType,

                IPAddress = @IpAddress,
                Port = @Port,
                CommKey = @CommKey,

                Enabled = @Enabled,
                AutomaticSync = @AutomaticSync,

                UpdatedAt = GETDATE()

            WHERE DeviceID = @DeviceId;
            SELECT @@ROWCOUNT;
        ", device);

            _logger.LogInformation(
                "Biometric device update affected {AffectedRows} row(s). DeviceId={DeviceId}",
                affected,
                device.DeviceId);

            return affected > 0;
        }

        public async Task<bool> DeleteAsync(
            string deviceId,
            CancellationToken cancellationToken = default)
        {
            using var connection = _sqlConnectionFactory.Create();

            await connection.OpenAsync(cancellationToken);

            var affected = await connection.ExecuteAsync(@"
            DELETE FROM dbo.BiometricDevice
            WHERE DeviceID = @DeviceId
        ",
            new { DeviceId = deviceId });

            return affected > 0;
        }
    }
}



