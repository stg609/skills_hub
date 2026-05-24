using SkillsHub.Api.Domain;

namespace SkillsHub.Api.Persistence;

public sealed class MemorySkillsRepository(IReadOnlyList<SkillRecord> seedSkills) : ISkillsRepository
{
    private readonly object _gate = new();
    private List<SkillRecord> _skills = seedSkills.ToList();
    private readonly List<(string SkillSlug, string VisitorId, DateTimeOffset CreatedAt)> _likes = [];
    private readonly List<(string Slug, DownloadSource Source, DateTimeOffset CreatedAt)> _downloads = [];
    private readonly List<SyncRun> _syncRuns = [];
    private readonly Dictionary<string, DateTimeOffset> _locks = [];

    public Task<IReadOnlyList<SkillRecord>> ListSkillsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<SkillRecord>>(_skills.ToList());
    }

    public Task<SkillPage> SearchSkillsAsync(string query, string sort, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var normalized = query.Trim().ToLowerInvariant();
            var filtered = _skills.Where(skill =>
                skill.Active &&
                (normalized.Length == 0 ||
                 $"{skill.Name} {skill.Description} {skill.Source.RepoUrl} {skill.Source.SkillDir}".ToLowerInvariant().Contains(normalized)));
            var ordered = SkillCatalog.Sort(filtered, sort).ToList();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new SkillPage(items, page, pageSize, ordered.Count, "eq"));
        }
    }

    public Task ReplaceSkillsAsync(IReadOnlyList<SkillRecord> skills, CancellationToken cancellationToken = default)
    {
        lock (_gate) _skills = skills.ToList();
        return Task.CompletedTask;
    }

    public Task<SyncSummary> UpsertIndexedSkillsAsync(string syncRunId, IReadOnlyList<SkillRecord> skills, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var existingByIdentity = _skills.ToDictionary(skill => skill.Identity);
            var created = 0;
            var updated = 0;
            foreach (var skill in skills)
            {
                if (existingByIdentity.TryGetValue(skill.Identity, out var old))
                {
                    updated++;
                    _skills = _skills.Select(item => item.Identity == skill.Identity
                        ? skill with { Downloads = old.Downloads, Likes = old.Likes, Active = true, IndexedAt = indexedAt, MissingSyncCount = 0 }
                        : item).ToList();
                }
                else
                {
                    created++;
                    _skills.Add(skill with { Active = true, IndexedAt = indexedAt, MissingSyncCount = 0 });
                }
            }

            return Task.FromResult(new SyncSummary(created, updated, 0, indexedAt));
        }
    }

    public Task<SyncSummary> CompleteIndexedSyncAsync(string syncRunId, DateTimeOffset indexedAt, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SyncSummary(0, 0, 0, indexedAt));

    public Task<SkillRecord?> FindSkillAsync(string slug, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_skills.FirstOrDefault(skill => skill.Slug == slug));
    }

    public Task<LikeResult> SetLikeAsync(string slug, string visitorId, bool liked, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var existing = _likes.FindIndex(like => like.SkillSlug == slug && like.VisitorId == visitorId);
            if (liked && existing < 0) _likes.Add((slug, visitorId, DateTimeOffset.UtcNow));
            if (!liked && existing >= 0) _likes.RemoveAt(existing);
            var likes = _likes.Count(like => like.SkillSlug == slug);
            _skills = _skills.Select(skill => skill.Slug == slug ? skill with { Likes = likes } : skill).ToList();
            return Task.FromResult(new LikeResult(slug, liked, likes));
        }
    }

    public Task<Downloads> RecordSkillEventAsync(string slug, SkillEventType eventType, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _downloads.Add((slug, eventType == SkillEventType.ZipDownloaded ? DownloadSource.Zip : DownloadSource.Npx, DateTimeOffset.UtcNow));
            var downloads = CountDownloads(slug);
            _skills = _skills.Select(skill => skill.Slug == slug ? skill with { Downloads = downloads } : skill).ToList();
            return Task.FromResult(downloads);
        }
    }

    public Task RefreshDownloadWindowsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _skills = _skills.Select(skill => skill with { Downloads = CountDownloads(skill.Slug) }).ToList();
            return Task.CompletedTask;
        }
    }

    public Task RebuildStatsAsync(CancellationToken cancellationToken = default) => RefreshDownloadWindowsAsync(cancellationToken);

    public Task<SyncRun> CreateSyncRunAsync(string source, CancellationToken cancellationToken = default)
    {
        var run = new SyncRun(Guid.NewGuid().ToString("N"), source, "running", DateTimeOffset.UtcNow, null, 0, 0, 0, null);
        lock (_gate) _syncRuns.Insert(0, run);
        return Task.FromResult(run);
    }

    public Task<SyncRun> FinishSyncRunAsync(string id, string status, int created, int updated, int deactivated, string? message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var index = _syncRuns.FindIndex(run => run.Id == id);
            var current = _syncRuns[index];
            var next = current with
            {
                Status = status,
                FinishedAt = DateTimeOffset.UtcNow,
                Created = created,
                Updated = updated,
                Deactivated = deactivated,
                Message = message
            };
            _syncRuns[index] = next;
            return Task.FromResult(next);
        }
    }

    public Task<SyncRun?> LatestSyncRunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_syncRuns.FirstOrDefault());
    }

    public Task<string?> AcquireLockAsync(string name, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_locks.TryGetValue(name, out var lockedUntil) && lockedUntil > now) return Task.FromResult<string?>(null);

            // 中文注释：内存锁只保证单进程开发环境不重入；生产跨 Pod 锁由 PostgreSQL 实现。
            _locks[name] = now.AddSeconds(ttlSeconds);
            return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
        }
    }

    public Task ReleaseLockAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        lock (_gate) _locks.Remove(name);
        return Task.CompletedTask;
    }

    public Task<bool> RenewLockAsync(string name, string ownerId, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        lock (_gate) _locks[name] = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
        return Task.FromResult(true);
    }

    public Task<bool> CheckReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private Downloads CountDownloads(string slug)
    {
        var now = DateTimeOffset.UtcNow;
        return new Downloads(
            _downloads.Count(item => item.Slug == slug && now - item.CreatedAt <= TimeSpan.FromDays(1)),
            _downloads.Count(item => item.Slug == slug && now - item.CreatedAt <= TimeSpan.FromDays(7)),
            _downloads.Count(item => item.Slug == slug));
    }
}
