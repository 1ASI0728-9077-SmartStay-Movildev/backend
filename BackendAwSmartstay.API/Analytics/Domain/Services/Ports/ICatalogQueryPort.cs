namespace BackendAwSmartstay.API.Analytics.Domain.Services.Ports;

/// <summary>
/// Puerto de consulta para métricas de catálogo de habitaciones, desacoplado de la persistencia directa de Accommodations.
/// </summary>
public interface ICatalogQueryPort
{
    Task<int> GetTotalRoomsCountAsync();
}
