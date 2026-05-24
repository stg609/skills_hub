using SkillsHub.Api;
using SkillsHub.Api.Domain;
using SkillsHub.Api.GitLab;
using SkillsHub.Api.Packaging;
using SkillsHub.Api.Persistence;
using SkillsHub.Api.Sync;

var builder = WebApplication.CreateBuilder(args);
var config = AppConfig.From(builder.Configuration);

if (string.IsNullOrWhiteSpace(config.DatabaseUrl) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("DATABASE_URL is required outside Development.");
}

builder.Services.AddSingleton(config);
builder.Services.AddHttpClient<GitLabClient>();
builder.Services.AddSingleton<SkillPackageBuilder>();
builder.Services.AddSingleton<GitLabIndexer>();
builder.Services.AddSingleton<ISkillsRepository>(_ =>
    string.IsNullOrWhiteSpace(config.DatabaseUrl)
        ? new MemorySkillsRepository(DemoSkills.Create())
        : new PostgresSkillsRepository(config.DatabaseUrl, config.PgPoolMax));
builder.Services.AddSingleton<SkillsService>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddHostedService<GitLabSyncHostedService>();
builder.Services.AddHostedService<StatsRefreshHostedService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(config.WebOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await MigrationRunner.RunAsync(config.DatabaseUrl);
    return;
}

app.UseCors();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/readyz", async (ISkillsRepository repository) =>
    await repository.CheckReadyAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("database is not ready", statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapGet("/api/skills", async (SkillsService skills, string? q, string? sort, int? page, int? pageSize) =>
    Results.Ok(await skills.ListAsync(q ?? "", sort ?? "updated", page ?? 1, pageSize ?? 30)));

app.MapGet("/api/skills/{slug}", async (SkillsService skills, string slug) =>
{
    var skill = await skills.DetailAsync(slug);
    return skill is null ? Results.NotFound(new { message = "skill not found" }) : Results.Ok(skill);
});

app.MapPost("/api/skills/{slug}/like", async (SkillsService skills, HttpRequest request, string slug) =>
{
    var visitorId = request.Headers["x-skills-hub-visitor"].FirstOrDefault() ?? "anonymous";
    var result = await skills.SetLikeAsync(slug, visitorId, true);
    return result is null ? Results.NotFound(new { message = "skill not found" }) : Results.Ok(result);
});

app.MapDelete("/api/skills/{slug}/like", async (SkillsService skills, HttpRequest request, string slug) =>
{
    var visitorId = request.Headers["x-skills-hub-visitor"].FirstOrDefault() ?? "anonymous";
    var result = await skills.SetLikeAsync(slug, visitorId, false);
    return result is null ? Results.NotFound(new { message = "skill not found" }) : Results.Ok(result);
});

app.MapGet("/api/skills/{slug}/install", async (SkillsService skills, string slug) =>
{
    var info = await skills.InstallInfoAsync(slug);
    return info is null ? Results.NotFound(new { message = "skill not found" }) : Results.Ok(info);
});

app.MapPost("/api/skills/{slug}/install", async (SkillsService skills, string slug) =>
{
    var result = await skills.TrackDownloadAsync(slug, DownloadSource.Npx);
    return result is null ? Results.NotFound(new { message = "skill not found" }) : Results.Ok(result);
});

app.MapPost("/api/skills/{slug}/install/wrapper", async (SkillsService skills, string slug) =>
{
    var result = await skills.TrackWrapperInstallAsync(slug);
    return result is null ? Results.NotFound(new { message = "skill not found" }) : Results.Ok(result);
});

app.MapGet("/api/skills/{slug}/download", async (SkillsService skills, string slug) =>
{
    try
    {
        var package = await skills.DownloadPackageAsync(slug);
        return package is null
            ? Results.NotFound(new { message = "skill not found" })
            : Results.File(package.Bytes, "application/zip", package.FileName);
    }
    catch (InvalidOperationException error)
    {
        // 中文注释：仓库内容不满足打包约束属于数据质量问题，返回 422，便于调用方区分 GitLab 临时不可用。
        return Results.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (HttpRequestException error)
    {
        return Results.Problem($"GitLab is unavailable: {error.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException error)
    {
        return Results.Problem($"GitLab request timed out: {error.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/api/internal/sync/gitlab", async (SyncService sync, HttpRequest request) =>
{
    var token = request.Headers["x-internal-sync-token"].FirstOrDefault();
    var result = await sync.RunGitLabSyncAsync(token);
    return result.Status == "unauthorized" ? Results.Unauthorized() : Results.Ok(result);
});

app.MapGet("/api/internal/sync/status", async (ISkillsRepository repository, HttpRequest request) =>
{
    var token = request.Headers["x-internal-sync-token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(config.InternalSyncToken) || token != config.InternalSyncToken)
        return Results.Unauthorized();

    return Results.Ok(await repository.LatestSyncRunAsync());
});

app.MapPost("/api/internal/stats/rebuild", async (ISkillsRepository repository, HttpRequest request) =>
{
    var token = request.Headers["x-internal-sync-token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(config.InternalSyncToken) || token != config.InternalSyncToken)
        return Results.Unauthorized();

    await repository.RebuildStatsAsync();
    return Results.Ok(new { status = "completed" });
});

await app.RunAsync();
