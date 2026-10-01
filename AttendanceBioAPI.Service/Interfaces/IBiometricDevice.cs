using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IBiometricDevice
{
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task<IReadOnlyCollection<AttendancePunch>> GetAttendancePunchesAsync(CancellationToken cancellationToken = default);
}
