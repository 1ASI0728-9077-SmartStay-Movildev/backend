using SmartStay.IAM.Application.OutboundServices;
using SmartStay.IAM.Domain.Model.Enums;
using SmartStay.IAM.Domain.Model.Queries;
using SmartStay.IAM.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using SmartStay.Contracts.Http;

namespace SmartStay.IAM.Interfaces.REST;

/// <summary>
/// Endpoint interno para validación de tokens e identidad entre microservicios e IAM.
/// Protegido a nivel perimetral para evitar acceso público desde el Gateway Nginx.
/// </summary>
[ApiController]
[Route("internal/v1/identity")]
[AllowAnonymous]
public class InternalIdentityController(
    ITokenService tokenService,
    IUserQueryService userQueryService,
    ILogger<InternalIdentityController> logger) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateToken([FromBody] ValidateIdentityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Token))
        {
            return Ok(new ValidateIdentityResponse(false, null, null, null, null, null, null, "Token cannot be empty"));
        }

        var userId = await tokenService.ValidateToken(request.Token);
        if (userId is null)
        {
            logger.LogDebug("Internal validation: Token signature invalid or expired.");
            return Ok(new ValidateIdentityResponse(false, null, null, null, null, null, null, "Invalid or expired token"));
        }

        var user = await userQueryService.Handle(new GetUserByIdQuery(userId.Value));
        if (user is null)
        {
            logger.LogWarning("Internal validation: User {UserId} does not exist.", userId.Value);
            return Ok(new ValidateIdentityResponse(false, null, null, null, null, null, null, "User not found"));
        }

        if (user.Status == UserStatus.Inactive)
        {
            logger.LogInformation("Internal validation: User {UserId} is inactive.", userId.Value);
            return Ok(new ValidateIdentityResponse(false, user.Id, user.Username.Value, user.Role.Value, user.HotelId, user.ChainId, user.TokenVersion, "Account is inactive"));
        }

        try
        {
            var handler = new JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(request.Token);
            var versionClaim = jwt.Claims.FirstOrDefault(c => c.Type == "token_version");

            if (versionClaim is null || !int.TryParse(versionClaim.Value, out var tokenVersion))
            {
                logger.LogWarning("Internal validation: Missing token_version claim for User {UserId}.", userId.Value);
                return Ok(new ValidateIdentityResponse(false, user.Id, user.Username.Value, user.Role.Value, user.HotelId, user.ChainId, user.TokenVersion, "Missing token version"));
            }

            if (tokenVersion != user.TokenVersion)
            {
                logger.LogInformation("Internal validation: Token version mismatch for User {UserId} (token: {TokenVer}, db: {DbVer}).", user.Id, tokenVersion, user.TokenVersion);
                return Ok(new ValidateIdentityResponse(false, user.Id, user.Username.Value, user.Role.Value, user.HotelId, user.ChainId, user.TokenVersion, "Token revoked"));
            }

            return Ok(new ValidateIdentityResponse(
                true,
                user.Id,
                user.Username.Value,
                user.Role.Value,
                user.HotelId,
                user.ChainId,
                user.TokenVersion
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Internal validation: Error parsing token claims for User {UserId}.", userId.Value);
            return Ok(new ValidateIdentityResponse(false, null, null, null, null, null, null, "Malformed token structure"));
        }
    }
}
