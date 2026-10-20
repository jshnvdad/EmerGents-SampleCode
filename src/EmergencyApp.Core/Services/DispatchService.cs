using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Services;

public static class EtaCalculator
{
    public static int Minutes(double distanceKm, ResponderType type, DispatchOptions options)
    {
        var speed = type == ResponderType.Ambulance ? options.AmbulanceSpeedKmh : options.FirstResponderSpeedKmh;
        return Math.Max(1, (int)Math.Ceiling(distanceKm / speed * 60.0));
    }
}

/// <summary>Picks the nearest available ambulance(s) and first responder(s) within the search radius.</summary>
public sealed class DispatchService(IResponderRepository responders, DispatchOptions options) : IDispatchService
{
    public async Task<IReadOnlyList<DispatchCandidate>> FindAsync(
        GeoPoint location, int ambulances, int firstResponders, CancellationToken ct = default)
    {
        var all = await responders.GetAllAsync(ct);

        var nearby = all
            .Where(r => r.Status == ResponderStatus.Available)
            .Select(r =>
            {
                var d = r.Location.DistanceKmTo(location);
                return new DispatchCandidate(r, d, EtaCalculator.Minutes(d, r.Type, options));
            })
            .Where(c => c.DistanceKm <= options.MaxSearchRadiusKm)
            .ToList();

        IEnumerable<DispatchCandidate> Pick(ResponderType type, int count) =>
            nearby.Where(c => c.Responder.Type == type).OrderBy(c => c.DistanceKm).Take(count);

        return Pick(ResponderType.Ambulance, ambulances)
            .Concat(Pick(ResponderType.FirstResponder, firstResponders))
            .OrderBy(c => c.EtaMinutes).ThenBy(c => c.DistanceKm)
            .ToList();
    }
}
