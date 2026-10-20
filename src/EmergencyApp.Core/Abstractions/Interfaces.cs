using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Abstractions;

// ---- Repositories (in-memory implementations provided; swap for EF Core etc.) ----
public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken ct = default);
}

public interface IResponderRepository
{
    Task<Responder?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Responder>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Responder responder, CancellationToken ct = default);
    Task UpdateAsync(Responder responder, CancellationToken ct = default);
}

public interface IEmergencyRepository
{
    Task AddAsync(EmergencyRequest request, CancellationToken ct = default);
    Task<EmergencyRequest?> GetAsync(Guid id, CancellationToken ct = default);
    Task<EmergencyRequest?> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<EmergencyRequest>> GetPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EmergencyRequest>> GetActiveByResponderAsync(Guid responderId, CancellationToken ct = default);
    Task UpdateAsync(EmergencyRequest request, CancellationToken ct = default);
}

public interface IMessageRepository
{
    Task AddAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> ListAsync(Guid emergencyId, Guid responderId, CancellationToken ct = default);
}

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}

// ---- External-facing ports ----
/// <summary>Turns coordinates into "Your current address". Replace with Google/Nominatim/etc.</summary>
public interface IGeocodingService
{
    Task<string> ReverseGeocodeAsync(GeoPoint location, CancellationToken ct = default);
}

/// <summary>Route polyline between a responder and the user. Replace with Google Directions/OSRM/etc.</summary>
public interface IRoutingService
{
    IReadOnlyList<GeoPoint> GetRoute(GeoPoint from, GeoPoint to);
}

/// <summary>Pushes real-time updates to clients (SignalR, FCM, ...).</summary>
public interface IEmergencyEventPublisher
{
    Task PublishAsync(EmergencyEvent evt, CancellationToken ct = default);
}

public interface IInstructionProvider
{
    EmergencyInstructions Get(EmergencyStatus status);
}

// ---- Application services ----
public interface IDispatchService
{
    Task<IReadOnlyList<DispatchCandidate>> FindAsync(
        GeoPoint location, int ambulances, int firstResponders, CancellationToken ct = default);
}

public interface IHomeService
{
    Task<HomeSummary> GetHomeAsync(Guid userId, GeoPoint currentLocation, CancellationToken ct = default);
}

public interface IEmergencyService
{
    /// <summary>The big SOS button. Idempotent: returns the active request if one already exists.</summary>
    Task<EmergencyStatusDto> TriggerSosAsync(Guid userId, GeoPoint location, CancellationToken ct = default);
    /// <summary>"Cancel request".</summary>
    Task<EmergencyStatusDto> CancelAsync(Guid emergencyId, Guid userId, string? reason, CancellationToken ct = default);
    Task<EmergencyStatusDto> GetStatusAsync(Guid emergencyId, Guid userId, CancellationToken ct = default);
    /// <summary>"See responders on map".</summary>
    Task<EmergencyMapDto> GetMapAsync(Guid emergencyId, Guid userId, CancellationToken ct = default);
    /// <summary>"Open full instruction".</summary>
    Task<EmergencyInstructions> GetInstructionsAsync(Guid emergencyId, Guid userId, CancellationToken ct = default);

    // Responder-side operations
    Task UpdateResponderLocationAsync(Guid responderId, GeoPoint location, CancellationToken ct = default);
    Task ResolveAsync(Guid emergencyId, CancellationToken ct = default);
    /// <summary>Called by a background worker to retry requests that had no responders.</summary>
    Task<int> RetryPendingDispatchAsync(CancellationToken ct = default);
}

public interface IMessagingService
{
    /// <summary>"Call" button: validates the responder is assigned and returns a tel: URI.</summary>
    Task<CallInfo> GetCallInfoAsync(Guid emergencyId, Guid userId, Guid responderId, CancellationToken ct = default);
    /// <summary>"Message" button.</summary>
    Task<ChatMessage> SendAsync(Guid emergencyId, Guid userId, Guid responderId, string text, CancellationToken ct = default);
    Task<ChatMessage> ReplyAsResponderAsync(Guid emergencyId, Guid responderId, string text, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> ListAsync(Guid emergencyId, Guid userId, Guid responderId, CancellationToken ct = default);
}

public interface INotificationService
{
    Task<IReadOnlyList<Notification>> ListAsync(Guid userId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}
