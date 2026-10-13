using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RealEstate.Domain;

namespace RealEstate.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "RealEstateApi";
    public string Audience { get; set; } = "RealEstateClients";
    public int ExpiresMinutes { get; set; } = 720;
    public string Key { get; set; } = "";   // >= 32 caracteres; en Render se genera sola (render.yaml)
}

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public (string Token, DateTimeOffset ExpiresAt) Create(AppUser user, IEnumerable<string> roles)
    {
        var o = options.Value;
        var expires = DateTimeOffset.UtcNow.AddMinutes(o.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email ?? ""),
            new("name", user.FullName)
        };

        // El tenant viaja DENTRO del token firmado: es la única fuente de verdad para el aislamiento
        if (user.TenantId is { } tenantId)
            claims.Add(new Claim("tenant_id", tenantId.ToString()));

        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)),
                SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
