using CZero.Api.Auth;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CZero.Api.Controllers;

/// <summary>KPI totals per staff name — the page's kpiTotals object: {name: {jobs, ms}}.</summary>
[ApiController]
[Route("api/kpi")]
[Authorize(Roles = Roles.OpsAdmin)]
public sealed class KpiController(NpgsqlDataSource db) : ControllerBase
{
    [HttpGet]
    public async Task<Dictionary<string, KpiDto>> Get(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<KpiRow>(new CommandDefinition(
            "select name, jobs, ms from kpi_totals", cancellationToken: ct));
        return rows.ToDictionary(r => r.Name, r => new KpiDto(r.Jobs, r.Ms), StringComparer.Ordinal);
    }
}
