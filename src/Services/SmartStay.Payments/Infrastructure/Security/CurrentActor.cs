namespace SmartStay.Payments.Infrastructure.Security;

public record CurrentActor(int Id, string Username, string Role, int? TokenVersion);
