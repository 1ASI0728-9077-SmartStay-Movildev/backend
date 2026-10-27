using SmartStay.Profiles.Domain.Model.ValueObjects;

namespace SmartStay.Profiles.Domain.Model.Queries;

public record GetProfileByIdQuery(int ProfileId);

public record GetProfileByEmailQuery(EmailAddress Email);

public record GetProfileByUserIdQuery(int UserId);

public record GetAllProfilesQuery;
