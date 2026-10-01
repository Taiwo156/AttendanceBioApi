using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces
{
    public interface IZkbioService
    {
        Task<bool> LoginAsync(CancellationToken ct);
        string? GetAuthToken();
        Task<List<ZkbioEmployee>> GetEmployeesAsync(CancellationToken ct);
        Task<List<ZkbioDepartment>> GetDepartmentsAsync(CancellationToken ct);
        Task<List<ZkbioTransaction>> GetTransactionsAsync(DateTime startTime, CancellationToken ct);
        Task<List<ZkbioOvertime>> GetOvertimesAsync(DateTime date, CancellationToken ct);
    }
}
