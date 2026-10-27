using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Domain.Model.Commands;
using SmartStay.Profiles.Interfaces.REST.Resources;

namespace SmartStay.Profiles.Interfaces.REST.Transform;

public static class ProfileAssemblers
{
    public static ProfileResource ToResourceFromEntity(Profile entity)
    {
        return new ProfileResource(
            entity.Id,
            entity.FullName,
            entity.EmailAddress,
            entity.StreetAddress,
            entity.UserId
        );
    }

    public static CreateProfileCommand ToCommandFromResource(CreateProfileResource resource)
    {
        return new CreateProfileCommand(
            resource.FirstName,
            resource.LastName,
            resource.Email,
            resource.Street,
            resource.Number,
            resource.City,
            resource.PostalCode,
            resource.Country,
            resource.UserId
        );
    }
}
