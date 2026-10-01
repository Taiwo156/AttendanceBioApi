using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IDeviceSyncService
{
    Task InitializeAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceSyncResult> SyncDeviceAsync(
        string deviceId,
        bool manual,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DeviceSyncResult>> SyncAutomaticDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DeviceSyncStatus>> GetStatusesAsync(
        CancellationToken cancellationToken = default);
}
