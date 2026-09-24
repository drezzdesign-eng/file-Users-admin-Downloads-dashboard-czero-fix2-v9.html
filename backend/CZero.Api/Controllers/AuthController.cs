using CZero.Api.Auth;
using CZero.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CZero.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Login)]
public sealed class AuthController(AuthOptions options, TokenService tokens) : ControllerBase
{
    /// <summary>Ops Monitor ⚙ Settings PIN.</summary>
    [HttpPost("ops-admin")]
    public ActionResult<TokenDto> OpsAdmin(OpsAdminLoginInput input)
    {
        if (!TokenService.SecretEquals(options.OpsAdminPin, input.Pin)) return Unauthorized();
        return new TokenDto(tokens.Issue(Roles.OpsAdmin), Roles.OpsAdmin);
    }

    /// <summary>Coaching page login. Role is "admin" or "hr", same values as the page's dropdown.</summary>
    [HttpPost("coaching")]
    public ActionResult<TokenDto> Coaching(CoachingLoginInput input)
    {
        var (expected, role) = input.Role switch
        {
            "admin" => (options.CoachingAdminPassword, Roles.CoachAdmin),
            "hr" => (options.CoachingHrPassword, Roles.CoachHr),
            _ => ("", ""),
        };
        if (!TokenService.SecretEquals(expected, input.Password)) return Unauthorized();
        return new TokenDto(tokens.Issue(role), input.Role!);
    }
}
