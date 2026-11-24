using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Fixtures;

/// <summary>
/// Fixture providing authenticated JWT tokens through IAM sign-in and synthetic JWT generation for boundary tests.
/// </summary>
public class AuthTokenFixture
{
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);

    public record SignInResponse(int Id, string Username, string Token, string Role, int? HotelId, int? ChainId);

    /// <summary>
    /// Acquires a JWT token via sign-in endpoint with thread-safe caching.
    /// </summary>
    public async Task<string> GetTokenAsync(HttpClient client, string username, string? password = null)
    {
        if (_tokens.TryGetValue(username, out var cachedToken))
        {
            return cachedToken;
        }

        var effectivePassword = password ?? E2ETestConfig.DefaultPassword;
        var requestUrl = "/api/v1/authentication/sign-in";

        var response = await client.PostAsJsonAsync(requestUrl, new
        {
            username,
            password = effectivePassword
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SignInResponse>();
        if (body is null || string.IsNullOrWhiteSpace(body.Token))
        {
            throw new InvalidOperationException($"Sign-in succeeded for '{username}', but returned empty token.");
        }

        _tokens[username] = body.Token;
        return body.Token;
    }

    public Task<string> GetGuest1TokenAsync(HttpClient client) => GetTokenAsync(client, "guest1");

    public Task<string> GetGuest2TokenAsync(HttpClient client) => GetTokenAsync(client, "guest2");

    public Task<string> GetAdminTokenAsync(HttpClient client) => GetTokenAsync(client, "admin");

    /// <summary>
    /// Signs up a new user dynamically and returns an authenticated JWT token.
    /// </summary>
    public async Task<string> RegisterAndGetTokenAsync(HttpClient client, string username, string password, string? role = null)
    {
        var signUpUrl = "/api/v1/authentication/sign-up";
        var signUpBody = role != null 
            ? (object)new { username, password, role } 
            : new { username, password };

        var signUpResp = await client.PostAsJsonAsync(signUpUrl, signUpBody);
        signUpResp.EnsureSuccessStatusCode();

        try
        {
            await using var conn = new MySqlConnector.MySqlConnection(E2ETestConfig.MySqlConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE users SET token_version = 1 WHERE username = @u";
            cmd.Parameters.AddWithValue("@u", username);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort alignment
        }

        return await GetTokenAsync(client, username, password);
    }

    /// <summary>
    /// Generates a synthetic JWT token for security, tampering, boundary, and expiry testing.
    /// </summary>
    public string CreateSyntheticJwt(
        int userId,
        string username,
        string role,
        int tokenVersion = 1,
        TimeSpan? lifetime = null,
        string? customSecret = null,
        string? customIssuer = null,
        string? customAudience = null)
    {
        var secret = customSecret ?? E2ETestConfig.JwtSecret;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("token_version", tokenVersion.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: customIssuer ?? E2ETestConfig.JwtIssuer,
            audience: customAudience ?? E2ETestConfig.JwtAudience,
            claims: claims,
            expires: expiry,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void InvalidateCache(string username)
    {
        _tokens.TryRemove(username, out _);
    }

    public void ClearCache()
    {
        _tokens.Clear();
    }
}
