namespace SmartStay.Concierge.Domain;

/// <summary>
/// Resultado detallado de la búsqueda y verificación de catálogo y disponibilidad.
/// Permite diferenciar fallos de disponibilidad de catálogo vacío o presupuesto insuficiente.
/// </summary>
public sealed record LodgingSearchResult(
    IReadOnlyList<RoomOffer> AvailableOffers,
    bool AvailabilityServiceAvailable,
    bool BudgetExceeded,
    decimal LowestAvailableTotal,
    int TotalCatalogMatches
);
