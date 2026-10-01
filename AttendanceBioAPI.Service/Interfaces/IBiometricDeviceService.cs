namespace AttendanceBioAPI.Service.Interfaces;

public interface IBiometricDeviceService
{
    Task<bool> TestConnectionAsync(
        CancellationToken cancellationToken = default);
}
