namespace CZero.Api.Auth;

/// <summary>Bound from the "Auth" config section (env vars Auth__JwtKey, Auth__OpsAdminPin, ...).</summary>
public sealed class AuthOptions
{
    public string JwtKey { get; set; } = "";
    public string OpsAdminPin { get; set; } = "";
    public string CoachingAdminPassword { get; set; } = "";
    public string CoachingHrPassword { get; set; } = "";

    /// <summary>The only department the HR coaching account may see.</summary>
    public string CoachingHrDepartment { get; set; } = "Operation & Technical";

    public int TokenHours { get; set; } = 12;

    public void Validate()
    {
        if (JwtKey.Length < 32)
            throw new InvalidOperationException("Auth__JwtKey must be set and at least 32 characters long.");
        if (string.IsNullOrWhiteSpace(OpsAdminPin))
            throw new InvalidOperationException("Auth__OpsAdminPin must be set.");
        if (string.IsNullOrWhiteSpace(CoachingAdminPassword))
            throw new InvalidOperationException("Auth__CoachingAdminPassword must be set.");
        if (string.IsNullOrWhiteSpace(CoachingHrPassword))
            throw new InvalidOperationException("Auth__CoachingHrPassword must be set.");
    }
}
