using CZero.Api.Auth;
using CZero.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CZero.Api.Controllers;

[ApiController]
[Route("api/staff")]
[Authorize(Roles = Roles.OpsAdmin)]
public sealed class StaffController(NpgsqlDataSource db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StaffDto>> GetAll(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await LoadAsync(conn, null, ct);
    }

    /// <summary>Save the whole staff directory from the Staff tab (same as the page's Save button).</summary>
    [HttpPut]
    public async Task<IReadOnlyList<StaffDto>> ReplaceAll(List<StaffInput> input, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition("delete from staff", transaction: tx, cancellationToken: ct));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            var s = input[i];
            var id = string.IsNullOrWhiteSpace(s.Id) || !seen.Add(s.Id) ? $"s{Guid.NewGuid():N}" : s.Id;
            await conn.ExecuteAsync(new CommandDefinition(
                "insert into staff (id, sort_order, name, role) values (@id, @i, @Name, @Role)",
                new { id, i, Name = (s.Name ?? "").Trim(), Role = (s.Role ?? "").Trim() }, tx, cancellationToken: ct));
        }

        var result = await LoadAsync(conn, tx, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task<IReadOnlyList<StaffDto>> LoadAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, CancellationToken ct) =>
        (await conn.QueryAsync<StaffDto>(new CommandDefinition(
            "select id, name, role from staff order by sort_order, id", transaction: tx, cancellationToken: ct))).ToList();
}
