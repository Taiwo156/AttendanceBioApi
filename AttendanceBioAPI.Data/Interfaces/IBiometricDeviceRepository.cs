using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Data.Interfaces;

public interface IBiometricDeviceRepository
{
    Task<IReadOnlyCollection<BiometricDevice>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<BiometricDevice?> GetByIdAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<BiometricDevice> CreateAsync(
        BiometricDevice device,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        BiometricDevice device,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
}


