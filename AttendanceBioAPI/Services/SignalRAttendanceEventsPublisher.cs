using AttendanceBioApi.Models;
using AttendanceBioAPI.Hubs;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AttendanceBioAPI.Services;

public class SignalRAttendanceEventsPublisher : IAttendanceSignalRPublisher
{
    private readonly IHubContext<AttendanceHub> _hubContext;

    public SignalRAttendanceEventsPublisher(
        IHubContext<AttendanceHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PublishStatusChangeAsync(
        AttendanceStatusEvent attendanceEvent,
        CancellationToken cancellationToken = default)
    {
        await _hubContext
            .Clients
            .Group(attendanceEvent.SiteId)
            .SendAsync(
                "StatusChange",
                attendanceEvent,
                cancellationToken);
    }

    public async Task PublishProcessingAlertAsync(
        AttendanceProcessingAlert alert,
        CancellationToken cancellationToken = default)
    {
        await _hubContext
            .Clients
            .All
            .SendAsync(
                "ProcessingAlert",
                alert,
                cancellationToken);
    }
}

