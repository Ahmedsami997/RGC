# RGC – Company Announcement System

Real-time company-wide announcements for Royal Golf Club PCs, built with **C# .NET 8, WPF, SignalR and SQL Server**.

```
 ┌──────────────────┐   HTTPS/REST + SignalR    ┌──────────────────────┐   SignalR (WebSocket)   ┌────────────────┐
 │  RGC Admin       │ ────────────────────────▶ │  RGC Server          │ ◀─────────────────────▶ │ RGC Agent (PC) │ × N
 │  Console (WPF)   │ ◀── live status pushes ── │  ASP.NET Core 8      │                         │ tray app (WPF) │
 └──────────────────┘                           │  SignalR hubs        │                         └────────────────┘
                                                │  EF Core ─▶ SQL Server│
                                                └──────────────────────┘
```

| Project | Output | What it is |
|---|---|---|
| `src/RGC.AdminConsole` | `RGC.Admin.exe` | **Admin Console** – secure login, dashboard of connected PCs, compose & broadcast, history, delivery and read status |
| `src/RGC.ClientAgent`  | `RGC.Agent.exe` | **Client Agent** – runs silently in the system tray, starts with Windows, shows the locked announcement popup |
| `src/RGC.Server`       | `RGC.Server.exe` | Central SignalR hub + REST API + SQL Server storage (runs as a Windows Service) |
| `src/RGC.Shared`       | library | Contracts (DTOs, hub method names, priorities) shared by all three |
| `src/RGC.Branding`     | library | RGC logo, icon, colours and WPF styles shared by both desktop apps |

> SignalR needs a hub that both apps connect to, so alongside the two desktop
> applications there is one small server component. It is the only thing that talks to SQL Server.

## Features

**Admin Console**
- Secure sign-in (PBKDF2-hashed passwords, 5 failed attempts → 15 min lockout, per-IP rate limit, 8 h JWT session)
- Dashboard: online / offline / registered PCs, searchable list with user, IP, agent version, last seen – updates live
- New announcement: title, message, priority (**Normal**, **Important**, **Critical**) with a live preview of the popup
- Broadcast to all PCs with confirmation
- History of every announcement ever sent, with delivered x/y and read x/y per announcement
- Per-PC status: *Pending delivery* → *Delivered – not read yet* → *Read* with delivered time, acknowledged time and Windows user; filter and **Export CSV**

**Client Agent**
- No window – lives in the system tray (status: connected / offline, "Show last announcement", "Open log folder")
- Starts automatically with Windows (HKLM Run via installer, or HKCU Run self-registration)
- Connects on startup and reconnects forever if the server or network drops
- PCs that were offline receive announcements when they come back (within 72 h, configurable)
- Acknowledgements are saved locally first, so none are lost if the server is unreachable

**Announcement popup**
- Modal, always on top of all applications (re-asserted every 2 s and on focus loss)
- No Close (X) button and no close action for the first 10 seconds, with the countdown
  *"You can close this message in 10 seconds..."*
- Alt+F4, Escape and the taskbar "Close window" are blocked
- After the countdown the **Close** button (and X) appear. If the message is long, the user must scroll to the end first
- Logs the displayed and acknowledged timestamps (sent to the server and written to the local log)
- Priority colour, sound and badge; queued if several arrive at once
- Never blocks Windows shutdown / log-off

## Setup

> **Online with Microsoft 365 sign-in?** Follow [AZURE-AND-M365-SETUP.md](AZURE-AND-M365-SETUP.md) instead –
> Azure App Service + Azure SQL, admins and staff sign in with their 365 accounts.


### 1. Database (SQL Server)
The server creates the `RGC_Announcements` database and tables automatically on first start.
If your DBA prefers to create them up front, run `database/CreateDatabase.sql`, and
`database/GrantServiceAccount.sql` to grant the service account access.

Tables: `AdminUsers`, `Clients`, `Announcements`, `AnnouncementRecipients` (delivery + acknowledgement per PC).

### 2. Server
Edit `src/RGC.Server/appsettings.json` (or set environment variables such as `Jwt__SigningKey`):

| Setting | Value |
|---|---|
| `ConnectionStrings:RgcDatabase` | your SQL Server connection string |
| `Jwt:SigningKey` | random secret, **32+ characters** |
| `Announcements:AgentKey` | random secret shared with all agents, **16+ characters** |
| `BootstrapAdmin:Password` | password for the first `admin` account (remove after first start) |
| `Kestrel:Endpoints` | listen address – default `http://0.0.0.0:5080` |

The server refuses to start while the `CHANGE-ME` placeholders are still in place.
Generate secrets in PowerShell 7 with: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`

More admins / password resets:
```powershell
RGC.Server.exe add-admin jane "S3cure-Passw0rd" "Jane Smith"
RGC.Server.exe reset-password jane "N3w-Passw0rd"
```

**Use HTTPS in production** – add an `Https` endpoint with your certificate under `Kestrel:Endpoints`
(or put the server behind IIS / a reverse proxy) so passwords and tokens are encrypted on the network.

### 3. Build the packages (on a Windows machine with the .NET 8 SDK)
```powershell
.\deploy\Publish.ps1
```
Produces self-contained `publish\Server`, `publish\AdminConsole`, `publish\Agent` (no .NET install needed on PCs).

### 4. Install
```powershell
# On the server (as Administrator)
cd publish\Server ; .\Install-Server.ps1 -ServiceAccount "DOMAIN\svc-rgc" -ServiceAccountPassword "..."

# On every PC (as Administrator, or push with GPO / Intune / SCCM)
cd publish\Agent  ; .\Install-Agent.ps1 -ServerUrl "http://rgc-server:5080" -AgentKey "<the AgentKey>"
```
The Admin Console needs no install – run `RGC.Admin.exe` and enter the server address on the sign-in screen
(default comes from `adminsettings.json`).

### Agent configuration
`%ProgramData%\RGC\agentsettings.json` (written by the installer; overrides the copy next to the exe):
```json
{ "ServerUrl": "http://rgc-server:5080", "AgentKey": "...", "CountdownSeconds": 10, "AutoStart": true, "AllowUserExit": false }
```
Logs: `%LocalAppData%\RGC\Logs\agent-YYYYMMDD.log` (connections, every announcement received and acknowledged).

## Development
```bash
dotnet build RGC.sln            # builds everything (WPF projects build on Windows; on Linux/macOS EnableWindowsTargeting is set)
dotnet run --project src/RGC.Server
```
Server API (all under `/api`, admin JWT required except login/health):
`POST /auth/login`, `GET /clients`, `GET /announcements`, `GET /announcements/{id}/recipients`, `POST /announcements`, `GET /health`.
Hubs: `/hubs/agent` (agent key header `X-RGC-Agent-Key`) and `/hubs/admin` (admin JWT).
