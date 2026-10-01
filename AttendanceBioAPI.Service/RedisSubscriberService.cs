using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace AttendanceBioAPI.Service;

public class RedisSubscriberService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IAttendanceSignalRPublisher _publisher;
    private readonly ILogger<RedisSubscriberService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisSubscriberService(
        IConnectionMultiplexer redis,
        IAttendanceSignalRPublisher publisher,
        ILogger<RedisSubscriberService> logger)
    {
        _redis = redis;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Redis attendance broadcast subscriber starting");

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.AttendanceLive),
            async (channel, value) =>
            {
                if (value.IsNullOrEmpty)
                    return;

                try
                {
                    var attendanceEvent =
                        JsonSerializer.Deserialize<AttendanceStatusEvent>(
                            value.ToString(),
                            JsonOptions);

                    if (attendanceEvent is not null)
                    {
                        _logger.LogInformation(
                            "Broadcasting attendance StatusChange for Employee {EmployeeId}, Site {SiteId}, Status {Status}",
                            attendanceEvent.EmployeeId,
                            attendanceEvent.SiteId,
                            attendanceEvent.Status);

                        await _publisher.PublishStatusChangeAsync(
                            attendanceEvent,
                            stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to process live attendance message");
                }
            });

        await subscriber.SubscribeAsync(
            RedisChannel.Literal("attendance:alerts"),
            async (channel, value) =>
            {
                if (value.IsNullOrEmpty)
                    return;

                try
                {
                    var alert =
                        JsonSerializer.Deserialize<AttendanceProcessingAlert>(
                            value.ToString(),
                            JsonOptions);

                    if (alert is not null)
                    {
                        await _publisher.PublishProcessingAlertAsync(
                            alert,
                            stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to process attendance alert message");
                }
            });

        _logger.LogInformation(
            "Redis subscriptions active: {LiveChannel}, attendance:alerts",
            RedisChannels.AttendanceLive);

        try
        {
            await Task.Delay(
                Timeout.Infinite,
                stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Redis subscriber service is stopping.");
        }
    }
}

