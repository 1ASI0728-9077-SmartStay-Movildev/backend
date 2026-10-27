using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Domain.Model.Commands;
using SmartStay.Profiles.Domain.Model.ValueObjects;
using SmartStay.Profiles.Domain.Repositories;
using SmartStay.Profiles.Domain.Services;

namespace SmartStay.Profiles.Application.Internal.CommandServices;

public class ProfileCommandService(
    IProfileRepository profileRepository,
    IProfilesUnitOfWork unitOfWork,
    ILogger<ProfileCommandService> logger) : IProfileCommandService
{
    public async Task<Profile?> Handle(CreateProfileCommand command)
    {
        // 1. Verificación idempotente por email o userId
        var existing = await profileRepository.FindByEmailAsync(new EmailAddress(command.Email));
        if (existing is not null)
        {
            logger.LogInformation("Profile already exists for email {Email}. Returning existing profile #{ProfileId}.", command.Email, existing.Id);
            if (command.UserId.HasValue && !existing.UserId.HasValue)
            {
                existing.LinkToUser(command.UserId.Value);
                profileRepository.Update(existing);
                await unitOfWork.CompleteAsync();
            }
            return existing;
        }

        var profile = new Profile(command);
        try
        {
            await profileRepository.AddAsync(profile);
            await unitOfWork.CompleteAsync();
            logger.LogInformation("Created new profile #{ProfileId} for email {Email}.", profile.Id, command.Email);
            return profile;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profile for email {Email}.", command.Email);
            return null;
        }
    }
}
