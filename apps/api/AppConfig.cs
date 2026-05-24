namespace SkillsHub.Api;

public sealed record AppConfig(
    string WebOrigin,
    string? DatabaseUrl,
    string GitLabBaseUrl,
    string GitLabToken,
    IReadOnlyList<string> GitLabGroups,
    bool GitLabSyncEnabled,
    int GitLabRequestTimeoutMs,
    int GitLabRequestRetries,
    int PgPoolMax,
    int MaxPackageFiles,
    long MaxPackageBytes,
    int MaxConcurrentPackageBuilds,
    int SyncLockTtlSeconds,
    string? InternalSyncToken,
    string HubPublicUrl,
    string WrapperPackageName)
{
    public static AppConfig From(IConfiguration configuration)
    {
        var section = configuration.GetSection("SkillsHub");
        return new AppConfig(
            Env("WEB_ORIGIN") ?? section.GetValue("WebOrigin", "http://localhost:5173")!,
            Env("DATABASE_URL"),
            Env("GITLAB_BASE_URL") ?? section.GetValue("GitLabBaseUrl", "https://gitlab.company.local")!,
            Env("GITLAB_TOKEN") ?? "",
            SplitList(Env("GITLAB_GROUPS")) ?? section.GetSection("GitLabGroups").Get<string[]>() ?? [],
            (Env("GITLAB_SYNC_ENABLED") ?? section.GetValue("GitLabSyncEnabled", true).ToString()) != "false",
            PositiveInt("GITLAB_REQUEST_TIMEOUT_MS", section.GetValue("GitLabRequestTimeoutMs", 10_000)),
            PositiveInt("GITLAB_REQUEST_RETRIES", section.GetValue("GitLabRequestRetries", 2)),
            PositiveInt("PG_POOL_MAX", section.GetValue("PgPoolMax", 5)),
            PositiveInt("MAX_PACKAGE_FILES", section.GetValue("MaxPackageFiles", 200)),
            PositiveLong("MAX_PACKAGE_BYTES", section.GetValue("MaxPackageBytes", 25L * 1024 * 1024)),
            PositiveInt("MAX_CONCURRENT_PACKAGE_BUILDS", section.GetValue("MaxConcurrentPackageBuilds", 2)),
            PositiveInt("SYNC_LOCK_TTL_SECONDS", section.GetValue("SyncLockTtlSeconds", 60 * 30)),
            Env("INTERNAL_SYNC_TOKEN"),
            (Env("HUB_PUBLIC_URL") ?? Env("WEB_ORIGIN") ?? section.GetValue("WebOrigin", "http://localhost:5173")!).TrimEnd('/'),
            Env("WRAPPER_PACKAGE_NAME") ?? section.GetValue("WrapperPackageName", "@company/skills-hub")!);
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name)?.Trim();

    private static IReadOnlyList<string>? SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int PositiveInt(string name, int fallback)
    {
        var raw = Env(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"{name} must be a positive integer.");
    }

    private static long PositiveLong(string name, long fallback)
    {
        var raw = Env(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return long.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"{name} must be a positive integer.");
    }
}
