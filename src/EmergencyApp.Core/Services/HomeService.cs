using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Services;

public sealed class HomeService(
    IUserRepository users,
    INotificationRepository notifications,
    IEmergencyRepository emergencies,
    IGeocodingService geocoder) : IHomeService
{
    public async Task<HomeSummary> GetHomeAsync(Guid userId, GeoPoint currentLocation, CancellationToken ct = default)
    {
        if (!currentLocation.IsValid) throw new InvalidInputException("Invalid location.");
        var user = await users.GetAsync(userId, ct) ?? throw new NotFoundException("User not found.");

        var unread = await notifications.CountUnreadAsync(userId, ct);
        var address = await geocoder.ReverseGeocodeAsync(currentLocation, ct);
        var active = await emergencies.GetActiveByUserAsync(userId, ct);

        return new HomeSummary(user.Id, user.FullName, user.AvatarUrl, unread, address, active?.Id);
    }
}

public sealed class NotificationService(INotificationRepository notifications) : INotificationService
{
    public Task<IReadOnlyList<Notification>> ListAsync(Guid userId, CancellationToken ct = default) =>
        notifications.ListAsync(userId, ct);

    public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default) =>
        notifications.MarkAllReadAsync(userId, ct);
}
