using System.Text.Json;

namespace CZero.Api.Models;

/// <summary>
/// Save from the coaching form. Info / comments / action rows replace the stored ones;
/// YearData is set for Year only, so other years of the same staff are left untouched.
/// </summary>
public sealed record CoachingSaveInput(
    string? Name,
    JsonElement Info,
    JsonElement Comments,
    JsonElement ActionRows,
    string? Year,
    JsonElement YearData);

/// <summary>
/// Records is the same {name: record} object the page exports.
/// OnlyMissing = true skips names the server already has.
/// </summary>
public sealed record CoachingImportInput(JsonElement Records, bool OnlyMissing);

public sealed record OpsJobDto(string Site, string Job, long At);

public sealed class OpsJobRow
{
    public string Site { get; set; } = "";
    public string Job { get; set; } = "";
    public long AtMs { get; set; }
}
