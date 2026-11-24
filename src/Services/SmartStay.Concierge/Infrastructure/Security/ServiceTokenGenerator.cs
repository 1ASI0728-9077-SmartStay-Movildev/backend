using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SmartStay.Concierge.Infrastructure.Security;

public class ServiceTokenGenerator(IConfiguration configuration)
{
    private readonly string _secret = configuration["TokenSettings:Secret"]
        ?? Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? "EstaEsUnaClaveSecretaMuyLargaYSeguraParaSmartStay123!";

    private readonly string _issuer = configuration["TokenSettings:Issuer"]
        ?? Environment.GetEnvironmentVariable("JWT_ISSUER")
        ?? "http://localhost:5192";

    private readonly string _audience = configuration["TokenSettings:Audience"]
        ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE")
        ?? "http://localhost:5173";

    public string GenerateGuestToken(int guestUserId, string? username = null)
    {
        var key = Encoding.ASCII.GetBytes(_secret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Sid, guestUserId.ToString()),
                new Claim(ClaimTypes.Name, username ?? $"guest{guestUserId}"),
                new Claim(ClaimTypes.Role, "Guest"),
                new Claim("token_version", "1")
            }),
            Expires = DateTime.UtcNow.AddHours(2),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JsonWebTokenHandler();
        return tokenHandler.CreateToken(tokenDescriptor);
    }
}
