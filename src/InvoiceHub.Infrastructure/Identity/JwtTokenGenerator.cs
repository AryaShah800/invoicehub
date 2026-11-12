using System.IdentityModel.Tokens.Jwt;
using System.Text;
using InvoiceHub.Application.Auth;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceHub.Infrastructure.Identity;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) Generate(Guid userId, string email, Guid tenantId, string role);
}

public sealed class JwtTokenGenerator(JwtOptions options, TimeProvider clock) : IJwtTokenGenerator
{
    public (string Token, DateTime ExpiresAtUtc) Generate(Guid userId, string email, Guid tenantId, string role)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(options.ExpiryMinutes);

        var claims = new[]
        {
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Email, email),
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new System.Security.Claims.Claim(AppClaims.TenantId, tenantId.ToString()),
            new System.Security.Claims.Claim(AppClaims.Role, role),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
