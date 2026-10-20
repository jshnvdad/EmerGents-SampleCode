using EmergencyApp.Core.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyApp.Api;

/// <summary>
/// Clients connect to /hubs/emergency and call Join(emergencyId), then listen for "emergencyEvent".
/// (No auth here - add [Authorize] and verify ownership before shipping.)
/// </summary>
public sealed class EmergencyHub : Hub
{
    public Task Join(Guid emergencyId) => Groups.AddToGroupAsync(Context.ConnectionId, emergencyId.ToString());
    public Task Leave(Guid emergencyId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, emergencyId.ToString());
}

public sealed class SignalREventPublisher(IHubContext<EmergencyHub> hub) : IEmergencyEventPublisher
{
    public Task PublishAsync(EmergencyEvent evt, CancellationToken ct = default) =>
        hub.Clients.Group(evt.EmergencyId.ToString()).SendAsync("emergencyEvent", evt, ct);
}

/// <summary>Retries SOS requests that found no responders (e.g. everyone was busy).</summary>
public sealed class DispatchRetryWorker(IEmergencyService emergencies, ILogger<DispatchRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var n = await emergencies.RetryPendingDispatchAsync(stoppingToken);
                if (n > 0) logger.LogInformation("Dispatched {Count} previously pending emergencies.", n);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "Pending-dispatch retry failed."); }
        }
    }
}
