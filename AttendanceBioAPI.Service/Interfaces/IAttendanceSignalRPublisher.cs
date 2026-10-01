using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IAttendanceSignalRPublisher
{
    Task PublishStatusChangeAsync(
        AttendanceStatusEvent attendanceEvent,
        CancellationToken cancellationToken = default);

    Task PublishProcessingAlertAsync(
        AttendanceProcessingAlert alert,
        CancellationToken cancellationToken = default);
}
