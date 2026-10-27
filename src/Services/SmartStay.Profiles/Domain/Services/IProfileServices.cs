using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Domain.Model.Commands;
using SmartStay.Profiles.Domain.Model.Queries;

namespace SmartStay.Profiles.Domain.Services;

public interface IProfileCommandService
{
    Task<Profile?> Handle(CreateProfileCommand command);
}

public interface IProfileQueryService
{
    Task<Profile?> Handle(GetProfileByIdQuery query);
    Task<Profile?> Handle(GetProfileByEmailQuery query);
    Task<Profile?> Handle(GetProfileByUserIdQuery query);
    Task<IEnumerable<Profile>> Handle(GetAllProfilesQuery query);
}
