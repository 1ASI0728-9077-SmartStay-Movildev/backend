using SmartStay.Concierge.Domain;

namespace SmartStay.Concierge.Application.Ports;

/// <summary>
/// Puerto de búsqueda de alojamiento. Consulta Accommodations (catálogo) y Bookings (disponibilidad orientativa).
/// </summary>
public interface ILodgingSearch
{
    Task<LodgingSearchResult> SearchAsync(StayPreferences request, CancellationToken ct = default);
}
