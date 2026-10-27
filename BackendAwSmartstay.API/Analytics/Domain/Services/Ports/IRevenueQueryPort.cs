namespace BackendAwSmartstay.API.Analytics.Domain.Services.Ports;

/// <summary>
/// Puerto de consulta para métricas de ingresos, desacoplado de la persistencia directa de Payments.
/// </summary>
public interface IRevenueQueryPort
{
    Task<decimal> GetTotalRevenueAsync(DateTime startUtc, DateTime endUtcExclusive);
}
