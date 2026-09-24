namespace CZero.Api.Models;

public sealed record StaffDto(string Id, string Name, string Role);

public sealed record StaffInput(string? Id, string? Name, string? Role);

public sealed record KpiDto(int Jobs, long Ms);

public sealed class KpiRow
{
    public string Name { get; set; } = "";
    public int Jobs { get; set; }
    public long Ms { get; set; }
}
