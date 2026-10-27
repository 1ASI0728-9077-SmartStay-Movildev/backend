using SmartStay.Analytics.Domain.Model.Aggregates;
using SmartStay.Analytics.Domain.Model.Queries;
using SmartStay.Analytics.Domain.Repositories;
using SmartStay.Analytics.Domain.Services;

namespace SmartStay.Analytics.Application.Internal.QueryServices;

public class AnalyticsQueryService(IAnalyticsRepository analyticsRepository) : IAnalyticsQueryService
{
    public async Task<PerformanceMetrics> Handle(GetMonthlyPerformanceQuery query)
    {
        return await analyticsRepository.GetMonthlyMetricsAsync();
    }
}
