using SmartStay.Analytics.Domain.Model.Aggregates;
using SmartStay.Analytics.Domain.Model.Queries;

namespace SmartStay.Analytics.Domain.Services;

public interface IAnalyticsQueryService
{
    Task<PerformanceMetrics> Handle(GetMonthlyPerformanceQuery query);
}
