using SkillsHub.Api.Domain;
using SkillsHub.Api.GitLab;
using SkillsHub.Api.Packaging;
using SkillsHub.Api.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace SkillsHub.Api;

public sealed class SkillsService(
    ISkillsRepository repository,
    GitLabClient gitlab,
    SkillPackageBuilder packageBuilder,
    AppConfig config)
{
    private const int MissingSyncGraceRuns = 3;
    private readonly SemaphoreSlim _packageBuilds = new(config.MaxConcurrentPackageBuilds, config.MaxConcurrentPackageBuilds);

    public Task<SkillPage> ListAsync(string query, string sort, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize <= 0 ? 30 : pageSize, 1, 100);
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length > 120) normalizedQuery = normalizedQuery[..120];
        return repository.SearchSkillsAsync(normalizedQuery, sort, normalizedPage, normalizedPageSize, cancellationToken);
    }

    public Task<SkillRecord?> DetailAsync(string slug, CancellationToken cancellationToken = default) =>
        repository.FindSkillAsync(slug, cancellationToken);

    public async Task<LikeResult?> SetLikeAsync(string slug, string visitorId, bool liked, CancellationToken cancellationToken = default)
    {
        if (await repository.FindSkillAsync(slug, cancellationToken) is null) return null;
        return await repository.SetLikeAsync(slug, visitorId, liked, cancellationToken);
    }

    public async Task<DownloadResult?> TrackDownloadAsync(string slug, DownloadSource source, CancellationToken cancellationToken = default)
    {
        var skill = await repository.FindSkillAsync(slug, cancellationToken);
        if (skill is null) return null;
        var eventType = source == DownloadSource.Zip ? SkillEventType.ZipDownloaded : SkillEventType.InstallCommandCopied;
        var downloads = await repository.RecordSkillEventAsync(slug, eventType, cancellationToken);
        return new DownloadResult(slug, source == DownloadSource.Zip ? "zip" : "npx", downloads, skill.InstallCommand, TrackedInstallCommand(skill));
    }

    public async Task<(IReadOnlyList<SkillRecord> Skills, SyncSummary Summary)> ApplyIndexedSkillsAsync(IReadOnlyList<SkillRecord> incoming, CancellationToken cancellationToken = default)
    {
        var existing = await repository.ListSkillsAsync(cancellationToken);
        var indexedAt = DateTimeOffset.UtcNow;
        var normalizedIncoming = EnsureUniqueSlugs(incoming, existing);
        var existingByIdentity = existing.ToDictionary(skill => skill.Identity);
        var incomingIdentities = normalizedIncoming.Select(skill => skill.Identity).ToHashSet();
        var next = new List<SkillRecord>();
        var created = 0;
        var updated = 0;
        var deactivated = 0;

        foreach (var skill in normalizedIncoming)
        {
            if (!existingByIdentity.TryGetValue(skill.Identity, out var old))
            {
                created++;
                next.Add(skill with { Active = true, IndexedAt = indexedAt, MissingSyncCount = 0 });
                continue;
            }

            updated++;
            // 中文注释：GitLab 元数据可以刷新，但下载/点赞属于 Hub 自己的统计，不能被索引覆盖。
            next.Add(skill with { Downloads = old.Downloads, Likes = old.Likes, Active = true, IndexedAt = indexedAt, MissingSyncCount = 0 });
        }

        foreach (var old in existing.Where(skill => !incomingIdentities.Contains(skill.Identity)))
        {
            var missingSyncCount = old.MissingSyncCount + 1;
            var active = missingSyncCount < MissingSyncGraceRuns && old.Active;
            if (old.Active && !active) deactivated++;
            // 中文注释：GitLab 同步可能因为分页、权限或网络短暂失败而漏扫，连续缺失多次后才下架，避免一次失败导致批量误隐藏。
            next.Add(old with { Active = active, IndexedAt = indexedAt, MissingSyncCount = missingSyncCount });
        }

        await repository.ReplaceSkillsAsync(next, cancellationToken);
        return (next, new SyncSummary(created, updated, deactivated, indexedAt));
    }

    public async Task<SyncSummary> ApplyIndexedSkillBatchesAsync(string syncRunId, IAsyncEnumerable<IReadOnlyList<SkillRecord>> batches, CancellationToken cancellationToken = default)
    {
        var indexedAt = DateTimeOffset.UtcNow;
        var created = 0;
        var updated = 0;
        await foreach (var batch in batches.WithCancellation(cancellationToken))
        {
            var result = await repository.UpsertIndexedSkillsAsync(syncRunId, batch, indexedAt, cancellationToken);
            created += result.Created;
            updated += result.Updated;
        }

        var completed = await repository.CompleteIndexedSyncAsync(syncRunId, indexedAt, cancellationToken);
        return new SyncSummary(created, updated, completed.Deactivated, indexedAt);
    }

    private static IReadOnlyList<SkillRecord> EnsureUniqueSlugs(IReadOnlyList<SkillRecord> incoming, IReadOnlyList<SkillRecord> existing)
    {
        var occupied = existing.ToDictionary(skill => skill.Identity, skill => skill.Slug);
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in existing)
        {
            used.TryAdd(skill.Slug, skill.Identity);
        }

        return incoming.Select(skill =>
        {
            var slug = occupied.TryGetValue(skill.Identity, out var previousSlug) ? previousSlug : skill.Slug;
            if (used.TryGetValue(slug, out var owner) && owner != skill.Identity)
            {
                slug = $"{skill.Slug}-{StableSuffix(skill.Identity)}";
            }

            used[slug] = skill.Identity;
            return skill with { Slug = slug };
        }).ToList();
    }

    private static string StableSuffix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public async Task<SkillPackage?> DownloadPackageAsync(string slug, CancellationToken cancellationToken = default)
    {
        var skill = await repository.FindSkillAsync(slug, cancellationToken);
        if (skill is null) return null;

        await _packageBuilds.WaitAsync(cancellationToken);
        try
        {
            var tree = await gitlab.ListRepositoryTreeAsync(skill.Source.ProjectId, skill.Source.DefaultBranch, skill.Source.SkillDir == "." ? "" : skill.Source.SkillDir, cancellationToken);
            var entries = packageBuilder.BuildEntries(skill, tree, config);
            var archiveFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var remainingBytes = config.MaxPackageBytes;

            foreach (var entry in entries)
            {
                var bytes = await gitlab.GetRawFileBytesLimitedAsync(skill.Source.ProjectId, entry.SourcePath, skill.Source.DefaultBranch, remainingBytes, cancellationToken);
                remainingBytes -= bytes.LongLength;
                archiveFiles[entry.ArchivePath] = bytes;
            }

            if (!archiveFiles.ContainsKey("SKILL.md"))
                throw new InvalidOperationException("skill package root SKILL.md not found");

            await repository.RecordSkillEventAsync(slug, SkillEventType.ZipDownloaded, cancellationToken);
            return new SkillPackage($"{skill.Slug}.zip", SkillPackageBuilder.Zip(archiveFiles));
        }
        finally
        {
            _packageBuilds.Release();
        }
    }

    public async Task<InstallInfo?> InstallInfoAsync(string slug, CancellationToken cancellationToken = default)
    {
        var skill = await repository.FindSkillAsync(slug, cancellationToken);
        if (skill is null) return null;
        return new InstallInfo(
            slug,
            skill.InstallCommand,
            TrackedInstallCommand(skill),
            "native npx commands that point directly at GitLab cannot report actual installs to the Hub; use the tracked wrapper command for counted CLI installs.");
    }

    public async Task<DownloadResult?> TrackWrapperInstallAsync(string slug, CancellationToken cancellationToken = default)
    {
        var skill = await repository.FindSkillAsync(slug, cancellationToken);
        if (skill is null) return null;
        var downloads = await repository.RecordSkillEventAsync(slug, SkillEventType.WrapperCliInstalled, cancellationToken);
        return new DownloadResult(slug, "wrapper-cli", downloads, skill.InstallCommand, TrackedInstallCommand(skill));
    }

    private string TrackedInstallCommand(SkillRecord skill) =>
        $"npx {config.WrapperPackageName} add {Uri.EscapeDataString(skill.Slug)} --hub {config.HubPublicUrl}";
}
