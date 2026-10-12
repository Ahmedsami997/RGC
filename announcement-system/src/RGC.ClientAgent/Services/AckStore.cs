using System.IO;
using System.Text.Json;
using RGC.Shared;

namespace RGC.ClientAgent.Services;

/// <summary>
/// Persists acknowledgements locally so none are lost while the server is unreachable,
/// and remembers which announcements were already acknowledged on this PC.
/// </summary>
public sealed class AckStore
{
    private const int MaxRemembered = 1000;
    private readonly object _gate = new();
    private readonly string _file = Path.Combine(AgentPaths.UserDataDir, "acknowledgements.json");
    private readonly State _state;

    public AckStore()
    {
        _state = Load();
    }

    public bool IsAcknowledged(Guid announcementId)
    {
        lock (_gate) return _state.Acknowledged.Contains(announcementId);
    }

    public void Record(AcknowledgementDto ack)
    {
        lock (_gate)
        {
            if (!_state.Acknowledged.Contains(ack.AnnouncementId))
                _state.Acknowledged.Add(ack.AnnouncementId);
            if (_state.Acknowledged.Count > MaxRemembered)
                _state.Acknowledged.RemoveRange(0, _state.Acknowledged.Count - MaxRemembered);
            _state.Pending.RemoveAll(p => p.AnnouncementId == ack.AnnouncementId);
            _state.Pending.Add(ack);
            Save();
        }
    }

    public AcknowledgementDto? FindPending(Guid announcementId)
    {
        lock (_gate) return _state.Pending.FirstOrDefault(p => p.AnnouncementId == announcementId);
    }

    public List<AcknowledgementDto> GetPending()
    {
        lock (_gate) return _state.Pending.ToList();
    }

    public void MarkSent(Guid announcementId)
    {
        lock (_gate)
        {
            if (_state.Pending.RemoveAll(p => p.AnnouncementId == announcementId) > 0) Save();
        }
    }

    private State Load()
    {
        try
        {
            if (File.Exists(_file))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(_file)) ?? new State();
        }
        catch (Exception ex)
        {
            AgentLog.Error("Could not read acknowledgement store; starting fresh", ex);
        }
        return new State();
    }

    private void Save()
    {
        try
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_state));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception ex)
        {
            AgentLog.Error("Could not save acknowledgement store", ex);
        }
    }

    private sealed class State
    {
        public List<Guid> Acknowledged { get; set; } = new();
        public List<AcknowledgementDto> Pending { get; set; } = new();
    }
}
