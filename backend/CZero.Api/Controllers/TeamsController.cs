using CZero.Api.Auth;
using CZero.Api.Data;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace CZero.Api.Controllers;

[ApiController]
[Route("api/teams")]
public sealed class TeamsController(NpgsqlDataSource db) : ControllerBase
{
    /// <summary>All teams including PINs, for the ⚙ Settings editor.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.OpsAdmin)]
    public async Task<IReadOnlyList<TeamDto>> GetAll(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await TeamQueries.LoadAsync(conn, includePin: true, ct);
    }

    /// <summary>
    /// Save the whole team list from the editor: teams in the list are added/updated (in that
    /// order), teams missing from it are removed. Their check-in history and KPI are kept.
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Roles.OpsAdmin)]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> ReplaceAll(List<TeamInput> input, CancellationToken ct)
    {
        if (input.Any(t => string.IsNullOrWhiteSpace(t.Id)))
            return BadRequest(new { error = "Every team needs an id." });
        if (input.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != input.Count)
            return BadRequest(new { error = "Team ids must be unique." });
        if (input.Any(t => t.Stage is not null && !Stages.IsValid(t.Stage)))
            return BadRequest(new { error = "Unknown stage." });

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var ids = input.Select(t => t.Id).ToArray();
        await conn.ExecuteAsync(new CommandDefinition("delete from teams where not (id = any(@ids))", new { ids }, tx, cancellationToken: ct));

        for (var i = 0; i < input.Count; i++)
        {
            var t = input[i];
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into teams (id, sort_order, name, lat, lng, site, job, stage, leader, assistant,
                                   vehicle_plate, vehicle_model, eta_arrival, eta_complete, eta_return, pin, updated_at)
                values (@Id, @SortOrder, @Name, @Lat, @Lng, @Site, @Job, coalesce(@Stage, 'transit'), @Leader, @Assistant,
                        @VehiclePlate, @VehicleModel, @EtaArrival, @EtaComplete, @EtaReturn, @Pin, now())
                on conflict (id) do update set
                    sort_order    = excluded.sort_order,
                    name          = excluded.name,
                    lat           = coalesce(@Lat, teams.lat),
                    lng           = coalesce(@Lng, teams.lng),
                    site          = excluded.site,
                    job           = excluded.job,
                    stage         = coalesce(@Stage, teams.stage),
                    leader        = excluded.leader,
                    assistant     = excluded.assistant,
                    vehicle_plate = excluded.vehicle_plate,
                    vehicle_model = excluded.vehicle_model,
                    eta_arrival   = excluded.eta_arrival,
                    eta_complete  = excluded.eta_complete,
                    eta_return    = excluded.eta_return,
                    pin           = excluded.pin,
                    updated_at    = now()
                """,
                new
                {
                    t.Id,
                    SortOrder = i,
                    Name = t.Name ?? "",
                    Lat = ValidLat(t.Lat),
                    Lng = ValidLng(t.Lng),
                    Site = t.Site ?? "",
                    Job = t.Job ?? "",
                    t.Stage,
                    Leader = (t.Leader ?? "").Trim(),
                    Assistant = (t.Assistant ?? "").Trim(),
                    VehiclePlate = t.VehiclePlate ?? "",
                    VehicleModel = t.VehicleModel ?? "",
                    EtaArrival = t.EtaArrival ?? "",
                    EtaComplete = t.EtaComplete ?? "",
                    EtaReturn = t.EtaReturn ?? "",
                    Pin = string.IsNullOrWhiteSpace(t.Pin) ? "0000" : t.Pin.Trim(),
                },
                tx, cancellationToken: ct));
        }

        var result = await TeamQueries.LoadAsync(conn, includePin: true, ct, tx);
        await tx.CommitAsync(ct);
        return Ok(result);
    }

    /// <summary>Checks a team's PIN before the check-in screen unlocks. 204 = correct, 401 = wrong.</summary>
    [HttpPost("{id}/pin-check")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> PinCheck(string id, PinInput input, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var pin = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select pin from teams where id = @id", new { id }, cancellationToken: ct));
        if (pin is null) return NotFound();
        return TokenService.SecretEquals(pin, input.Pin) ? NoContent() : Unauthorized();
    }

    private static double? ValidLat(double? v) => v is >= -90 and <= 90 ? v : null;
    private static double? ValidLng(double? v) => v is >= -180 and <= 180 ? v : null;
}
