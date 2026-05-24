using SkillsHub.Api.Persistence;

namespace SkillsHub.Api.Sync;

public sealed class SyncService(
    ISkillsRepository repository,
    SkillsService skills,
    GitLabIndexer indexer,
    AppConfig config)
{
    private const string LockName = "gitlab-sync";

    public async Task<SyncTriggerResult> RunGitLabSyncAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.InternalSyncToken) || token != config.InternalSyncToken)
            return new SyncTriggerResult("unauthorized", "gitlab", null, null);

        var ownerId = await repository.AcquireLockAsync(LockName, config.SyncLockTtlSeconds, cancellationToken);
        if (ownerId is null)
            return new SyncTriggerResult("running", "gitlab", "GitLab sync is already running", null);

        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewal = RenewLockUntilCancelledAsync(ownerId, renewalCts.Token);

        try
        {
            var work = PerformGitLabSyncAsync(renewalCts.Token);
            var completed = await Task.WhenAny(work, renewal);
            if (completed == renewal)
            {
                renewalCts.Cancel();
                await renewal;
                throw new InvalidOperationException("GitLab sync lock renewal failed.");
            }

            return await work;
        }
        finally
        {
            await renewalCts.CancelAsync();
            await renewal;
            await repository.ReleaseLockAsync(LockName, ownerId, CancellationToken.None);
        }
    }

    public async Task<SyncTriggerResult> PerformGitLabSyncAsync(CancellationToken cancellationToken = default)
    {
        var run = await repository.CreateSyncRunAsync("gitlab", cancellationToken);

        if (config.GitLabGroups.Count == 0 || string.IsNullOrWhiteSpace(config.GitLabToken))
        {
            var skipped = await repository.FinishSyncRunAsync(run.Id, "skipped", 0, 0, 0, "GITLAB_GROUPS or GITLAB_TOKEN is not configured", cancellationToken);
            return new SyncTriggerResult("skipped", "gitlab", skipped.Message, skipped);
        }

        try
        {
            var result = await skills.ApplyIndexedSkillBatchesAsync(run.Id, indexer.IndexBatchesAsync(100, cancellationToken), cancellationToken);
            var completed = await repository.FinishSyncRunAsync(run.Id, "completed", result.Created, result.Updated, result.Deactivated, null, cancellationToken);
            return new SyncTriggerResult("completed", "gitlab", null, completed);
        }
        catch (Exception error)
        {
            await repository.FinishSyncRunAsync(run.Id, "failed", 0, 0, 0, error.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task RenewLockUntilCancelledAsync(string ownerId, CancellationToken cancellationToken)
    {
        var intervalSeconds = Math.Max(10, config.SyncLockTtlSeconds / 3);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // 中文注释：GitLab 全量扫描可能超过锁 TTL，续租可以防止第二个 Pod 在旧任务未结束时接管同步。
                var renewed = await repository.RenewLockAsync(LockName, ownerId, config.SyncLockTtlSeconds, cancellationToken);
                if (!renewed) throw new InvalidOperationException("GitLab sync lock was lost.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

public sealed record SyncTriggerResult(string Status, string Source, string? Message, object? Run);
