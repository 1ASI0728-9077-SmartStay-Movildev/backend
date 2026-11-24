namespace SmartStay.Payments.Infrastructure.Configuration;

public class PaymentFeaturesOptions
{
    public const string SectionName = "Features";
    public bool EnableDemoPayment { get; set; } = false;
}
