using System.Collections.Concurrent;

namespace RGC.Server.Services;

/// <summary>Tracks which SignalR connections belong to which client computer.</summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, Guid> _connections = new();

    public void Add(string connectionId, Guid clientId) => _connections[connectionId] = clientId;

    public Guid? Remove(string connectionId) =>
        _connections.TryRemove(connectionId, out var clientId) ? clientId : null;

    public Guid? GetClientId(string connectionId) =>
        _connections.TryGetValue(connectionId, out var clientId) ? clientId : null;

    public bool HasConnections(Guid clientId) => _connections.Values.Any(v => v == clientId);

    public IReadOnlyCollection<Guid> ConnectedClientIds() => _connections.Values.Distinct().ToList();

    public IReadOnlyList<string> ConnectionsFor(Guid clientId) =>
        _connections.Where(kv => kv.Value == clientId).Select(kv => kv.Key).ToList();
}
