namespace RGC.Server.Services;

/// <summary>
/// PCs that stay connected for days never re-register, so mark every connected PC
/// as active periodically; otherwise they would vanish from "active PCs per day".
/// </summary>
public sealed class ActivityTracker(IServiceScopeFactory scopes, ConnectionRegistry registry, ILogger<ActivityTracker> log)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var connected = registry.ConnectedClientIds();
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ClientDirectory>().RecordActivityAsync(connected, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Could not record PC activity");
            }
        }
    }
}
