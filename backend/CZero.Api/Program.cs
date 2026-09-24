using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CZero.Api;
using CZero.Api.Auth;
using CZero.Api.Data;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;

var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
authOptions.Validate();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<TokenService>();

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? throw new InvalidOperationException("ConnectionStrings__Db must be set.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TokenService.Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TokenService.SigningKey(authOptions),
            RoleClaimType = TokenService.RoleClaim,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Login, ctx => PerIp(ctx, 10));
    o.AddPolicy(RateLimitPolicies.Checkin, ctx => PerIp(ctx, 30));
});

// Only needed when the pages are opened from a different origin (local testing).
// On the live site nginx serves /api on the same domain, so CORS isn't involved.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // nginx on the host is the only thing that can reach the API port (bound to 127.0.0.1),
    // so trust its X-Forwarded-For for the real client IP used by rate limiting.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

await MigrationRunner.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Logger);

app.UseForwardedHeaders();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

static RateLimitPartition<string> PerIp(HttpContext ctx, int permitsPerMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) });
