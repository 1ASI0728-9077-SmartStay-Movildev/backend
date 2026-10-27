using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Domain.Model.Queries;
using SmartStay.Profiles.Domain.Repositories;
using SmartStay.Profiles.Domain.Services;

namespace SmartStay.Profiles.Application.Internal.QueryServices;

public class ProfileQueryService(IProfileRepository profileRepository) : IProfileQueryService
{
    public async Task<Profile?> Handle(GetProfileByIdQuery query)
    {
        return await profileRepository.FindByIdAsync(query.ProfileId);
    }

    public async Task<Profile?> Handle(GetProfileByEmailQuery query)
    {
        return await profileRepository.FindByEmailAsync(query.Email);
    }

    public async Task<Profile?> Handle(GetProfileByUserIdQuery query)
    {
        return await profileRepository.FindByUserIdAsync(query.UserId);
    }

    public async Task<IEnumerable<Profile>> Handle(GetAllProfilesQuery query)
    {
        return await profileRepository.ListAsync();
    }
}
