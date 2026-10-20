using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Services;

public sealed class EmergencyService(
    IUserRepository users,
    IResponderRepository responders,
    IEmergencyRepository emergencies,
    INotificationRepository notifications,
    IDispatchService dispatch,
    IGeocodingService geocoder,
    IRoutingService router,
    IInstructionProvider instructions,
    IEmergencyEventPublisher events,
    DispatchOptions options,
    TimeProvider clock) : IEmergencyService
{
    // Serialises state changes so two SOS calls can't grab the same responder.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // ---------------- User-side ----------------

    public async Task<EmergencyStatusDto> TriggerSosAsync(Guid userId, GeoPoint location, CancellationToken ct = default)
    {
        if (!location.IsValid) throw new InvalidInputException("Invalid location.");
        if (await users.GetAsync(userId, ct) is null) throw new NotFoundException("User not found.");

        var pending = new List<EmergencyEvent>();
        EmergencyRequest request;

        await _gate.WaitAsync(ct);
        try
        {
            var existing = await emergencies.GetActiveByUserAsync(userId, ct);
            if (existing is not null)
            {
                request = existing; // idempotent: double-tapping SOS never creates a second request
            }
            else
            {
                var now = clock.GetUtcNow();
                request = new EmergencyRequest
                {
                    UserId = userId,
                    Location = location,
                    Address = await geocoder.ReverseGeocodeAsync(location, ct),
                    Status = EmergencyStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await emergencies.AddAsync(request, ct);
                pending.Add(new(request.Id, EmergencyEventType.Created));
                await AssignRespondersAsync(request, pending, ct);
            }
        }
        finally { _gate.Release(); }

        await PublishAsync(pending, ct);
        return await BuildStatusAsync(request, ct);
    }

    public async Task<EmergencyStatusDto> CancelAsync(Guid emergencyId, Guid userId, string? reason, CancellationToken ct = default)
    {
        var pending = new List<EmergencyEvent>();
        EmergencyRequest request;

        await _gate.WaitAsync(ct);
        try
        {
            request = await GetOwnedAsync(emergencyId, userId, ct);

            if (request.Status != EmergencyStatus.Cancelled) // cancelling twice is a no-op
            {
                if (!request.IsActive)
                    throw new ConflictException("This emergency is already resolved and can't be cancelled.");

                var now = clock.GetUtcNow();
                request.Status = EmergencyStatus.Cancelled;
                request.CancelledAt = now;
                request.CancelReason = reason;
                request.UpdatedAt = now;
                await ReleaseRespondersAsync(request, ct);
                await emergencies.UpdateAsync(request, ct);
                await AddNotificationAsync(userId, "Request cancelled", "Your emergency request was cancelled.", ct);
                pending.Add(new(request.Id, EmergencyEventType.Cancelled));
            }
        }
        finally { _gate.Release(); }

        await PublishAsync(pending, ct);
        return await BuildStatusAsync(request, ct);
    }

    public async Task<EmergencyStatusDto> GetStatusAsync(Guid emergencyId, Guid userId, CancellationToken ct = default) =>
        await BuildStatusAsync(await GetOwnedAsync(emergencyId, userId, ct), ct);

    public async Task<EmergencyMapDto> GetMapAsync(Guid emergencyId, Guid userId, CancellationToken ct = default)
    {
        var request = await GetOwnedAsync(emergencyId, userId, ct);
        var onMap = new List<ResponderOnMap>();

        foreach (var a in request.Assignments)
        {
            var responder = await responders.GetAsync(a.ResponderId, ct);
            if (responder is null) continue;
            onMap.Add(new ResponderOnMap(
                ToSummary(responder, a, request),
                responder.Location,
                router.GetRoute(responder.Location, request.Location)));
        }

        var nearest = onMap.OrderBy(r => r.Responder.DistanceKm).FirstOrDefault()?.Responder.ResponderId;
        return new EmergencyMapDto(request.Id, request.Location, request.Address, nearest, onMap);
    }

    public async Task<EmergencyInstructions> GetInstructionsAsync(Guid emergencyId, Guid userId, CancellationToken ct = default) =>
        instructions.Get((await GetOwnedAsync(emergencyId, userId, ct)).Status);

    // ---------------- Responder-side ----------------

    public async Task UpdateResponderLocationAsync(Guid responderId, GeoPoint location, CancellationToken ct = default)
    {
        if (!location.IsValid) throw new InvalidInputException("Invalid location.");
        var pending = new List<EmergencyEvent>();

        await _gate.WaitAsync(ct);
        try
        {
            var responder = await responders.GetAsync(responderId, ct) ?? throw new NotFoundException("Responder not found.");
            responder.Location = location;
            await responders.UpdateAsync(responder, ct);

            foreach (var request in await emergencies.GetActiveByResponderAsync(responderId, ct))
            {
                var a = request.Assignments.First(x => x.ResponderId == responderId);
                var distance = location.DistanceKmTo(request.Location);
                var arrived = distance <= options.ArrivalThresholdKm;
                a.EtaMinutes = arrived ? 0 : EtaCalculator.Minutes(distance, a.Type, options);

                pending.Add(new(request.Id, EmergencyEventType.ResponderLocationUpdated,
                    new { ResponderId = responderId, Location = location, a.EtaMinutes, DistanceKm = distance }));

                if (arrived && a.ArrivedAt is null)
                {
                    a.ArrivedAt = clock.GetUtcNow();
                    responder.Status = ResponderStatus.Arrived;
                    await responders.UpdateAsync(responder, ct);

                    if (request.Status == EmergencyStatus.Dispatched)
                    {
                        request.Status = EmergencyStatus.Arrived;
                        await AddNotificationAsync(request.UserId, "Help has arrived",
                            $"{responder.Name} has arrived at your location.", ct);
                    }
                    pending.Add(new(request.Id, EmergencyEventType.ResponderArrived, new { ResponderId = responderId }));
                }

                request.UpdatedAt = clock.GetUtcNow();
                await emergencies.UpdateAsync(request, ct);
            }
        }
        finally { _gate.Release(); }

        await PublishAsync(pending, ct);
    }

    public async Task ResolveAsync(Guid emergencyId, CancellationToken ct = default)
    {
        var pending = new List<EmergencyEvent>();

        await _gate.WaitAsync(ct);
        try
        {
            var request = await emergencies.GetAsync(emergencyId, ct) ?? throw new NotFoundException("Emergency not found.");
            if (!request.IsActive) return;

            request.Status = EmergencyStatus.Resolved;
            request.UpdatedAt = clock.GetUtcNow();
            await ReleaseRespondersAsync(request, ct);
            await emergencies.UpdateAsync(request, ct);
            await AddNotificationAsync(request.UserId, "Emergency resolved", "Your emergency has been marked as resolved.", ct);
            pending.Add(new(request.Id, EmergencyEventType.Resolved));
        }
        finally { _gate.Release(); }

        await PublishAsync(pending, ct);
    }

    public async Task<int> RetryPendingDispatchAsync(CancellationToken ct = default)
    {
        var pending = new List<EmergencyEvent>();
        var dispatched = 0;

        await _gate.WaitAsync(ct);
        try
        {
            foreach (var request in await emergencies.GetPendingAsync(ct))
            {
                await AssignRespondersAsync(request, pending, ct);
                if (request.Status == EmergencyStatus.Dispatched) dispatched++;
            }
        }
        finally { _gate.Release(); }

        await PublishAsync(pending, ct);
        return dispatched;
    }

    // ---------------- Helpers ----------------

    /// <summary>Must be called while holding <see cref="_gate"/>.</summary>
    private async Task AssignRespondersAsync(EmergencyRequest request, List<EmergencyEvent> pending, CancellationToken ct)
    {
        var haveAmbulances = request.Assignments.Count(a => a.Type == ResponderType.Ambulance);
        var haveFirst = request.Assignments.Count(a => a.Type == ResponderType.FirstResponder);

        var candidates = await dispatch.FindAsync(
            request.Location,
            Math.Max(0, options.MaxAmbulances - haveAmbulances),
            Math.Max(0, options.MaxFirstResponders - haveFirst),
            ct);

        if (candidates.Count == 0) return; // stays Pending; the retry worker will try again

        var now = clock.GetUtcNow();
        foreach (var c in candidates)
        {
            c.Responder.Status = ResponderStatus.EnRoute;
            await responders.UpdateAsync(c.Responder, ct);

            request.Assignments.Add(new ResponderAssignment
            {
                ResponderId = c.Responder.Id,
                Type = c.Responder.Type,
                AssignedAt = now,
                EtaMinutes = c.EtaMinutes
            });
            pending.Add(new(request.Id, EmergencyEventType.ResponderAssigned,
                new { ResponderId = c.Responder.Id, c.Responder.Name, c.EtaMinutes }));
        }

        if (request.Status == EmergencyStatus.Pending)
        {
            request.Status = EmergencyStatus.Dispatched;
            await AddNotificationAsync(request.UserId, "Help is on the way",
                "Ambulance and responders are on a way.", ct);
        }
        request.UpdatedAt = now;
        await emergencies.UpdateAsync(request, ct);
    }

    private async Task ReleaseRespondersAsync(EmergencyRequest request, CancellationToken ct)
    {
        foreach (var a in request.Assignments)
        {
            var responder = await responders.GetAsync(a.ResponderId, ct);
            if (responder is null || responder.Status == ResponderStatus.Offline) continue;
            responder.Status = ResponderStatus.Available;
            await responders.UpdateAsync(responder, ct);
        }
    }

    private async Task<EmergencyRequest> GetOwnedAsync(Guid emergencyId, Guid userId, CancellationToken ct)
    {
        var request = await emergencies.GetAsync(emergencyId, ct) ?? throw new NotFoundException("Emergency not found.");
        if (request.UserId != userId) throw new ForbiddenException("This emergency belongs to another user.");
        return request;
    }

    private Task AddNotificationAsync(Guid userId, string title, string body, CancellationToken ct) =>
        notifications.AddAsync(new Notification
        {
            UserId = userId, Title = title, Body = body, CreatedAt = clock.GetUtcNow()
        }, ct);

    private async Task PublishAsync(IEnumerable<EmergencyEvent> pending, CancellationToken ct)
    {
        foreach (var e in pending) await events.PublishAsync(e, ct);
    }

    private static ResponderSummary ToSummary(Responder r, ResponderAssignment a, EmergencyRequest request) =>
        new(r.Id, r.Name, r.Type, r.AvatarUrl, r.Phone, r.Status, a.EtaMinutes,
            Math.Round(r.Location.DistanceKmTo(request.Location), 2));

    private async Task<EmergencyStatusDto> BuildStatusAsync(EmergencyRequest request, CancellationToken ct)
    {
        var list = new List<ResponderSummary>();
        foreach (var a in request.Assignments.OrderBy(a => a.EtaMinutes))
        {
            var r = await responders.GetAsync(a.ResponderId, ct);
            if (r is not null) list.Add(ToSummary(r, a, request));
        }

        var (headline, message) = request.Status switch
        {
            EmergencyStatus.Pending    => ("Looking for nearby responders", "Hold on, we're contacting the closest help."),
            EmergencyStatus.Dispatched => ("Ambulance and responders are on a way", instructions.Get(request.Status).Summary),
            EmergencyStatus.Arrived    => ("Responders have arrived", "Help is at your location."),
            EmergencyStatus.Resolved   => ("Emergency resolved", "This emergency has been closed."),
            _                          => ("Request cancelled", "Your request was cancelled.")
        };

        return new EmergencyStatusDto(request.Id, request.Status, headline, message,
            request.Address, request.CreatedAt, list);
    }
}
