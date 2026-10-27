using Microsoft.EntityFrameworkCore;
using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Domain.Model.ValueObjects;
using SmartStay.Profiles.Domain.Repositories;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Profiles.Infrastructure.Persistence.EFC.Repositories;

public class ProfileRepository(ProfilesDbContext context) : IProfileRepository
{
    public async Task AddAsync(Profile profile)
    {
        await context.Profiles.AddAsync(profile);
    }

    public async Task<Profile?> FindByIdAsync(int id)
    {
        return await context.Profiles.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Profile?> FindByEmailAsync(EmailAddress email)
    {
        return await context.Profiles.FirstOrDefaultAsync(p => p.Email.Address == email.Address);
    }

    public async Task<Profile?> FindByUserIdAsync(int userId)
    {
        return await context.Profiles.FirstOrDefaultAsync(p => p.UserId == userId);
    }

    public async Task<IEnumerable<Profile>> ListAsync()
    {
        return await context.Profiles.ToListAsync();
    }

    public void Update(Profile profile)
    {
        context.Profiles.Update(profile);
    }
}
