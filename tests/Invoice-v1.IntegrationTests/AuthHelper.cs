using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using invoice_v1.src.Domain.Enums;

namespace Invoice_v1.IntegrationTests;

public static class AuthHelper
{
    public static string GenerateTestJwt(Guid userId, UserRole role)
    {
        var claims = new List<Claim>
        {
            // We include all three fallbacks your BaseController looks for
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("id", userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        };

        // This MUST match the secret in your test appsettings.json
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-jwt-secret-key-at-least-32-characters-long-for-signing"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "invoice-v1-test",
            audience: "invoice-v1-test-users",
            claims: claims,
            expires: DateTime.Now.AddMinutes(60),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}