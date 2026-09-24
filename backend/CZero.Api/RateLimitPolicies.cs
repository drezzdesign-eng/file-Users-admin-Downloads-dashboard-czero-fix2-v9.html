namespace CZero.Api;

public static class RateLimitPolicies
{
    /// <summary>PIN / password attempts: 10 per minute per IP.</summary>
    public const string Login = "login";

    /// <summary>Stage check-ins: 30 per minute per IP.</summary>
    public const string Checkin = "checkin";
}
