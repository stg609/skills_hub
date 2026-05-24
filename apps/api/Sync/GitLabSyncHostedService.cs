namespace SkillsHub.Api.Sync;

public sealed class GitLabSyncHostedService(SyncService sync, AppConfig config, ILogger<GitLabSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GitLabSyncEnabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay();
            await Task.Delay(delay, stoppingToken);
            try
            {
                await sync.RunGitLabSyncAsync(config.InternalSyncToken, stoppingToken);
            }
            catch (Exception error)
            {
                logger.LogError(error, "GitLab scheduled sync failed.");
            }
        }
    }

    private static TimeSpan NextRunDelay()
    {
        var now = DateTimeOffset.Now;
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, 3, 10, 0, now.Offset);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
