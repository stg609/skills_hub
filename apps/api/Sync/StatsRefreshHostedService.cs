using SkillsHub.Api.Persistence;

namespace SkillsHub.Api.Sync;

public sealed class StatsRefreshHostedService(
    ISkillsRepository repository,
    ILogger<StatsRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshOnceAsync(stoppingToken);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var ownerId = await repository.AcquireLockAsync("stats-refresh", 60 * 10, cancellationToken);
        if (ownerId is null) return;

        try
        {
            // 中文注释：downloads_1d/7d 是随自然日变化的窗口值，必须由后台定期刷新，不能依赖用户下载时顺手更新。
            await repository.RefreshDownloadWindowsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh download windows.");
        }
        finally
        {
            await repository.ReleaseLockAsync("stats-refresh", ownerId, CancellationToken.None);
        }
    }
}
