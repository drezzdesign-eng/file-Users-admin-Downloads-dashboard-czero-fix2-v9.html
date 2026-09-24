using CZero.Api.Data;
using CZero.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CZero.Api.Controllers;

/// <summary>Public live board (the TV screen). No PINs are ever included.</summary>
[ApiController]
[Route("api/board")]
public sealed class BoardController(NpgsqlDataSource db) : ControllerBase
{
    [HttpGet]
    public async Task<BoardDto> Get(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return new BoardDto(await TeamQueries.LoadAsync(conn, includePin: false, ct));
    }
}
