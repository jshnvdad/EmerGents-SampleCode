namespace EmergencyApp.Core.Models;

public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0088;

    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;

    /// <summary>Great-circle distance (Haversine) in kilometres.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        static double Rad(double deg) => deg * Math.PI / 180.0;
        var dLat = Rad(other.Latitude - Latitude);
        var dLon = Rad(other.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(Latitude)) * Math.Cos(Rad(other.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Linear interpolation towards <paramref name="target"/> (0 = this, 1 = target).</summary>
    public GeoPoint Lerp(GeoPoint target, double fraction) =>
        new(Latitude + (target.Latitude - Latitude) * fraction,
            Longitude + (target.Longitude - Longitude) * fraction);
}
