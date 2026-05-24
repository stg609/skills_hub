using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SkillsHub.Api.Domain;

namespace SkillsHub.Api.Persistence;

public sealed class PostgresSkillsRepository : ISkillsRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresSkillsRepository(string databaseUrl, int poolMax)
    {
        var builder = new NpgsqlDataSourceBuilder(databaseUrl);
        builder.ConnectionStringBuilder.MaxPoolSize = poolMax;
        _dataSource = builder.Build();
    }

    public async Task<IReadOnlyList<SkillRecord>> ListSkillsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            select s.payload, st.likes_count, st.downloads_1d, st.downloads_7d, st.downloads_all
            from skills s
            left join skill_stats st on st.skill_identity = s.identity
            order by s.updated_at desc, s.slug desc
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var skills = new List<SkillRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            skills.Add(ReadSkillRecord(reader));
        }

        return skills;
    }

    public async Task<SkillPage> SearchSkillsAsync(string query, string sort, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var offset = (page - 1) * pageSize;
        var hasQuery = query.Trim().Length >= 2;
        var orderBy = sort switch
        {
            "downloads_1d" => "coalesce(st.downloads_1d, 0) desc, s.slug desc",
            "downloads_7d" => "coalesce(st.downloads_7d, 0) desc, s.slug desc",
            "downloads_all" => "coalesce(st.downloads_all, 0) desc, s.slug desc",
            _ => "s.updated_at desc, s.slug desc"
        };

        var sql = $$"""
            select
              s.payload,
              coalesce(st.likes_count, 0)::int as likes_count,
              coalesce(st.downloads_1d, 0)::int as downloads_1d,
              coalesce(st.downloads_7d, 0)::int as downloads_7d,
              coalesce(st.downloads_all, 0)::int as downloads_all
            from skills s
            left join skill_stats st on st.skill_identity = s.identity
            where s.active = true
              and ($1 = false or s.search_text % $2 or s.search_text ilike '%' || $2 || '%')
            order by {{orderBy}}
            limit $3 offset $4
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(hasQuery);
        command.Parameters.AddWithValue(query.Trim());
        command.Parameters.AddWithValue(pageSize);
        command.Parameters.AddWithValue(offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var items = new List<SkillRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadSkillRecord(reader));
        }

        var total = await CountSkillsAsync(hasQuery, query.Trim(), cancellationToken);
        return new SkillPage(items, page, pageSize, total, "eq");
    }

    public async Task ReplaceSkillsAsync(IReadOnlyList<SkillRecord> skills, CancellationToken cancellationToken = default)
    {
        await UpsertSkillRowsAsync(skills, null, cancellationToken);
    }

    public async Task<SyncSummary> UpsertIndexedSkillsAsync(string syncRunId, IReadOnlyList<SkillRecord> skills, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
    {
        if (skills.Count == 0) return new SyncSummary(0, 0, 0, indexedAt);
        return await UpsertSkillRowsAsync(skills.Select(skill => skill with { Active = true, IndexedAt = indexedAt, MissingSyncCount = 0 }).ToList(), syncRunId, cancellationToken);
    }

    public async Task<SyncSummary> CompleteIndexedSyncAsync(string syncRunId, DateTimeOffset indexedAt, CancellationToken cancellationToken = default)
    {
        const int missingSyncGraceRuns = 3;
        await using var command = _dataSource.CreateCommand("""
            with missing as (
              update skills s set
                missing_sync_count = s.missing_sync_count + 1,
                active = case when s.missing_sync_count + 1 >= $2 then false else s.active end,
                payload = jsonb_set(
                  jsonb_set(s.payload, '{missingSyncCount}', to_jsonb(s.missing_sync_count + 1), true),
                  '{active}',
                  to_jsonb(case when s.missing_sync_count + 1 >= $2 then false else s.active end),
                  true
                )
              where s.active = true
                and not exists (
                  select 1 from sync_seen seen
                  where seen.sync_run_id = $1 and seen.skill_identity = s.identity
                )
              returning (missing_sync_count >= $2) as deactivated
            ),
            cleanup as (
              delete from sync_seen where sync_run_id = $1
            )
            select count(*) filter (where deactivated)::int from missing
            """);
        command.Parameters.AddWithValue(syncRunId);
        command.Parameters.AddWithValue(missingSyncGraceRuns);
        var deactivated = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        return new SyncSummary(0, 0, deactivated, indexedAt);
    }

    private async Task<SyncSummary> UpsertSkillRowsAsync(IReadOnlyList<SkillRecord> skills, string? syncRunId, CancellationToken cancellationToken)
    {
        var rows = skills.Select(skill => new
        {
            identity = skill.Identity,
            slug = skill.Slug,
            name = skill.Name,
            description = skill.Description,
            category = skill.Category,
            artifact_type = skill.ArtifactType,
            active = skill.Active,
            updated_at = skill.UpdatedAt,
            source_provider = skill.Source.Provider,
            source_project_id = skill.Source.ProjectId,
            source_repo_url = skill.Source.RepoUrl,
            source_default_branch = skill.Source.DefaultBranch,
            source_skill_dir = skill.Source.SkillDir,
            source_skill_path = skill.Source.SkillPath,
            source_commit_sha = skill.Source.CommitSha,
            missing_sync_count = skill.MissingSyncCount,
            payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(skill, JsonOptions))
        }).ToList();

        await using var command = _dataSource.CreateCommand("""
            with incoming as (
              select *
              from jsonb_to_recordset($1::jsonb) as x(
                identity text,
                slug text,
                name text,
                description text,
                category text,
                artifact_type text,
                active boolean,
                updated_at timestamptz,
                source_provider text,
                source_project_id bigint,
                source_repo_url text,
                source_default_branch text,
                source_skill_dir text,
                source_skill_path text,
                source_commit_sha text,
                missing_sync_count integer,
                payload jsonb
              )
            ),
            existing as (
              select incoming.identity, (s.identity is not null) as existed
              from incoming
              left join skills s on s.identity = incoming.identity
            ),
            normalized as (
              select
                incoming.*,
                coalesce(current_skill.slug,
                  case when slug_owner.identity is not null and slug_owner.identity <> incoming.identity
                    then incoming.slug || '-' || lower(substr(encode(sha256(convert_to(incoming.identity, 'UTF8')), 'hex'), 1, 8))
                    else incoming.slug
                  end
                ) as final_slug
              from incoming
              left join skills current_skill on current_skill.identity = incoming.identity
              left join skills slug_owner on slug_owner.slug = incoming.slug and slug_owner.identity <> incoming.identity
            ),
            upserted as (
              insert into skills (
                identity, slug, name, description, category, artifact_type, active, updated_at,
                source_provider, source_project_id, source_repo_url, source_default_branch, source_skill_dir, source_skill_path, source_commit_sha, missing_sync_count,
                payload
              )
              select
                identity, final_slug, name, description, category, artifact_type, active, updated_at,
                source_provider, source_project_id, source_repo_url, source_default_branch, source_skill_dir, source_skill_path, source_commit_sha, missing_sync_count,
                jsonb_set(payload, '{slug}', to_jsonb(final_slug), true)
              from normalized
              on conflict (identity) do update set
                slug = excluded.slug,
                name = excluded.name,
                description = excluded.description,
                category = excluded.category,
                artifact_type = excluded.artifact_type,
                active = excluded.active,
                updated_at = excluded.updated_at,
                source_provider = excluded.source_provider,
                source_project_id = excluded.source_project_id,
                source_repo_url = excluded.source_repo_url,
                source_default_branch = excluded.source_default_branch,
                source_skill_dir = excluded.source_skill_dir,
                source_skill_path = excluded.source_skill_path,
                source_commit_sha = excluded.source_commit_sha,
                missing_sync_count = excluded.missing_sync_count,
                payload = excluded.payload
              returning identity
            ),
            seen as (
              insert into sync_seen (sync_run_id, skill_identity)
              select $2, identity from upserted
              where $2 is not null
              on conflict (sync_run_id, skill_identity) do nothing
            ),
            stats as (
              insert into skill_stats (skill_identity)
              select identity from upserted
              on conflict (skill_identity) do nothing
            )
            select
              count(*) filter (where existed = false)::int as created,
              count(*) filter (where existed = true)::int as updated
            from existing
            """);
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(rows, JsonOptions), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.AddWithValue((object?)syncRunId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SyncSummary(reader.GetInt32(0), reader.GetInt32(1), 0, DateTimeOffset.UtcNow);
    }

    public async Task<SkillRecord?> FindSkillAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("""
            select s.payload, st.likes_count, st.downloads_1d, st.downloads_7d, st.downloads_all
            from skills s
            left join skill_stats st on st.skill_identity = s.identity
            where s.slug = $1 and s.active = true
            """);
        command.Parameters.AddWithValue(slug);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSkillRecord(reader) : null;
    }

    public async Task<LikeResult> SetLikeAsync(string slug, string visitorId, bool liked, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var identity = await ResolveActiveIdentityAsync(connection, transaction, slug, cancellationToken);
        if (identity is null) return new LikeResult(slug, false, 0);

        var delta = 0;
        if (liked)
        {
            await using var insert = new NpgsqlCommand("""
                insert into likes (skill_identity, visitor_id)
                values ($1, $2)
                on conflict (skill_identity, visitor_id) do nothing
                returning 1
                """, connection, transaction);
            insert.Parameters.AddWithValue(identity);
            insert.Parameters.AddWithValue(visitorId);
            delta = await insert.ExecuteScalarAsync(cancellationToken) is null ? 0 : 1;
        }
        else
        {
            await using var delete = new NpgsqlCommand("""
                delete from likes
                where skill_identity = $1 and visitor_id = $2
                returning 1
                """, connection, transaction);
            delete.Parameters.AddWithValue(identity);
            delete.Parameters.AddWithValue(visitorId);
            delta = await delete.ExecuteScalarAsync(cancellationToken) is null ? 0 : -1;
        }

        // 中文注释：点赞计数与 likes 明细必须在同一事务内更新，否则弱网重试会造成列表计数和明细表不一致。
        await using var stats = new NpgsqlCommand("""
            insert into skill_stats (skill_identity, likes_count)
            values ($1, greatest($2, 0))
            on conflict (skill_identity) do update set
              likes_count = greatest(skill_stats.likes_count + $2, 0),
              updated_at = now()
            returning likes_count
            """, connection, transaction);
        stats.Parameters.AddWithValue(identity);
        stats.Parameters.AddWithValue(delta);
        var likes = (int)(await stats.ExecuteScalarAsync(cancellationToken) ?? 0);

        await transaction.CommitAsync(cancellationToken);
        return new LikeResult(slug, liked, likes);
    }

    public async Task<Downloads> RecordSkillEventAsync(string slug, SkillEventType eventType, CancellationToken cancellationToken = default)
    {
        var source = EventSourceValue(eventType);
        var eventDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ChinaTimeZone()).DateTime);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var identity = await ResolveActiveIdentityAsync(connection, transaction, slug, cancellationToken);
        if (identity is null) return new Downloads(0, 0, 0);

        await using (var insert = new NpgsqlCommand("""
            insert into download_events (skill_identity, source, event_type)
            values ($1, $2, $3)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(identity);
            insert.Parameters.AddWithValue(source);
            insert.Parameters.AddWithValue(source);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var daily = new NpgsqlCommand("""
            insert into skill_download_daily (skill_identity, day, event_type, count)
            values ($1, $2, $3, 1)
            on conflict (skill_identity, day, event_type) do update set
              count = skill_download_daily.count + 1,
              updated_at = now()
            """, connection, transaction))
        {
            daily.Parameters.AddWithValue(identity);
            daily.Parameters.AddWithValue(eventDay);
            daily.Parameters.AddWithValue(source);
            await daily.ExecuteNonQueryAsync(cancellationToken);
        }

        // 中文注释：请求链路只原子递增总量；1d/7d 是会随时间自然过期的窗口值，由后台刷新任务统一维护。
        await using var stats = new NpgsqlCommand("""
            insert into skill_stats (skill_identity, downloads_all)
            values ($1, 1)
            on conflict (skill_identity) do update set
              downloads_all = skill_stats.downloads_all + 1,
              updated_at = now()
            returning downloads_1d, downloads_7d, downloads_all
            """, connection, transaction);
        stats.Parameters.AddWithValue(identity);
        await using var reader = await stats.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var downloads = new Downloads(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));

        await transaction.CommitAsync(cancellationToken);
        return downloads;
    }

    public async Task RefreshDownloadWindowsAsync(CancellationToken cancellationToken = default)
    {
        // 中文注释：窗口统计采用 Asia/Shanghai 自然日，避免“过去 24 小时”在榜单展示上产生用户难以理解的跳变。
        const string sql = """
            with sums as (
              select
                skill_identity,
                coalesce(sum(count) filter (where day = ((now() at time zone 'Asia/Shanghai')::date)), 0)::int as d1,
                coalesce(sum(count) filter (where day >= ((now() at time zone 'Asia/Shanghai')::date - 6)), 0)::int as d7
              from skill_download_daily
              group by skill_identity
            )
            update skill_stats st set
              downloads_1d = coalesce((select sums.d1 from sums where sums.skill_identity = st.skill_identity), 0),
              downloads_7d = coalesce((select sums.d7 from sums where sums.skill_identity = st.skill_identity), 0),
              updated_at = now()
            """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RebuildStatsAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = await AcquireLockAsync("stats-maintenance", 60 * 60 * 6, cancellationToken);
        if (ownerId is null) throw new InvalidOperationException("stats rebuild is already running");

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 中文注释：重建期间锁住派生表，避免在线下载/点赞同时写入导致 daily/stat 中间态或唯一键冲突。
            var statements = new[]
            {
                "lock table skill_download_daily, skill_stats in exclusive mode",
                "delete from skill_download_daily",
                """
                insert into skill_download_daily (skill_identity, day, event_type, count)
                select
                  skill_identity,
                  (created_at at time zone 'Asia/Shanghai')::date as day,
                  source as event_type,
                  count(*)::int
                from download_events
                group by skill_identity, (created_at at time zone 'Asia/Shanghai')::date, source
                """,
                """
                insert into skill_stats (skill_identity)
                select identity from skills
                on conflict (skill_identity) do nothing
                """,
                """
                update skill_stats set
                  likes_count = 0,
                  downloads_1d = 0,
                  downloads_7d = 0,
                  downloads_all = 0,
                  last_rebuilt_at = now(),
                  updated_at = now()
                """,
                """
                update skill_stats st set likes_count = counts.likes
                from (
                  select skill_identity, count(*)::int as likes
                  from likes
                  group by skill_identity
                ) counts
                where counts.skill_identity = st.skill_identity
                """,
                """
                update skill_stats st set downloads_all = counts.downloads_all
                from (
                  select skill_identity, count(*)::int as downloads_all
                  from download_events
                  group by skill_identity
                ) counts
                where counts.skill_identity = st.skill_identity
                """
            };

            foreach (var sql in statements)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await ReleaseLockAsync("stats-maintenance", ownerId, CancellationToken.None);
        }

        await RefreshDownloadWindowsAsync(cancellationToken);
    }

    public async Task<SyncRun> CreateSyncRunAsync(string source, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("insert into sync_runs (source, status) values ($1, 'running') returning *");
        command.Parameters.AddWithValue(source);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSyncRun(reader);
    }

    public async Task<SyncRun> FinishSyncRunAsync(string id, string status, int created, int updated, int deactivated, string? message, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("""
            update sync_runs set
              status = $2,
              finished_at = now(),
              created = $3,
              updated = $4,
              deactivated = $5,
              message = $6
            where id = $1
            returning *
            """);
        command.Parameters.AddWithValue(Guid.Parse(id));
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(created);
        command.Parameters.AddWithValue(updated);
        command.Parameters.AddWithValue(deactivated);
        command.Parameters.AddWithValue((object?)message ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSyncRun(reader);
    }

    public async Task<SyncRun?> LatestSyncRunAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("select * from sync_runs order by started_at desc limit 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSyncRun(reader) : null;
    }

    public async Task<string?> AcquireLockAsync(string name, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        var ownerId = Guid.NewGuid().ToString("N");
        await using var command = _dataSource.CreateCommand("""
            insert into sync_locks (name, owner_id, locked_until)
            values ($1, $2, now() + ($3 || ' seconds')::interval)
            on conflict (name) do update set
              owner_id = excluded.owner_id,
              locked_until = excluded.locked_until,
              updated_at = now()
            where sync_locks.locked_until < now()
            returning owner_id
            """);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(ttlSeconds);
        return await command.ExecuteScalarAsync(cancellationToken) is null ? null : ownerId;
    }

    public async Task ReleaseLockAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("delete from sync_locks where name = $1 and owner_id = $2");
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(ownerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> RenewLockAsync(string name, string ownerId, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("""
            update sync_locks
            set locked_until = now() + ($3 || ' seconds')::interval,
                updated_at = now()
            where name = $1 and owner_id = $2
            """);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(ttlSeconds);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand("select 1");
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    private async Task<int> CountSkillsAsync(bool hasQuery, string query, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand("""
            select count(*)::int
            from skills s
            where s.active = true
              and ($1 = false or s.search_text % $2 or s.search_text ilike '%' || $2 || '%')
            """);
        command.Parameters.AddWithValue(hasQuery);
        command.Parameters.AddWithValue(query);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<string?> ResolveActiveIdentityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string slug, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select identity from skills where slug = $1 and active = true", connection, transaction);
        command.Parameters.AddWithValue(slug);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static SkillRecord ReadSkillRecord(NpgsqlDataReader reader)
    {
        var payload = JsonSerializer.Deserialize<SkillRecord>(reader.GetString(0), JsonOptions)!;
        return payload with
        {
            Likes = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            Downloads = new Downloads(
                reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4))
        };
    }

    private static string EventSourceValue(SkillEventType eventType) =>
        eventType switch
        {
            SkillEventType.ZipDownloaded => "zip_downloaded",
            SkillEventType.InstallCommandCopied => "install_command_copied",
            SkillEventType.HubInstallStarted => "hub_install_started",
            SkillEventType.WrapperCliInstalled => "wrapper_cli_installed",
            _ => "unknown"
        };

    private static TimeZoneInfo ChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }

    private static SyncRun ReadSyncRun(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("id")).ToString("N"),
            reader.GetString(reader.GetOrdinal("source")),
            reader.GetString(reader.GetOrdinal("status")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at")),
            reader.IsDBNull(reader.GetOrdinal("finished_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("finished_at")),
            reader.GetInt32(reader.GetOrdinal("created")),
            reader.GetInt32(reader.GetOrdinal("updated")),
            reader.GetInt32(reader.GetOrdinal("deactivated")),
            reader.IsDBNull(reader.GetOrdinal("message")) ? null : reader.GetString(reader.GetOrdinal("message")));
}
