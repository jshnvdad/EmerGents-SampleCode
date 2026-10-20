namespace EmergencyApp.Core.Models;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? Phone { get; set; }
}

public sealed class Responder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Phone { get; set; } = "";
    public ResponderType Type { get; set; }
    public ResponderStatus Status { get; set; } = ResponderStatus.Available;
    public GeoPoint Location { get; set; }
}

public sealed class ResponderAssignment
{
    public Guid ResponderId { get; set; }
    public ResponderType Type { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    public int EtaMinutes { get; set; }
}

public sealed class EmergencyRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public GeoPoint Location { get; set; }
    public string Address { get; set; } = "";
    public EmergencyStatus Status { get; set; } = EmergencyStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public List<ResponderAssignment> Assignments { get; } = new();

    public bool IsActive => Status is EmergencyStatus.Pending or EmergencyStatus.Dispatched or EmergencyStatus.Arrived;
}

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmergencyId { get; set; }
    public Guid ResponderId { get; set; }
    public MessageSender Sender { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
}

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
