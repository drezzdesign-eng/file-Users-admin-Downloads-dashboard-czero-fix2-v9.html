using CZero.Api.Auth;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace CZero.Api.Controllers;

[ApiController]
[Route("api/teams/{teamId}/checkins")]
public sealed class CheckinsController(NpgsqlDataSource db) : ControllerBase
{
    /// <summary>
    /// A team presses a checkpoint. In one transaction (team row locked, so two taps at
    /// the same moment can't clash): add the time since the previous checkpoint to the
    /// KPI of whoever was on that previous checkpoint (+1 job when the new stage is
    /// "returned"), record the checkpoint, and move the team to the new stage/location.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Checkin)]
    public async Task<IActionResult> Create(string teamId, CheckinInput input, CancellationToken ct)
    {
        if (!Stages.IsValid(input.Stage)) return BadRequest(new { error = "Unknown stage." });
        var stage = input.Stage!;
        var lat = input.Lat is >= -90 and <= 90 ? input.Lat : null;
        var lng = input.Lng is >= -180 and <= 180 ? input.Lng : null;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var team = await conn.QuerySingleOrDefaultAsync<TeamRow>(new CommandDefinition(
            "select id, name, stage, leader, assistant, site, job, pin from teams where id = @teamId for update",
            new { teamId }, tx, cancellationToken: ct));
        if (team is null) return NotFound();
        if (!TokenService.SecretEquals(team.Pin, input.Pin)) return Unauthorized();

        // Repeat tap on the current checkpoint — ignore, avoids duplicate history rows.
        if (team.Stage == stage) return NoContent();

        var now = DateTime.UtcNow;
        var nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

        var prev = await conn.QuerySingleOrDefaultAsync<HistoryRow>(new CommandDefinition(
            """
            select (extract(epoch from at) * 1000)::bigint as at_ms, leader, assistant
            from checkins where team_id = @teamId
            order by at desc, id desc limit 1
            """,
            new { teamId }, tx, cancellationToken: ct));

        if (prev is not null)
        {
            var durMs = Math.Max(0, nowMs - prev.AtMs);
            var jobs = stage == Stages.Returned ? 1 : 0;
            var names = new[] { prev.Leader, prev.Assistant }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal);
            foreach (var name in names)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into kpi_totals (name, jobs, ms) values (@name, @jobs, @durMs)
                    on conflict (name) do update set jobs = kpi_totals.jobs + excluded.jobs,
                                                     ms   = kpi_totals.ms + excluded.ms
                    """,
                    new { name, jobs, durMs }, tx, cancellationToken: ct));
            }
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            insert into checkins (team_id, team_name, stage, at, leader, assistant, site, job, lat, lng)
            values (@Id, @Name, @stage, @now, @Leader, @Assistant, @Site, @Job, @lat, @lng)
            """,
            new { team.Id, team.Name, stage, now, team.Leader, team.Assistant, team.Site, team.Job, lat, lng },
            tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            update teams set stage = @stage, lat = coalesce(@lat, lat), lng = coalesce(@lng, lng), updated_at = now()
            where id = @teamId
            """,
            new { stage, lat, lng, teamId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return NoContent();
    }
}
