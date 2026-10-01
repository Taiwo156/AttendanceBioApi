using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces
{
    public interface ICompanyResolutionService
    {
        Task<string?> ResolveCompanyIdAsync(ZkbioEmployee employee, CancellationToken ct);
        Task WaitUntilReadyAsync(CancellationToken ct);
    }
}

