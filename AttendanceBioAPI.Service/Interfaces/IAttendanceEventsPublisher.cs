using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IAttendanceEventsPublisher
{
    Task PublishAsync(AttendanceStatusEvent evt);

    Task PublishAlertAsync(AttendanceProcessingAlert alert);
}
