using System.Text.Json.Serialization;

namespace SkillsHub.Api.Domain;

public sealed record Downloads(int D1, int D7, int All);

public sealed record SkillPage(
    IReadOnlyList<SkillRecord> Items,
    int Page,
    int PageSize,
    int Total,
    string TotalRelation);

public sealed record SkillSource(
    string Provider,
    long ProjectId,
    string RepoUrl,
    string DefaultBranch,
    string SkillDir,
    string SkillPath,
    string CommitSha);

public sealed record SkillRecord(
    string Identity,
    string Slug,
    string Name,
    string Description,
    string Category,
    string ArtifactType,
    SkillSource Source,
    DateTimeOffset UpdatedAt,
    string InstallCommand,
    Downloads Downloads,
    int Likes,
    bool Active = true,
    DateTimeOffset? IndexedAt = null,
    int MissingSyncCount = 0);

public enum DownloadSource
{
    [JsonPropertyName("zip")]
    Zip,
    [JsonPropertyName("npx")]
    Npx
}

public enum SkillEventType
{
    ZipDownloaded,
    InstallCommandCopied,
    HubInstallStarted,
    WrapperCliInstalled
}

public sealed record SyncRun(
    string Id,
    string Source,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Created,
    int Updated,
    int Deactivated,
    string? Message);

public sealed record SyncSummary(int Created, int Updated, int Deactivated, DateTimeOffset IndexedAt);

public sealed record InstallInfo(string Slug, string InstallCommand, string TrackedInstallCommand, string Tracking);

public sealed record DownloadResult(string Slug, string Source, Downloads Downloads, string InstallCommand, string TrackedInstallCommand);

public sealed record LikeResult(string Slug, bool Liked, int Likes);

public sealed record SkillPackage(string FileName, byte[] Bytes);
