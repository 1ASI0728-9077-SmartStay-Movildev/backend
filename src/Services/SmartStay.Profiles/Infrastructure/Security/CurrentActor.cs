namespace SmartStay.Profiles.Infrastructure.Security;

public record CurrentActor(int Id, string Username, string Role, int? TokenVersion);
