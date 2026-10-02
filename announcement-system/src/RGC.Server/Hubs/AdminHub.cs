using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RGC.Server.Hubs;

/// <summary>Push channel for Admin Consoles (live connection/delivery/ack updates).</summary>
[Authorize(Roles = "Admin")]
public sealed class AdminHub : Hub;
