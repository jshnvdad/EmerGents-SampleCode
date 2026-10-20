using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Abstractions;

// Home screen: "Welcome back, X", notification badge, SOS button, "Your current address"
public sealed record HomeSummary(
    Guid UserId, string UserName, string? AvatarUrl,
    int UnreadNotifications, string CurrentAddress, Guid? ActiveEmergencyId);

public sealed record ResponderSummary(
    Guid ResponderId, string Name, ResponderType Type, string? AvatarUrl, string Phone,
    ResponderStatus Status, int EtaMinutes, double DistanceKm);

// "Ambulance and responders are on a way" screen
public sealed record EmergencyStatusDto(
    Guid EmergencyId, EmergencyStatus Status, string Headline, string Message,
    string Address, DateTimeOffset CreatedAt, IReadOnlyList<ResponderSummary> Responders);

// Map screen: "You are here", route polyline, "1st Responder (5 min away)"
public sealed record ResponderOnMap(ResponderSummary Responder, GeoPoint Location, IReadOnlyList<GeoPoint> Route);

public sealed record EmergencyMapDto(
    Guid EmergencyId, GeoPoint UserLocation, string Address,
    Guid? NearestResponderId, IReadOnlyList<ResponderOnMap> Responders);

// "Open full instruction"
public sealed record EmergencyInstructions(string Summary, IReadOnlyList<string> Steps);

// "Call" button
public sealed record CallInfo(string PhoneNumber, string TelUri);

public sealed record EmergencyEvent(Guid EmergencyId, EmergencyEventType Type, object? Data = null);

public sealed record DispatchCandidate(Responder Responder, double DistanceKm, int EtaMinutes);

public sealed class DispatchOptions
{
    public int MaxAmbulances { get; init; } = 1;
    public int MaxFirstResponders { get; init; } = 2;
    public double MaxSearchRadiusKm { get; init; } = 15;
    public double AmbulanceSpeedKmh { get; init; } = 50;
    public double FirstResponderSpeedKmh { get; init; } = 30;
    /// <summary>Responder counts as "arrived" within this distance of the user.</summary>
    public double ArrivalThresholdKm { get; init; } = 0.05;
}
