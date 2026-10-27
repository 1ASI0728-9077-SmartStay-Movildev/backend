namespace SmartStay.Concierge.Domain;

/// <summary>
/// Preferencias de estancia del huésped extraídas o provistas en la conversación.
/// </summary>
public sealed record StayPreferences(
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Guests,
    string District,
    decimal BudgetTotal,
    string Currency = "USD"
);
