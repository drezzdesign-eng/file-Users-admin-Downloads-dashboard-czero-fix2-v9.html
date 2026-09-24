using System.Globalization;
using System.Text.Json;
using CZero.Api.Auth;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CZero.Api.Controllers;

/// <summary>
/// One-time move of the old JSONBin data into the database. Accepts the JSONBin
/// "latest" response as-is ({record: ...}) or just the record. Refuses to run when
/// data already exists unless ?overwrite=true.
/// </summary>
[ApiController]
[Route("api/admin/import-jsonbin")]
[Authorize(Roles = Roles.OpsAdmin)]
public sealed class ImportController(NpgsqlDataSource db) : ControllerBase
{
    private sealed record LegacyCheckin(string Stage, long At, string Leader, string Assistant, string Site, string Job);

    [HttpPost]
    public async Task<IActionResult> Import([FromBody] JsonElement body, [FromQuery] bool overwrite, CancellationToken ct)
    {
        var record = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("record", out var r) ? r : body;

        JsonElement teamsEl, staffEl = default, kpiEl = default;
        if (record.ValueKind == JsonValueKind.Array)
        {
            teamsEl = record; // oldest format: a plain array of teams
        }
        else if (record.ValueKind == JsonValueKind.Object)
        {
            record.TryGetProperty("teams", out teamsEl);
            record.TryGetProperty("staff", out staffEl);
            record.TryGetProperty("kpiTotals", out kpiEl);
        }
        else
        {
            return BadRequest(new { error = "Not a JSONBin record." });
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "select (select count(*) from teams) + (select count(*) from checkins) + (select count(*) from staff)",
            transaction: tx, cancellationToken: ct));
        if (existing > 0 && !overwrite)
            return Conflict(new { error = "Database already has data. Re-run with ?overwrite=true to replace it." });

        await conn.ExecuteAsync(new CommandDefinition(
            "truncate teams, checkins, staff, kpi_totals", transaction: tx, cancellationToken: ct));

        var teamCount = 0;
        var checkinCount = 0;
        var backfill = new Dictionary<string, (int Jobs, long Ms)>(StringComparer.Ordinal);

        foreach (var t in Items(teamsEl))
        {
            var id = Str(t, "id");
            if (id.Length == 0) id = $"t{Guid.NewGuid():N}";
            var leader = Str(t, "leader");
            var assistant = Str(t, "assistant");
            var site = Str(t, "site");
            var job = Str(t, "job");
            var stage = Str(t, "stage");
            if (!Stages.IsValid(stage)) stage = Stages.FromLegacyStatus(Str(t, "status"));
            var pin = Str(t, "pin");

            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into teams (id, sort_order, name, lat, lng, site, job, stage, leader, assistant,
                                   vehicle_plate, vehicle_model, eta_arrival, eta_complete, eta_return, pin)
                values (@id, @sortOrder, @name, @lat, @lng, @site, @job, @stage, @leader, @assistant,
                        @vehiclePlate, @vehicleModel, @etaArrival, @etaComplete, @etaReturn, @pin)
                on conflict (id) do nothing
                """,
                new
                {
                    id,
                    sortOrder = teamCount,
                    name = Str(t, "name"),
                    lat = Num(t, "lat"),
                    lng = Num(t, "lng"),
                    site,
                    job,
                    stage,
                    leader,
                    assistant,
                    vehiclePlate = Str(t, "vehiclePlate"),
                    vehicleModel = Str(t, "vehicleModel"),
                    etaArrival = Str(t, "etaArrival"),
                    etaComplete = Str(t, "etaComplete"),
                    etaReturn = Str(t, "etaReturn"),
                    pin = pin.Length == 0 ? "0000" : pin,
                },
                tx, cancellationToken: ct));
            teamCount++;

            // Older checkpoints may lack leader/assistant/site/job snapshots — the page
            // fell back to the team's current values for those, so do the same here.
            var history = Items(t.TryGetProperty("history", out var h) ? h : default)
                .Select(e => new LegacyCheckin(
                    Str(e, "stage"),
                    (long)(Num(e, "at") ?? 0),
                    Has(e, "leader") ? Str(e, "leader") : leader,
                    Has(e, "assistant") ? Str(e, "assistant") : assistant,
                    Has(e, "site") ? Str(e, "site") : site,
                    Has(e, "job") ? Str(e, "job") : job))
                .Where(e => Stages.IsValid(e.Stage) && e.At > 0)
                .ToList();

            for (var i = 0; i < history.Count; i++)
            {
                var e = history[i];
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    insert into checkins (team_id, team_name, stage, at, leader, assistant, site, job)
                    values (@id, @teamName, @Stage, @at, @Leader, @Assistant, @Site, @Job)
                    """,
                    new { id, teamName = Str(t, "name"), e.Stage, at = DateTimeOffset.FromUnixTimeMilliseconds(e.At).UtcDateTime, e.Leader, e.Assistant, e.Site, e.Job },
                    tx, cancellationToken: ct));
                checkinCount++;

                // Same as the page's old backfillKpiFromHistory, used only if kpiTotals is missing.
                var names = new[] { e.Leader, e.Assistant }.Where(n => n.Length > 0).ToList();
                if (i + 1 < history.Count)
                {
                    var dur = history[i + 1].At - e.At;
                    foreach (var n in names) backfill[n] = (backfill.GetValueOrDefault(n).Jobs, backfill.GetValueOrDefault(n).Ms + dur);
                }
                if (e.Stage == Stages.Returned)
                {
                    foreach (var n in names) backfill[n] = (backfill.GetValueOrDefault(n).Jobs + 1, backfill.GetValueOrDefault(n).Ms);
                }
            }
        }

        var staffCount = 0;
        foreach (var s in Items(staffEl))
        {
            var id = Str(s, "id");
            await conn.ExecuteAsync(new CommandDefinition(
                "insert into staff (id, sort_order, name, role) values (@id, @staffCount, @name, @role) on conflict (id) do nothing",
                new { id = id.Length == 0 ? $"s{Guid.NewGuid():N}" : id, staffCount, name = Str(s, "name"), role = Str(s, "role") },
                tx, cancellationToken: ct));
            staffCount++;
        }

        var kpi = new Dictionary<string, (int Jobs, long Ms)>(StringComparer.Ordinal);
        if (kpiEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in kpiEl.EnumerateObject())
                kpi[p.Name] = ((int)(Num(p.Value, "jobs") ?? 0), (long)(Num(p.Value, "ms") ?? 0));
        }
        else
        {
            kpi = backfill;
        }
        foreach (var (name, v) in kpi)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "insert into kpi_totals (name, jobs, ms) values (@name, @Jobs, @Ms)",
                new { name, v.Jobs, v.Ms }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return Ok(new { teams = teamCount, checkins = checkinCount, staff = staffCount, kpi = kpi.Count, kpiBackfilled = kpiEl.ValueKind != JsonValueKind.Object });
    }

    private static IEnumerable<JsonElement> Items(JsonElement el) =>
        el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object) : [];

    private static bool Has(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Undefined;

    // Values edited by hand in JSONBin may be numbers where strings are expected (e.g. pin: 1234).
    private static string Str(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    private static double? Num(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
