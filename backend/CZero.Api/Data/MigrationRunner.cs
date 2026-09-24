using System.Reflection;
using Dapper;
using Npgsql;

namespace CZero.Api.Data;

/// <summary>
/// Applies the embedded Migrations/*.sql files in name order on startup, once each.
/// Running them here means a deploy can never start against an out-of-date schema.
/// </summary>
public static class MigrationRunner
{
    private const long AdvisoryLockKey = 7_231_004_118;

    public static async Task RunAsync(NpgsqlDataSource dataSource, ILogger logger, CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("select pg_advisory_lock(@AdvisoryLockKey)", new { AdvisoryLockKey }, cancellationToken: ct));
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                create table if not exists schema_migrations (
                    version    text primary key,
                    applied_at timestamptz not null default now()
                )
                """, cancellationToken: ct));

            var applied = (await conn.QueryAsync<string>(new CommandDefinition("select version from schema_migrations", cancellationToken: ct)))
                .ToHashSet(StringComparer.Ordinal);

            var assembly = Assembly.GetExecutingAssembly();
            var scripts = assembly.GetManifestResourceNames()
                .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.Ordinal);

            foreach (var resource in scripts)
            {
                var version = VersionOf(resource);
                if (applied.Contains(version)) continue;

                await using var stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Missing migration resource {resource}");
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(ct);

                await using var tx = await conn.BeginTransactionAsync(ct);
                await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition(
                    "insert into schema_migrations (version) values (@version)", new { version }, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
                logger.LogInformation("Applied migration {Version}", version);
            }
        }
        finally
        {
            await conn.ExecuteAsync(new CommandDefinition("select pg_advisory_unlock(@AdvisoryLockKey)", new { AdvisoryLockKey }, cancellationToken: ct));
        }
    }

    // "CZero.Api.Migrations.001_initial.sql" -> "001_initial"
    private static string VersionOf(string resourceName)
    {
        var withoutExt = resourceName[..^".sql".Length];
        return withoutExt[(withoutExt.LastIndexOf('.') + 1)..];
    }
}
