namespace SmartStay.Concierge.Infrastructure.Security;

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
}
