namespace CZero.Api.Models;

public sealed record OpsAdminLoginInput(string? Pin);

public sealed record CoachingLoginInput(string? Role, string? Password);

public sealed record TokenDto(string Token, string Role);
