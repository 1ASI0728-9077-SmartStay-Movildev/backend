namespace SmartStay.Accommodations.Infrastructure.Security;

/// <summary>
/// Representa la identidad y el contexto del actor autenticado que ejecuta la solicitud en Accommodations.
/// </summary>
public sealed record CurrentActor(
    int UserId,
    string Username,
    string Role,
    int? HotelId,
    int? ChainId
)
{
    public bool IsChainAdmin => string.Equals(Role, "chain_admin", StringComparison.OrdinalIgnoreCase);
    public bool IsHotelAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verifica si el actor tiene autorización para acceder o mutar datos de un hotel específico.
    /// </summary>
    public bool CanAccessHotel(int targetHotelId)
    {
        if (IsChainAdmin) return true;
        if (IsHotelAdmin) return HotelId == targetHotelId;
        return true; // Huéspedes u otros roles pueden consultar hoteles autorizados
    }
}
