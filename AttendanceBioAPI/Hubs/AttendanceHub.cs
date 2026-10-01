using System.Collections.Concurrent;
using AttendanceBioApi.Models;
using Microsoft.AspNetCore.SignalR;

namespace AttendanceBioAPI.Hubs;

public class AttendanceHub : Hub
{
    private static readonly ConcurrentDictionary<
        string,
        CancellationTokenSource> AuthTimeouts = new();

    private static readonly ConcurrentDictionary<
        string,
        bool> Joined = new();

    private readonly ILogger<AttendanceHub> _logger;

    public AttendanceHub(ILogger<AttendanceHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected: {ConnectionId}",
            Context.ConnectionId);

        var cts = new CancellationTokenSource();

        AuthTimeouts[Context.ConnectionId] = cts;

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (AuthTimeouts.TryRemove(
                Context.ConnectionId,
                out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        Joined.TryRemove(
            Context.ConnectionId,
            out _);

        _logger.LogInformation(
            "Client disconnected: {ConnectionId}",
            Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Joins the SignalR group for a specific attendance site/branch.
    ///
    /// SiteId comes from the application's company/branch configuration.
    /// There is intentionally no hardcoded site list here.
    /// </summary>
    public async Task JoinSite(string siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
        {
            await Clients.Caller.SendAsync(
                "join_error",
                new
                {
                    message = "Site ID is required."
                });

            return;
        }

        siteId = siteId.Trim();

        _logger.LogInformation(
            "Client {ConnectionId} attempting to join site:{SiteId}",
            Context.ConnectionId,
            siteId);

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            siteId);

        Joined[Context.ConnectionId] = true;

        await Clients.Caller.SendAsync(
            "joined",
            new
            {
                siteId
            });

        _logger.LogInformation(
            "Client {ConnectionId} joined site:{SiteId}",
            Context.ConnectionId,
            siteId);
    }
}
