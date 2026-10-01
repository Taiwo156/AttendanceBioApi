using AttendanceBioApi.Models;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AttendanceBioAPI.Service.Biometric;

public class BiometricDeviceFactory : IBiometricDeviceFactory
{
    private readonly IBiometricDeviceRepository _repository;
    private readonly ILoggerFactory _loggerFactory;

    public BiometricDeviceFactory(
        IBiometricDeviceRepository repository,
        ILoggerFactory loggerFactory)
    {
        _repository = repository;
        _loggerFactory = loggerFactory;
    }

    public async Task<IBiometricDevice?> CreateAsync(string deviceId,CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetByIdAsync(
            deviceId,
            cancellationToken);

        if (device is null)
        {
            return null;
        }

        if (!device.Enabled)
        {
            throw new InvalidOperationException(
                $"Biometric device '{device.DeviceName}' is disabled.");
        }

        return CreateDevice(device);
    }

    private IBiometricDevice CreateDevice(BiometricDevice device)
    {
        return device.Manufacturer.Trim().ToLowerInvariant() switch
        {
            "zkteco" => new ZktecoDevice(
                device,
                _loggerFactory.CreateLogger<ZktecoDevice>()),

            _ => throw new NotSupportedException(
                $"Biometric manufacturer '{device.Manufacturer}' is not supported.")
        };
    }

    public IBiometricDevice CreateForTest(BiometricDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Manufacturer))
        {
            throw new InvalidOperationException(
                "Biometric manufacturer is required.");
        }

        return CreateDevice(device);
    }
}
