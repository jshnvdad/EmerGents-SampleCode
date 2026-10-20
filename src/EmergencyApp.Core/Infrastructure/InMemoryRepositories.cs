using System.Collections.Concurrent;
using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Models;

namespace EmergencyApp.Core.Infrastructure;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _items = new();
    public void Seed(IEnumerable<User> users) { foreach (var u in users) _items[u.Id] = u; }
    public Task<User?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.TryGetValue(id, out var u) ? u : null);
}

public sealed class InMemoryResponderRepository : IResponderRepository
{
    private readonly ConcurrentDictionary<Guid, Responder> _items = new();
    public void Seed(IEnumerable<Responder> responders) { foreach (var r in responders) _items[r.Id] = r; }
    public Task<Responder?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.TryGetValue(id, out var r) ? r : null);
    public Task<IReadOnlyList<Responder>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Responder>>(_items.Values.ToList());
    public Task AddAsync(Responder responder, CancellationToken ct = default)
    { _items[responder.Id] = responder; return Task.CompletedTask; }
    public Task UpdateAsync(Responder responder, CancellationToken ct = default)
    { _items[responder.Id] = responder; return Task.CompletedTask; }
}

public sealed class InMemoryEmergencyRepository : IEmergencyRepository
{
    private readonly ConcurrentDictionary<Guid, EmergencyRequest> _items = new();

    public Task AddAsync(EmergencyRequest request, CancellationToken ct = default)
    { _items[request.Id] = request; return Task.CompletedTask; }

    public Task<EmergencyRequest?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.TryGetValue(id, out var r) ? r : null);

    public Task<EmergencyRequest?> GetActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_items.Values.FirstOrDefault(r => r.UserId == userId && r.IsActive));

    public Task<IReadOnlyList<EmergencyRequest>> GetPendingAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmergencyRequest>>(
            _items.Values.Where(r => r.Status == EmergencyStatus.Pending).OrderBy(r => r.CreatedAt).ToList());

    public Task<IReadOnlyList<EmergencyRequest>> GetActiveByResponderAsync(Guid responderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmergencyRequest>>(
            _items.Values.Where(r => r.IsActive && r.Assignments.Any(a => a.ResponderId == responderId)).ToList());

    public Task UpdateAsync(EmergencyRequest request, CancellationToken ct = default)
    { _items[request.Id] = request; return Task.CompletedTask; }
}

public sealed class InMemoryMessageRepository : IMessageRepository
{
    private readonly ConcurrentQueue<ChatMessage> _items = new();
    public Task AddAsync(ChatMessage message, CancellationToken ct = default)
    { _items.Enqueue(message); return Task.CompletedTask; }
    public Task<IReadOnlyList<ChatMessage>> ListAsync(Guid emergencyId, Guid responderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(
            _items.Where(m => m.EmergencyId == emergencyId && m.ResponderId == responderId)
                  .OrderBy(m => m.SentAt).ToList());
}

public sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly ConcurrentQueue<Notification> _items = new();
    public Task AddAsync(Notification notification, CancellationToken ct = default)
    { _items.Enqueue(notification); return Task.CompletedTask; }
    public Task<IReadOnlyList<Notification>> ListAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Notification>>(
            _items.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).ToList());
    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_items.Count(n => n.UserId == userId && !n.IsRead));
    public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        foreach (var n in _items.Where(n => n.UserId == userId)) n.IsRead = true;
        return Task.CompletedTask;
    }
}
