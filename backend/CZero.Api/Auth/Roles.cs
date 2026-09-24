namespace CZero.Api.Auth;

public static class Roles
{
    /// <summary>Ops Monitor ⚙ Settings (admin PIN).</summary>
    public const string OpsAdmin = "ops-admin";

    /// <summary>Coaching page, full access.</summary>
    public const string CoachAdmin = "coach-admin";

    /// <summary>Coaching page, view &amp; print only, one department.</summary>
    public const string CoachHr = "coach-hr";

    public const string AnyCoach = CoachAdmin + "," + CoachHr;
}
