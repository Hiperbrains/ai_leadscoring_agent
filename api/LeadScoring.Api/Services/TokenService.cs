using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace LeadScoring.Api.Services;

public class TokenService(IConfiguration configuration)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(configuration["Tracking:SigningKey"] ?? "change-this-dev-signing-key-32-chars");
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public string CreateLeadToken(Guid leadId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("lead_id", leadId.ToString("D", CultureInfo.InvariantCulture))]),
            Expires = DateTime.UtcNow.AddDays(30),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256)
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return _tokenHandler.WriteToken(token);
    }

    public Guid? ValidateLeadToken(string token)
    {
        try
        {
            var principal = _tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_key),
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var claim = principal.FindFirst("lead_id")?.Value;
            return Guid.TryParse(claim, out var leadId) ? leadId : null;
        }
        catch
        {
            return null;
        }
    }
}
