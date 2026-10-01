using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IEmployeeResolutionService
{
    Task<PayrollEmployeeBiometric?> ResolveByBiometricIdAsync(string biometricId, CancellationToken cancellationToken = default);
}
