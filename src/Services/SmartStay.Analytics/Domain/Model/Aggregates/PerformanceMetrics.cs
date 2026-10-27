namespace SmartStay.Analytics.Domain.Model.Aggregates;

public class PerformanceMetrics
{
    public PerformanceMetrics()
    {
        DateGenerated = DateTime.UtcNow;
    }

    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
    public double OccupancyRate { get; set; }
    public int CancelledBookings { get; set; }
    public DateTime DateGenerated { get; private set; }
}
