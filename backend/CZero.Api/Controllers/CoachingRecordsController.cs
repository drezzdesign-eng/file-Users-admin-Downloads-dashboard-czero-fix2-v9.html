using System.Text.Json;
using CZero.Api.Auth;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CZero.Api.Controllers;

/// <summary>
/// Coaching records in the same {name: {info, comments, actionRows, years}} shape the
/// coaching page has always used (it was in localStorage before).
/// </summary>
[ApiController]
[Route("api/coaching/records")]
public sealed class CoachingRecordsController(NpgsqlDataSource db, AuthOptions options) : ControllerBase
{
    private const string RecordJson =
        "jsonb_build_object('info', info, 'comments', comments, 'actionRows', action_rows, 'years', years)";

    /// <summary>All records. The HR account only gets its own department.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.AnyCoach)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var dept = User.IsInRole(Roles.CoachHr) ? options.CoachingHrDepartment : null;
        await using var conn = await db.OpenConnectionAsync(ct);
        return JsonContent(await LoadAllAsync(conn, dept, null, ct));
    }

    /// <summary>Save one staff member's form for one year. Returns the full stored record.</summary>
    [HttpPut]
    [Authorize(Roles = Roles.CoachAdmin)]
    public async Task<IActionResult> Save(CoachingSaveInput input, CancellationToken ct)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "Staff name is required." });
        var year = input.Year?.Trim();
        if (string.IsNullOrEmpty(year)) return BadRequest(new { error = "Year is required." });

        await using var conn = await db.OpenConnectionAsync(ct);
        var saved = await conn.QuerySingleAsync<string>(new CommandDefinition(
            $"""
            insert into coaching_records (name, info, comments, action_rows, years, updated_at)
            values (@name, @info::jsonb, @comments::jsonb, @actionRows::jsonb,
                    jsonb_build_object(@year::text, @yearData::jsonb), now())
            on conflict (name) do update set
                info        = excluded.info,
                comments    = excluded.comments,
                action_rows = excluded.action_rows,
                years       = coaching_records.years || excluded.years,
                updated_at  = now()
            returning {RecordJson}::text
            """,
            new
            {
                name,
                info = RawOr(input.Info, "{}"),
                comments = RawOr(input.Comments, "{}"),
                actionRows = RawOr(input.ActionRows, "[]"),
                year,
                yearData = RawOr(input.YearData, "{}"),
            },
            cancellationToken: ct));
        return JsonContent(saved);
    }

    [HttpDelete]
    [Authorize(Roles = Roles.CoachAdmin)]
    public async Task<IActionResult> Delete([FromQuery] string name, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var deleted = await conn.ExecuteAsync(new CommandDefinition(
            "delete from coaching_records where name = @name", new { name }, cancellationToken: ct));
        return deleted == 0 ? NotFound() : NoContent();
    }

    /// <summary>Import an exported JSON file (the page's Import button). Returns all records.</summary>
    [HttpPost("import")]
    [Authorize(Roles = Roles.CoachAdmin)]
    public async Task<IActionResult> Import(CoachingImportInput input, CancellationToken ct)
    {
        if (input.Records.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "Records must be an object of {name: record}." });

        var onConflict = input.OnlyMissing
            ? "do nothing"
            : """
              do update set info = excluded.info, comments = excluded.comments,
                            action_rows = excluded.action_rows, years = excluded.years, updated_at = now()
              """;

        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var entry in input.Records.EnumerateObject())
        {
            var name = entry.Name.Trim();
            if (name.Length == 0 || entry.Value.ValueKind != JsonValueKind.Object) continue;
            await conn.ExecuteAsync(new CommandDefinition(
                $"""
                insert into coaching_records (name, info, comments, action_rows, years, updated_at)
                select @name,
                       coalesce(r -> 'info', '{"{}"}'::jsonb),
                       coalesce(r -> 'comments', '{"{}"}'::jsonb),
                       coalesce(r -> 'actionRows', '[]'::jsonb),
                       coalesce(r -> 'years', '{"{}"}'::jsonb),
                       now()
                from (select @record::jsonb as r) src
                on conflict (name) {onConflict}
                """,
                new { name, record = entry.Value.GetRawText() }, tx, cancellationToken: ct));
        }

        var all = await LoadAllAsync(conn, null, tx, ct);
        await tx.CommitAsync(ct);
        return JsonContent(all);
    }

    private static async Task<string> LoadAllAsync(NpgsqlConnection conn, string? dept, NpgsqlTransaction? tx, CancellationToken ct) =>
        await conn.QuerySingleAsync<string>(new CommandDefinition(
            $"""
            select coalesce(jsonb_object_agg(name, {RecordJson}), '{"{}"}'::jsonb)::text
            from coaching_records
            where cast(@dept as text) is null
               or lower(trim(coalesce(info ->> 'dept', ''))) = lower(trim(cast(@dept as text)))
            """,
            new { dept }, tx, cancellationToken: ct));

    private static string RawOr(JsonElement el, string fallback) =>
        el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? fallback : el.GetRawText();

    private ContentResult JsonContent(string json) => Content(json, "application/json");
}
