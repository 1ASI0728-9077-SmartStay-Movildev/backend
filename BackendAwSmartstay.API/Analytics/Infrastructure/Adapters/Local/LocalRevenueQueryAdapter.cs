using BackendAwSmartstay.API.Analytics.Domain.Services.Ports;
using BackendAwSmartstay.API.Payments.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Analytics.Infrastructure.Adapters.Local;

/// <summary>
/// Adaptador de consulta local que lee ingresos desde la persistencia compartida durante la transición.
/// </summary>
public class LocalRevenueQueryAdapter(AppDbContext context) : IRevenueQueryPort
{
    public async Task<decimal> GetTotalRevenueAsync(DateTime startUtc, DateTime endUtcExclusive)
    {
        return await context.Set<Payment>()
            .Where(p => p.PaymentDate >= startUtc && p.PaymentDate < endUtcExclusive && p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.Amount);
    }
}
