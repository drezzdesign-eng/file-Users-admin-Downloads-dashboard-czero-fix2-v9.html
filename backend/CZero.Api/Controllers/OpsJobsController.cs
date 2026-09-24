using CZero.Api.Auth;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CZero.Api.Controllers;

/// <summary>
/// Completed field jobs for one person, for the coaching page's "Import dari Ops Monitor"
/// button. Reads the full check-in log, so nothing is lost to the old 20-entry cap.
/// </summary>
[ApiController]
[Route("api/coaching/ops-jobs")]
[Authorize(Roles = Roles.CoachAdmin)]
public sealed class OpsJobsController(NpgsqlDataSource db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OpsJobDto>>> Get([FromQuery] string? name, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "name is required." });

        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OpsJobRow>(new CommandDefinition(
            """
            select site, job, (extract(epoch from at) * 1000)::bigint as at_ms
            from checkins
            where stage = @complete
              and (lower(leader) = lower(@name) or lower(assistant) = lower(@name))
            order by at, id
            """,
            new { name, complete = Stages.Complete }, cancellationToken: ct));
        return Ok(rows.Select(r => new OpsJobDto(r.Site, r.Job, r.AtMs)).ToList());
    }
}
