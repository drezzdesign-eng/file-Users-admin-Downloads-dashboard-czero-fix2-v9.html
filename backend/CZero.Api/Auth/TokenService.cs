using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CZero.Api.Auth;

public sealed class TokenService(AuthOptions options)
{
    public const string Issuer = "czero-dashboard-api";
    public const string RoleClaim = "role";

    private readonly JsonWebTokenHandler _handler = new();

    public static SymmetricSecurityKey SigningKey(AuthOptions options) =>
        new(Encoding.UTF8.GetBytes(options.JwtKey));

    public string Issue(string role)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Subject = new ClaimsIdentity([new Claim(RoleClaim, role)]),
            Expires = DateTime.UtcNow.AddHours(options.TokenHours),
            SigningCredentials = new SigningCredentials(SigningKey(options), SecurityAlgorithms.HmacSha256),
        };
        return _handler.CreateToken(descriptor);
    }

    /// <summary>Constant-time compare so PINs/passwords can't be guessed from response timing.</summary>
    public static bool SecretEquals(string? expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || given is null) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
