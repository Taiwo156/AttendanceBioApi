using AttendanceBioAPI.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service.Biometric;

public class BiometricDeviceService : IBiometricDeviceService
{
    private readonly IBiometricDevice _biometricDevice;
    private readonly ILogger<BiometricDeviceService> _logger;

    public BiometricDeviceService(
        IBiometricDevice biometricDevice,
        ILogger<BiometricDeviceService> logger)
    {
        _biometricDevice = biometricDevice;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _biometricDevice.ConnectAsync(
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error testing biometric device connection.");

            return false;
        }
        finally
        {
            await _biometricDevice.DisconnectAsync();
        }
    }
}
