using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Services;

public sealed class MessagingService(
    IEmergencyRepository emergencies,
    IResponderRepository responders,
    IMessageRepository messages,
    INotificationRepository notifications,
    IEmergencyEventPublisher events,
    TimeProvider clock) : IMessagingService
{
    private const int MaxMessageLength = 500;

    public async Task<CallInfo> GetCallInfoAsync(Guid emergencyId, Guid userId, Guid responderId, CancellationToken ct = default)
    {
        var (_, responder) = await ValidateAsync(emergencyId, userId, responderId, requireActive: true, ct);
        var digits = new string(responder.Phone.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
        return new CallInfo(responder.Phone, $"tel:{digits}");
    }

    public async Task<ChatMessage> SendAsync(Guid emergencyId, Guid userId, Guid responderId, string text, CancellationToken ct = default)
    {
        text = NormalizeText(text);
        await ValidateAsync(emergencyId, userId, responderId, requireActive: true, ct);

        var msg = await StoreAsync(emergencyId, responderId, MessageSender.User, text, ct);
        await events.PublishAsync(new(emergencyId, EmergencyEventType.MessageReceived, msg), ct);
        return msg;
    }

    public async Task<ChatMessage> ReplyAsResponderAsync(Guid emergencyId, Guid responderId, string text, CancellationToken ct = default)
    {
        text = NormalizeText(text);
        var request = await emergencies.GetAsync(emergencyId, ct) ?? throw new NotFoundException("Emergency not found.");
        if (!request.IsActive) throw new ConflictException("This emergency is no longer active.");
        if (request.Assignments.All(a => a.ResponderId != responderId))
            throw new ForbiddenException("Responder is not assigned to this emergency.");
        var responder = await responders.GetAsync(responderId, ct) ?? throw new NotFoundException("Responder not found.");

        var msg = await StoreAsync(emergencyId, responderId, MessageSender.Responder, text, ct);
        await notifications.AddAsync(new Notification
        {
            UserId = request.UserId,
            Title = $"Message from {responder.Name}",
            Body = text,
            CreatedAt = clock.GetUtcNow()
        }, ct);
        await events.PublishAsync(new(emergencyId, EmergencyEventType.MessageReceived, msg), ct);
        return msg;
    }

    public async Task<IReadOnlyList<ChatMessage>> ListAsync(Guid emergencyId, Guid userId, Guid responderId, CancellationToken ct = default)
    {
        await ValidateAsync(emergencyId, userId, responderId, requireActive: false, ct);
        return await messages.ListAsync(emergencyId, responderId, ct);
    }

    private async Task<ChatMessage> StoreAsync(Guid emergencyId, Guid responderId, MessageSender sender, string text, CancellationToken ct)
    {
        var msg = new ChatMessage
        {
            EmergencyId = emergencyId, ResponderId = responderId,
            Sender = sender, Text = text, SentAt = clock.GetUtcNow()
        };
        await messages.AddAsync(msg, ct);
        return msg;
    }

    private async Task<(EmergencyRequest Request, Responder Responder)> ValidateAsync(
        Guid emergencyId, Guid userId, Guid responderId, bool requireActive, CancellationToken ct)
    {
        var request = await emergencies.GetAsync(emergencyId, ct) ?? throw new NotFoundException("Emergency not found.");
        if (request.UserId != userId) throw new ForbiddenException("This emergency belongs to another user.");
        if (requireActive && !request.IsActive) throw new ConflictException("This emergency is no longer active.");
        if (request.Assignments.All(a => a.ResponderId != responderId))
            throw new NotFoundException("Responder is not assigned to this emergency.");
        var responder = await responders.GetAsync(responderId, ct) ?? throw new NotFoundException("Responder not found.");
        return (request, responder);
    }

    private static string NormalizeText(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) throw new InvalidInputException("Message can't be empty.");
        if (text.Length > MaxMessageLength) throw new InvalidInputException($"Message is limited to {MaxMessageLength} characters.");
        return text;
    }
}
