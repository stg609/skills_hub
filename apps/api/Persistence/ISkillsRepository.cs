using SkillsHub.Api.Domain;

namespace SkillsHub.Api.Persistence;

public interface ISkillsRepository
{
    Task<IReadOnlyList<SkillRecord>> ListSkillsAsync(CancellationToken cancellationToken = default);
    Task<SkillPage> SearchSkillsAsync(string query, string sort, int page, int pageSize, CancellationToken cancellationToken = default);
    Task ReplaceSkillsAsync(IReadOnlyList<SkillRecord> skills, CancellationToken cancellationToken = default);
    Task<SyncSummary> UpsertIndexedSkillsAsync(string syncRunId, IReadOnlyList<SkillRecord> skills, DateTimeOffset indexedAt, CancellationToken cancellationToken = default);
    Task<SyncSummary> CompleteIndexedSyncAsync(string syncRunId, DateTimeOffset indexedAt, CancellationToken cancellationToken = default);
    Task<SkillRecord?> FindSkillAsync(string slug, CancellationToken cancellationToken = default);
    Task<LikeResult> SetLikeAsync(string slug, string visitorId, bool liked, CancellationToken cancellationToken = default);
    Task<Downloads> RecordSkillEventAsync(string slug, SkillEventType eventType, CancellationToken cancellationToken = default);
    Task RefreshDownloadWindowsAsync(CancellationToken cancellationToken = default);
    Task RebuildStatsAsync(CancellationToken cancellationToken = default);
    Task<SyncRun> CreateSyncRunAsync(string source, CancellationToken cancellationToken = default);
    Task<SyncRun> FinishSyncRunAsync(string id, string status, int created, int updated, int deactivated, string? message, CancellationToken cancellationToken = default);
    Task<SyncRun?> LatestSyncRunAsync(CancellationToken cancellationToken = default);
    Task<string?> AcquireLockAsync(string name, int ttlSeconds, CancellationToken cancellationToken = default);
    Task<bool> RenewLockAsync(string name, string ownerId, int ttlSeconds, CancellationToken cancellationToken = default);
    Task ReleaseLockAsync(string name, string ownerId, CancellationToken cancellationToken = default);
    Task<bool> CheckReadyAsync(CancellationToken cancellationToken = default);
}
