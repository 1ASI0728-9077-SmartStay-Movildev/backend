namespace SmartStay.Contracts.Http;

/// <summary>
/// Contrato de solicitud para validar un token de identidad y su versión contra IAM.
/// </summary>
public sealed record ValidateIdentityRequest(string Token);

/// <summary>
/// Contrato de respuesta con el resultado de la validación criptográfica, estado y permisos del actor.
/// </summary>
public sealed record ValidateIdentityResponse(
    bool IsValid,
    int? UserId,
    string? Username,
    string? Role,
    int? HotelId,
    int? ChainId,
    int? TokenVersion,
    string? Reason = null
);
