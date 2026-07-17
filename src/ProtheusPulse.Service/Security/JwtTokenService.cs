using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Domain.Monitoring;
using ProtheusPulse.Service.Configuration;

namespace ProtheusPulse.Service.Security;

public interface ITokenService
{
    AuthToken Create(User user);
}

public sealed record AuthToken(string AccessToken, DateTimeOffset ExpiresAt, string Username, string DisplayName, UserRole Role);

public sealed class JwtTokenService(SecurityOptions options, IClock clock) : ITokenService
{
    public AuthToken Create(User user)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(options.TokenLifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey!)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            options.JwtIssuer,
            options.JwtAudience,
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            credentials);

        return new AuthToken(new JwtSecurityTokenHandler().WriteToken(jwt), expires, user.Username, user.DisplayName, user.Role);
    }
}
