using System.Data;
using CZero.Api.Models;
using Dapper;

namespace CZero.Api.Data;

public static class TeamQueries
{
    /// <summary>How many recent checkpoints are sent to the board per team (the page's old cap).</summary>
    public const int HistoryPerTeam = 20;

    public const string TeamColumns =
        "id, sort_order, name, lat, lng, site, job, stage, leader, assistant, vehicle_plate, vehicle_model, eta_arrival, eta_complete, eta_return, pin";

    public static async Task<IReadOnlyList<TeamDto>> LoadAsync(IDbConnection conn, bool includePin, CancellationToken ct, IDbTransaction? tx = null)
    {
        var teams = (await conn.QueryAsync<TeamRow>(new CommandDefinition(
            $"select {TeamColumns} from teams order by sort_order, id", transaction: tx, cancellationToken: ct))).ToList();
        if (teams.Count == 0) return [];

        var history = await conn.QueryAsync<HistoryRow>(new CommandDefinition(
            """
            select team_id, stage, at_ms, leader, assistant, site, job
            from (
                select c.team_id, c.stage, c.leader, c.assistant, c.site, c.job, c.at, c.id,
                       (extract(epoch from c.at) * 1000)::bigint as at_ms,
                       row_number() over (partition by c.team_id order by c.at desc, c.id desc) as rn
                from checkins c
                where c.team_id = any(@ids)
            ) x
            where rn <= @limit
            order by team_id, at, id
            """,
            new { ids = teams.Select(t => t.Id).ToArray(), limit = HistoryPerTeam }, tx, cancellationToken: ct));

        var historyByTeam = history
            .GroupBy(h => h.TeamId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<HistoryEntryDto>)g.Select(h => new HistoryEntryDto(h.Stage, h.AtMs, h.Leader, h.Assistant, h.Site, h.Job)).ToList());

        return teams.Select(t => new TeamDto(
            t.Id, t.Name, t.Lat, t.Lng, t.Site, t.Job, t.Stage, t.Leader, t.Assistant,
            t.VehiclePlate, t.VehicleModel, t.EtaArrival, t.EtaComplete, t.EtaReturn,
            includePin ? t.Pin : null,
            historyByTeam.GetValueOrDefault(t.Id, []))).ToList();
    }
}
