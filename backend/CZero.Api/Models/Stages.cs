namespace CZero.Api.Models;

/// <summary>Same pipeline as STAGE_ORDER in index.html.</summary>
public static class Stages
{
    public const string Transit = "transit";
    public const string Complete = "complete";
    public const string Returned = "returned";

    private static readonly HashSet<string> All =
        [Transit, "arrived", "survey", "installing", "testing", "troubleshoot", Complete, Returned];

    public static bool IsValid(string? stage) => stage is not null && All.Contains(stage);

    /// <summary>Old 4-value "status" field from the first version of the dashboard.</summary>
    public static string FromLegacyStatus(string? status) => status switch
    {
        "installing" => "installing",
        "done" => Complete,
        "issue" => "troubleshoot",
        _ => Transit,
    };
}
