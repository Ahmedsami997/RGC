using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RGC.Shared;

namespace RGC.AdminConsole.Services;

public sealed class SessionExpiredException() : Exception("Your session has expired. Please sign in again.");

public sealed class ApiException(string message) : Exception(message);

/// <summary>REST client for the RGC server's admin API.</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    private Func<Task<string?>> _token = () => Task.FromResult<string?>(null);

    public string ServerUrl { get; }
    public string DisplayName { get; private set; } = "";
    public string? Email { get; private set; }
    /// <summary>Supplies a current access token (refreshed automatically for Microsoft 365 sign-in).</summary>
    public Func<Task<string?>> TokenProvider => _token;

    public ApiClient(string serverUrl)
    {
        ServerUrl = serverUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        using var res = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadProblemAsync(res, "Sign in failed."));

        var session = await res.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new ApiException("Empty response from server.");
        _token = () => Task.FromResult<string?>(session.Token);
        DisplayName = string.IsNullOrWhiteSpace(session.DisplayName) ? session.Username : session.DisplayName;
        return session;
    }

    public async Task<AuthConfigDto?> GetAuthConfigAsync()
    {
        // Null means an older server without Microsoft 365 support.
        using var res = await _http.GetAsync(AuthRoutes.Config);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<AuthConfigDto>() : null;
    }

    /// <summary>Uses a Microsoft 365 token source and checks the account has the Admin role.</summary>
    public async Task UseMicrosoft365Async(Func<Task<string?>> tokenProvider)
    {
        _token = tokenProvider;
        var me = await GetAsync<MeDto>(AuthRoutes.Me);
        if (!me.IsAdmin)
            throw new ApiException($"{me.Email ?? me.Name} is not an RGC announcements administrator. Ask IT to give your account the Admin role on the \"RGC Announcements\" app in Microsoft Entra.");
        DisplayName = me.Name;
        Email = me.Email;
    }

    public Task<List<ClientDto>> GetClientsAsync() => GetAsync<List<ClientDto>>("api/clients");

    public Task<List<AnnouncementSummaryDto>> GetAnnouncementsAsync() => GetAsync<List<AnnouncementSummaryDto>>("api/announcements?take=1000");

    public Task<List<RecipientStatusDto>> GetRecipientsAsync(Guid announcementId) =>
        GetAsync<List<RecipientStatusDto>>($"api/announcements/{announcementId}/recipients");

    public Task<List<ReadRecordDto>> GetClientRecordsAsync(Guid clientId) =>
        GetAsync<List<ReadRecordDto>>($"api/clients/{clientId}/records");

    public Task<StatsDto> GetStatsAsync(int days)
    {
        var tz = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
        return GetAsync<StatsDto>($"api/stats?days={days}&tz={tz}");
    }

    public Task<List<PersonSummaryDto>> GetPeopleAsync() => GetAsync<List<PersonSummaryDto>>("api/people");

    public Task<List<ReadRecordDto>> GetPersonRecordsAsync(string user) =>
        GetAsync<List<ReadRecordDto>>($"api/people/records?user={Uri.EscapeDataString(user)}");

    public async Task<ResendResultDto> ResendToUnreadAsync(Guid announcementId)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"api/announcements/{announcementId}/resend");
        using var res = await SendAsync(msg);
        await EnsureSuccessAsync(res);
        return (await res.Content.ReadFromJsonAsync<ResendResultDto>())!;
    }

    public async Task<AnnouncementSummaryDto> SendAnnouncementAsync(SendAnnouncementRequest request)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "api/announcements") { Content = JsonContent.Create(request) };
        using var res = await SendAsync(msg);
        await EnsureSuccessAsync(res);
        return (await res.Content.ReadFromJsonAsync<AnnouncementSummaryDto>())!;
    }

    public Task<List<ChatMessageDto>> GetChatAsync(Guid clientId) =>
        GetAsync<List<ChatMessageDto>>($"api/clients/{clientId}/chat");

    public async Task<ChatMessageDto> SendChatAsync(Guid clientId, string text)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"api/clients/{clientId}/chat") { Content = JsonContent.Create(new SendChatRequest(text)) };
        using var res = await SendAsync(msg);
        await EnsureSuccessAsync(res);
        return (await res.Content.ReadFromJsonAsync<ChatMessageDto>())!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, url);
        using var res = await SendAsync(msg);
        await EnsureSuccessAsync(res);
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
    {
        var token = await _token();
        if (token is null) throw new SessionExpiredException();
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(msg);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res)
    {
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new SessionExpiredException();
        if (res.StatusCode == HttpStatusCode.Forbidden) throw new ApiException("Your account is not allowed to do this (Admin role required).");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadProblemAsync(res, $"Server error ({(int)res.StatusCode})."));
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage res, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors))
                foreach (var prop in errors.EnumerateObject())
                foreach (var msg in prop.Value.EnumerateArray())
                    return msg.GetString() ?? fallback;
            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d) return d;
            if (root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t) return t;
        }
        catch { /* not a problem+json body */ }
        return res.StatusCode == HttpStatusCode.TooManyRequests ? "Too many attempts. Please wait a minute and try again." : fallback;
    }

    public void Dispose() => _http.Dispose();
}
