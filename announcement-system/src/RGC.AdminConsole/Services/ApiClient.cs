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

    public string ServerUrl { get; }
    public LoginResponse? Session { get; private set; }

    public ApiClient(string serverUrl)
    {
        ServerUrl = serverUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        using var res = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadProblemAsync(res, "Sign in failed."));

        Session = await res.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new ApiException("Empty response from server.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
        return Session;
    }

    public Task<List<ClientDto>> GetClientsAsync() => GetAsync<List<ClientDto>>("api/clients");

    public Task<List<AnnouncementSummaryDto>> GetAnnouncementsAsync() => GetAsync<List<AnnouncementSummaryDto>>("api/announcements?take=1000");

    public Task<List<RecipientStatusDto>> GetRecipientsAsync(Guid announcementId) =>
        GetAsync<List<RecipientStatusDto>>($"api/announcements/{announcementId}/recipients");

    public async Task<AnnouncementSummaryDto> SendAnnouncementAsync(SendAnnouncementRequest request)
    {
        using var res = await _http.PostAsJsonAsync("api/announcements", request);
        await EnsureSuccessAsync(res);
        return (await res.Content.ReadFromJsonAsync<AnnouncementSummaryDto>())!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var res = await _http.GetAsync(url);
        await EnsureSuccessAsync(res);
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res)
    {
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new SessionExpiredException();
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
