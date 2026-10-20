using System.Text.Json.Serialization;
using EmergencyApp.Api;
using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Infrastructure;
using EmergencyApp.Core.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Register the SignalR publisher BEFORE AddEmergencyCore so it wins over the null publisher.
builder.Services.AddSingleton<IEmergencyEventPublisher, SignalREventPublisher>();
builder.Services.AddEmergencyCore(seedDemoData: true);
builder.Services.AddHostedService<DispatchRetryWorker>();

var app = builder.Build();

// Domain exceptions -> HTTP status codes
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DomainException ex)
    {
        ctx.Response.StatusCode = ex switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ForbiddenException => StatusCodes.Status403Forbidden,
            ConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapHub<EmergencyHub>("/hubs/emergency");

// NOTE: "X-User-Id" stands in for real authentication (JWT/cookie). Swap it for the authenticated user's id.
const string UserHeader = "X-User-Id";

var api = app.MapGroup("/api");

// ---- Home screen ----
api.MapGet("/home", (IHomeService svc, [FromHeader(Name = UserHeader)] Guid userId, double lat, double lng, CancellationToken ct) =>
    svc.GetHomeAsync(userId, new GeoPoint(lat, lng), ct));

api.MapGet("/notifications", (INotificationService svc, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    svc.ListAsync(userId, ct));

api.MapPost("/notifications/read-all", async (INotificationService svc, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
{
    await svc.MarkAllReadAsync(userId, ct);
    return Results.NoContent();
});

// ---- Emergency flow ----
var emergencies = api.MapGroup("/emergencies");

// SOS button
emergencies.MapPost("/", async (IEmergencyService svc, [FromHeader(Name = UserHeader)] Guid userId, LocationBody body, CancellationToken ct) =>
{
    var status = await svc.TriggerSosAsync(userId, new GeoPoint(body.Latitude, body.Longitude), ct);
    return Results.Created($"/api/emergencies/{status.EmergencyId}", status);
});

// "Ambulance and responders are on a way"
emergencies.MapGet("/{id:guid}", (IEmergencyService svc, Guid id, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    svc.GetStatusAsync(id, userId, ct));

// "Cancel request"
emergencies.MapPost("/{id:guid}/cancel", (IEmergencyService svc, Guid id, [FromHeader(Name = UserHeader)] Guid userId, CancelBody? body, CancellationToken ct) =>
    svc.CancelAsync(id, userId, body?.Reason, ct));

// "See responders on map"
emergencies.MapGet("/{id:guid}/map", (IEmergencyService svc, Guid id, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    svc.GetMapAsync(id, userId, ct));

// "Open full instruction"
emergencies.MapGet("/{id:guid}/instructions", (IEmergencyService svc, Guid id, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    svc.GetInstructionsAsync(id, userId, ct));

// "Call"
emergencies.MapPost("/{id:guid}/responders/{responderId:guid}/call", (IMessagingService svc, Guid id, Guid responderId, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    svc.GetCallInfoAsync(id, userId, responderId, ct));

// "Message"
emergencies.MapGet("/{id:guid}/responders/{responderId:guid}/messages", (IMessagingService svc, Guid id, Guid responderId, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    svc.ListAsync(id, userId, responderId, ct));

emergencies.MapPost("/{id:guid}/responders/{responderId:guid}/messages", (IMessagingService svc, Guid id, Guid responderId, [FromHeader(Name = UserHeader)] Guid userId, MessageBody body, CancellationToken ct) =>
    svc.SendAsync(id, userId, responderId, body.Text, ct));

// ---- Responder-side (would live behind responder/dispatcher auth) ----
var responders = api.MapGroup("/responders");

responders.MapPost("/{responderId:guid}/location", async (IEmergencyService svc, Guid responderId, LocationBody body, CancellationToken ct) =>
{
    await svc.UpdateResponderLocationAsync(responderId, new GeoPoint(body.Latitude, body.Longitude), ct);
    return Results.NoContent();
});

responders.MapPost("/{responderId:guid}/emergencies/{id:guid}/messages", (IMessagingService svc, Guid responderId, Guid id, MessageBody body, CancellationToken ct) =>
    svc.ReplyAsResponderAsync(id, responderId, body.Text, ct));

api.MapPost("/emergencies/{id:guid}/resolve", async (IEmergencyService svc, Guid id, CancellationToken ct) =>
{
    await svc.ResolveAsync(id, ct);
    return Results.NoContent();
});

// ---- Dev helper: moves every assigned responder 35% closer to the user so you can watch ETAs drop ----
if (app.Environment.IsDevelopment())
{
    api.MapPost("/dev/emergencies/{id:guid}/simulate-tick", async (IEmergencyService svc, Guid id, [FromHeader(Name = UserHeader)] Guid userId, CancellationToken ct) =>
    {
        var map = await svc.GetMapAsync(id, userId, ct);
        foreach (var r in map.Responders)
            await svc.UpdateResponderLocationAsync(r.Responder.ResponderId, r.Location.Lerp(map.UserLocation, 0.35), ct);
        return await svc.GetMapAsync(id, userId, ct);
    });
}

app.Run();

public sealed record LocationBody(double Latitude, double Longitude);
public sealed record CancelBody(string? Reason);
public sealed record MessageBody(string Text);

public partial class Program { }
