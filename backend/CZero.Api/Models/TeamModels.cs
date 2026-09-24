namespace CZero.Api.Models;

public sealed class TeamRow
{
    public string Id { get; set; } = "";
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public string Site { get; set; } = "";
    public string Job { get; set; } = "";
    public string Stage { get; set; } = Stages.Transit;
    public string Leader { get; set; } = "";
    public string Assistant { get; set; } = "";
    public string VehiclePlate { get; set; } = "";
    public string VehicleModel { get; set; } = "";
    public string EtaArrival { get; set; } = "";
    public string EtaComplete { get; set; } = "";
    public string EtaReturn { get; set; } = "";
    public string Pin { get; set; } = "";
}

public sealed class HistoryRow
{
    public string TeamId { get; set; } = "";
    public string Stage { get; set; } = "";
    public long AtMs { get; set; }
    public string Leader { get; set; } = "";
    public string Assistant { get; set; } = "";
    public string Site { get; set; } = "";
    public string Job { get; set; } = "";
}

/// <summary>One checkpoint, same shape as team.history[] in index.html (at = epoch ms).</summary>
public sealed record HistoryEntryDto(string Stage, long At, string Leader, string Assistant, string Site, string Job);

/// <summary>A team as index.html expects it. Pin is only filled for the admin.</summary>
public sealed record TeamDto(
    string Id,
    string Name,
    double? Lat,
    double? Lng,
    string Site,
    string Job,
    string Stage,
    string Leader,
    string Assistant,
    string VehiclePlate,
    string VehicleModel,
    string EtaArrival,
    string EtaComplete,
    string EtaReturn,
    string? Pin,
    IReadOnlyList<HistoryEntryDto> History);

public sealed record BoardDto(IReadOnlyList<TeamDto> Teams);

/// <summary>
/// One team from the admin editor. Stage / Lat / Lng are null when the admin didn't
/// change them, so a check-in made while the editor was open isn't overwritten.
/// </summary>
public sealed record TeamInput(
    string Id,
    string? Name,
    double? Lat,
    double? Lng,
    string? Site,
    string? Job,
    string? Stage,
    string? Leader,
    string? Assistant,
    string? VehiclePlate,
    string? VehicleModel,
    string? EtaArrival,
    string? EtaComplete,
    string? EtaReturn,
    string? Pin);

public sealed record PinInput(string? Pin);

public sealed record CheckinInput(string? Pin, string? Stage, double? Lat, double? Lng);
