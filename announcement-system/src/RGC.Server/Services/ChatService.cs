using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RGC.Server.Data;
using RGC.Server.Hubs;
using RGC.Shared;

namespace RGC.Server.Services;

/// <summary>IT support chat between Admin Consoles and the user of one PC.</summary>
public sealed class ChatService(
    RgcDbContext db,
    ConnectionRegistry registry,
    IHubContext<AgentHub> agentHub,
    IHubContext<AdminHub> adminHub)
{
    public enum SendResult { Sent, NotFound, Offline }

    public static string? Validate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "Type a message." :
        text.Length > ChatLimits.MaxLength ? $"Messages can be at most {ChatLimits.MaxLength} characters." : null;

    public async Task<List<ChatMessageDto>> GetHistoryAsync(Guid clientId, CancellationToken ct)
    {
        var machine = await db.Clients.Where(c => c.Id == clientId).Select(c => c.MachineName).FirstOrDefaultAsync(ct) ?? "";
        var rows = await db.ChatMessages
            .Where(m => m.ClientId == clientId)
            .OrderByDescending(m => m.SentAtUtc).ThenByDescending(m => m.Id)
            .Take(200)
            .ToListAsync(ct);
        rows.Reverse();
        return rows.Select(m => ToDto(m, machine)).ToList();
    }

    /// <summary>IT writes to a PC. Chat is live only: the PC must be connected.</summary>
    public async Task<(SendResult Result, ChatMessageDto? Message)> SendFromAdminAsync(
        Guid clientId, string text, string author, CancellationToken ct)
    {
        var machine = await db.Clients.Where(c => c.Id == clientId).Select(c => c.MachineName).FirstOrDefaultAsync(ct);
        if (machine is null) return (SendResult.NotFound, null);

        var connections = registry.ConnectionsFor(clientId);
        if (connections.Count == 0) return (SendResult.Offline, null);

        var dto = await SaveAsync(clientId, machine, fromAdmin: true, author, text, ct);
        await agentHub.Clients.Clients(connections).SendAsync(AgentClientMethods.ReceiveChat, dto, ct);
        await adminHub.Clients.All.SendAsync(AdminClientMethods.ChatMessage, dto, ct);
        return (SendResult.Sent, dto);
    }

    /// <summary>The PC's user writes to IT; every open Admin Console sees it.</summary>
    public async Task<ChatMessageDto?> SendFromPcAsync(Guid clientId, string text, string author, CancellationToken ct)
    {
        var machine = await db.Clients.Where(c => c.Id == clientId).Select(c => c.MachineName).FirstOrDefaultAsync(ct);
        if (machine is null) return null;

        var dto = await SaveAsync(clientId, machine, fromAdmin: false, author, text, ct);
        await adminHub.Clients.All.SendAsync(AdminClientMethods.ChatMessage, dto, ct);
        // Echo to the PC's other connections (e.g. two Windows users signed in) so they stay in step.
        await agentHub.Clients.Clients(registry.ConnectionsFor(clientId)).SendAsync(AgentClientMethods.ReceiveChat, dto, ct);
        return dto;
    }

    private async Task<ChatMessageDto> SaveAsync(Guid clientId, string machine, bool fromAdmin, string author, string text, CancellationToken ct)
    {
        var row = new ChatMessage
        {
            ClientId = clientId,
            FromAdmin = fromAdmin,
            Author = author.Length <= 200 ? author : author[..200],
            Text = text.Trim(),
            SentAtUtc = DateTime.UtcNow
        };
        db.ChatMessages.Add(row);
        await db.SaveChangesAsync(ct);
        return ToDto(row, machine);
    }

    private static ChatMessageDto ToDto(ChatMessage m, string machine) =>
        new(m.Id, m.ClientId, machine, m.FromAdmin, m.Author, m.Text, m.SentAtUtc);
}
