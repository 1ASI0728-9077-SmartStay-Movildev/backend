using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Domain.Model.ValueObjects;

namespace SmartStay.Profiles.Domain.Repositories;

public interface IProfileRepository
{
    Task AddAsync(Profile profile);
    Task<Profile?> FindByIdAsync(int id);
    Task<Profile?> FindByEmailAsync(EmailAddress email);
    Task<Profile?> FindByUserIdAsync(int userId);
    Task<IEnumerable<Profile>> ListAsync();
    void Update(Profile profile);
}
