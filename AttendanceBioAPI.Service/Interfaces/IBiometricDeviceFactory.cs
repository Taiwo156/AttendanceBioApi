using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IBiometricDeviceFactory
{
    Task<IBiometricDevice?> CreateAsync(string deviceId,CancellationToken cancellationToken = default);

    IBiometricDevice CreateForTest(BiometricDevice device);
}
