using Npgsql;

namespace SkillsHub.Api.Persistence;

public static class MigrationRunner
{
    public static async Task RunAsync(string? databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("DATABASE_URL is required to run migrations.");

        await using var dataSource = NpgsqlDataSource.Create(databaseUrl);
        await using var connection = await dataSource.OpenConnectionAsync();

        await using (var lockCommand = new NpgsqlCommand("select pg_advisory_lock(hashtext('skills_hub_migrations'))", connection))
        {
            await lockCommand.ExecuteNonQueryAsync();
        }

        try
        {
            var migrationDir = ResolveMigrationDir();

            foreach (var migrationPath in Directory.GetFiles(migrationDir, "*.sql").OrderBy(path => path, StringComparer.Ordinal))
            {
                var version = Path.GetFileNameWithoutExtension(migrationPath);
                if (await HasMigrationAsync(connection, version)) continue;

                var sql = await File.ReadAllTextAsync(migrationPath);
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    await using var command = new NpgsqlCommand(sql, connection, transaction);
                    await command.ExecuteNonQueryAsync();
                    await using var mark = new NpgsqlCommand(
                        "insert into schema_migrations (version) values ($1) on conflict (version) do nothing",
                        connection,
                        transaction);
                    mark.Parameters.AddWithValue(version);
                    await mark.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand("select pg_advisory_unlock(hashtext('skills_hub_migrations'))", connection);
            await unlockCommand.ExecuteNonQueryAsync();
        }
    }

    private static string ResolveMigrationDir()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "migrations"),
            Path.Combine(currentDirectory, "migrations"),
            Path.Combine(currentDirectory, "apps", "api", "migrations"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "migrations")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath)) return fullPath;
        }

        // 中文注释：本地 dotnet run、发布后的容器入口、以及从 apps/api 目录直接运行时工作目录不同；找不到目录时把候选路径打出来方便部署排查。
        throw new DirectoryNotFoundException(
            "Could not find migrations directory. Checked: " + string.Join(", ", candidates.Select(Path.GetFullPath)));
    }

    private static async Task<bool> HasMigrationAsync(NpgsqlConnection connection, string version)
    {
        await using var exists = new NpgsqlCommand("select to_regclass('public.schema_migrations') is not null", connection);
        if (await exists.ExecuteScalarAsync() is not true) return false;

        await using var command = new NpgsqlCommand("select exists(select 1 from schema_migrations where version = $1)", connection);
        command.Parameters.AddWithValue(version);
        return await command.ExecuteScalarAsync() is true;
    }
}
