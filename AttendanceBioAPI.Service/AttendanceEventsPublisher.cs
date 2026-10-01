using System.Text.Json;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.Interfaces;
using StackExchange.Redis;

namespace AttendanceBioAPI.Service;



public class AttendanceEventsPublisher : IAttendanceEventsPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AttendanceEventsPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task PublishAsync(AttendanceStatusEvent evt)
    {
        var subscriber = _redis.GetSubscriber();
        var payload = JsonSerializer.Serialize(evt, JsonOptions);
        await subscriber.PublishAsync(RedisChannel.Literal(RedisChannels.AttendanceLive), payload);
    }
    public async Task PublishAlertAsync(AttendanceProcessingAlert alert)
    {
        var subscriber = _redis.GetSubscriber();
        var payload = JsonSerializer.Serialize(alert, JsonOptions);
        await subscriber.PublishAsync(RedisChannel.Literal("attendance:alerts"), payload);
    }
}

