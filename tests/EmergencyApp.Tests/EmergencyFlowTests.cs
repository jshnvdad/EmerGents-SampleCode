using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Infrastructure;
using EmergencyApp.Core.Models;
using EmergencyApp.Core.Services;
using Xunit;

namespace EmergencyApp.Tests;

internal sealed class Harness
{
    public readonly InMemoryUserRepository Users = new();
    public readonly InMemoryResponderRepository Responders = new();
    public readonly InMemoryEmergencyRepository Emergencies = new();
    public readonly InMemoryMessageRepository Messages = new();
    public readonly InMemoryNotificationRepository Notifications = new();
    public readonly IEmergencyService Emergency;
    public readonly IMessagingService Messaging;
    public readonly IHomeService Home;

    public Guid UserId => DemoData.DemoUserId;
    public GeoPoint UserLocation => DemoData.UserLocation;

    public Harness(bool seedResponders = true)
    {
        Users.Seed(DemoData.Users());
        if (seedResponders) Responders.Seed(DemoData.Responders());

        var options = new DispatchOptions();
        var events = new NullEventPublisher();
        var clock = TimeProvider.System;
        var geocoder = new FakeGeocodingService();

        Emergency = new EmergencyService(Users, Responders, Emergencies, Notifications,
            new DispatchService(Responders, options), geocoder, new StraightLineRoutingService(),
            new StaticInstructionProvider(), events, options, clock);
        Messaging = new MessagingService(Emergencies, Responders, Messages, Notifications, events, clock);
        Home = new HomeService(Users, Notifications, Emergencies, geocoder);
    }
}

public class EmergencyFlowTests
{
    [Fact]
    public async Task Sos_dispatches_nearest_ambulance_and_two_first_responders()
    {
        var h = new Harness();

        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);

        Assert.Equal(EmergencyStatus.Dispatched, status.Status);
        Assert.Equal("Ambulance and responders are on a way", status.Headline);
        Assert.Equal(1, status.Responders.Count(r => r.Type == ResponderType.Ambulance));
        Assert.Equal(2, status.Responders.Count(r => r.Type == ResponderType.FirstResponder));
        Assert.Contains(status.Responders, r => r.Name == "Newark EMS Unit 12");   // nearest ambulance
        Assert.DoesNotContain(status.Responders, r => r.Name == "Off-duty Medic"); // offline
        Assert.All(status.Responders, r => Assert.Equal(ResponderStatus.EnRoute, r.Status));
    }

    [Fact]
    public async Task Pressing_sos_twice_returns_the_same_request()
    {
        var h = new Harness();

        var first = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);
        var second = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);

        Assert.Equal(first.EmergencyId, second.EmergencyId);
        Assert.Equal(first.Responders.Count, second.Responders.Count);
    }

    [Fact]
    public async Task Sos_with_no_responders_stays_pending_then_dispatches_on_retry()
    {
        var h = new Harness(seedResponders: false);

        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);
        Assert.Equal(EmergencyStatus.Pending, status.Status);

        await h.Responders.AddAsync(new Responder
        {
            Name = "Late Medic", Type = ResponderType.FirstResponder, Phone = "+1 555 0199",
            Location = new GeoPoint(40.7400, -74.1700)
        });

        var dispatched = await h.Emergency.RetryPendingDispatchAsync();
        var after = await h.Emergency.GetStatusAsync(status.EmergencyId, h.UserId);

        Assert.Equal(1, dispatched);
        Assert.Equal(EmergencyStatus.Dispatched, after.Status);
    }

    [Fact]
    public async Task Cancel_releases_responders_and_is_idempotent()
    {
        var h = new Harness();
        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);

        var cancelled = await h.Emergency.CancelAsync(status.EmergencyId, h.UserId, "False alarm");
        var again = await h.Emergency.CancelAsync(status.EmergencyId, h.UserId, null);

        Assert.Equal(EmergencyStatus.Cancelled, cancelled.Status);
        Assert.Equal(EmergencyStatus.Cancelled, again.Status);
        var all = await h.Responders.GetAllAsync();
        Assert.DoesNotContain(all, r => r.Status == ResponderStatus.EnRoute);
    }

    [Fact]
    public async Task Another_user_cannot_cancel_or_view_a_request()
    {
        var h = new Harness();
        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);
        var stranger = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Emergency.CancelAsync(status.EmergencyId, stranger, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Emergency.GetMapAsync(status.EmergencyId, stranger));
    }

    [Fact]
    public async Task Map_contains_route_and_nearest_responder()
    {
        var h = new Harness();
        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);

        var map = await h.Emergency.GetMapAsync(status.EmergencyId, h.UserId);

        Assert.Equal(h.UserLocation, map.UserLocation);
        Assert.Equal(3, map.Responders.Count);
        Assert.All(map.Responders, r => Assert.True(r.Route.Count >= 2));
        Assert.NotNull(map.NearestResponderId);
    }

    [Fact]
    public async Task Responder_location_updates_lower_eta_and_mark_arrival()
    {
        var h = new Harness();
        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);
        var emily = status.Responders.First(r => r.Name == "Emily Dayton");

        await h.Emergency.UpdateResponderLocationAsync(emily.ResponderId, new GeoPoint(40.7380, -74.1700));
        var closer = (await h.Emergency.GetStatusAsync(status.EmergencyId, h.UserId))
            .Responders.First(r => r.ResponderId == emily.ResponderId);
        Assert.True(closer.EtaMinutes < emily.EtaMinutes);

        await h.Emergency.UpdateResponderLocationAsync(emily.ResponderId, h.UserLocation);
        var arrived = await h.Emergency.GetStatusAsync(status.EmergencyId, h.UserId);

        Assert.Equal(EmergencyStatus.Arrived, arrived.Status);
        Assert.Equal(0, arrived.Responders.First(r => r.ResponderId == emily.ResponderId).EtaMinutes);
        Assert.Equal(2, await h.Notifications.CountUnreadAsync(h.UserId)); // "on the way" + "arrived"
    }

    [Fact]
    public async Task Can_message_and_call_assigned_responder_only()
    {
        var h = new Harness();
        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);
        var assigned = status.Responders[0];

        var call = await h.Messaging.GetCallInfoAsync(status.EmergencyId, h.UserId, assigned.ResponderId);
        Assert.StartsWith("tel:+1555", call.TelUri);

        await h.Messaging.SendAsync(status.EmergencyId, h.UserId, assigned.ResponderId, "  Second floor  ");
        await h.Messaging.ReplyAsResponderAsync(status.EmergencyId, assigned.ResponderId, "On my way");
        var thread = await h.Messaging.ListAsync(status.EmergencyId, h.UserId, assigned.ResponderId);

        Assert.Equal(2, thread.Count);
        Assert.Equal("Second floor", thread[0].Text);

        var unassigned = (await h.Responders.GetAllAsync()).First(r => status.Responders.All(s => s.ResponderId != r.Id));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Messaging.SendAsync(status.EmergencyId, h.UserId, unassigned.Id, "hello"));
        await Assert.ThrowsAsync<InvalidInputException>(() =>
            h.Messaging.SendAsync(status.EmergencyId, h.UserId, assigned.ResponderId, "   "));
    }

    [Fact]
    public async Task Home_summary_shows_name_address_unread_and_active_request()
    {
        var h = new Harness();

        var before = await h.Home.GetHomeAsync(h.UserId, h.UserLocation);
        Assert.Equal("Jenifer Pilman", before.UserName);
        Assert.Equal("151-171 Montclair Ave Newark, NJ 07104, USA", before.CurrentAddress);
        Assert.Null(before.ActiveEmergencyId);

        var status = await h.Emergency.TriggerSosAsync(h.UserId, h.UserLocation);
        var after = await h.Home.GetHomeAsync(h.UserId, h.UserLocation);

        Assert.Equal(status.EmergencyId, after.ActiveEmergencyId);
        Assert.Equal(1, after.UnreadNotifications);
    }

    [Fact]
    public void Haversine_is_roughly_correct()
    {
        var newark = new GeoPoint(40.7357, -74.1724);
        var nyc = new GeoPoint(40.7128, -74.0060);
        var km = newark.DistanceKmTo(nyc);
        Assert.InRange(km, 13, 16);
    }
}
