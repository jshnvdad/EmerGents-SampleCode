using System.Globalization;
using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Infrastructure;

/// <summary>Offline stand-in for a real reverse-geocoder.</summary>
public sealed class FakeGeocodingService : IGeocodingService
{
    public Task<string> ReverseGeocodeAsync(GeoPoint location, CancellationToken ct = default)
    {
        if (location.DistanceKmTo(DemoData.UserLocation) < 0.5)
            return Task.FromResult("151-171 Montclair Ave Newark, NJ 07104, USA");

        return Task.FromResult(string.Create(CultureInfo.InvariantCulture,
            $"{location.Latitude:F5}, {location.Longitude:F5}"));
    }
}

/// <summary>Returns a straight polyline. Replace with a real routing engine.</summary>
public sealed class StraightLineRoutingService : IRoutingService
{
    public IReadOnlyList<GeoPoint> GetRoute(GeoPoint from, GeoPoint to)
    {
        const int segments = 10;
        return Enumerable.Range(0, segments + 1).Select(i => from.Lerp(to, i / (double)segments)).ToList();
    }
}

/// <summary>Default publisher that does nothing. The API project overrides it with SignalR.</summary>
public sealed class NullEventPublisher : IEmergencyEventPublisher
{
    public Task PublishAsync(EmergencyEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class StaticInstructionProvider : IInstructionProvider
{
    public EmergencyInstructions Get(EmergencyStatus status) => status switch
    {
        EmergencyStatus.Pending => new(
            "We are looking for the nearest responders. If this is life-threatening, also call your local emergency number.",
            new[] { "Stay where you are and keep your phone on.", "Unlock the door if it is safe to do so." }),

        EmergencyStatus.Dispatched or EmergencyStatus.Arrived => new(
            "Speak calmly to the person until the first aider arrives. First responders are on the way and the Rescue service is alarmed.",
            new[]
            {
                "Stay calm and keep the person comfortable and still.",
                "Do not move them unless they are in immediate danger.",
                "Check breathing and responsiveness; start CPR if trained and needed.",
                "Apply firm pressure to any heavy bleeding with a clean cloth.",
                "Unlock doors and turn on lights so responders can find you.",
                "Keep your phone nearby: responders may call you."
            }),

        _ => new("No active emergency.", Array.Empty<string>())
    };
}

public static class DemoData
{
    public static readonly Guid DemoUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly GeoPoint UserLocation = new(40.7357, -74.1724); // Newark, NJ (as in the mock-up)

    public static IEnumerable<User> Users() => new[]
    {
        new User { Id = DemoUserId, FullName = "Jenifer Pilman", Phone = "+1 555 0100" }
    };

    public static IEnumerable<Responder> Responders() => new[]
    {
        new Responder { Name = "Newark EMS Unit 12", Type = ResponderType.Ambulance,     Phone = "+1 555 0112", Location = new(40.7420, -74.1800) },
        new Responder { Name = "Newark EMS Unit 7",  Type = ResponderType.Ambulance,     Phone = "+1 555 0107", Location = new(40.7600, -74.2000) },
        new Responder { Name = "Emily Dayton",       Type = ResponderType.FirstResponder, Phone = "+1 555 0121", Location = new(40.7500, -74.1500) },
        new Responder { Name = "Marcus Reed",        Type = ResponderType.FirstResponder, Phone = "+1 555 0122", Location = new(40.7100, -74.1600) },
        new Responder { Name = "Sofia Alvarez",      Type = ResponderType.FirstResponder, Phone = "+1 555 0123", Location = new(40.7000, -74.1700) },
        new Responder { Name = "Off-duty Medic",     Type = ResponderType.FirstResponder, Phone = "+1 555 0124", Location = new(40.7360, -74.1720), Status = ResponderStatus.Offline },
    };
}
