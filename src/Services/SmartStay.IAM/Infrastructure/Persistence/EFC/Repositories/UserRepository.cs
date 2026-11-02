using Microsoft.EntityFrameworkCore;
using SmartStay.IAM.Domain.Model.Aggregates;
using SmartStay.IAM.Domain.Repositories;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.IAM.Infrastructure.Persistence.EFC.Repositories;

public class UserRepository(IamDbContext context) : IUserRepository
{
    public async Task AddAsync(User user)
    {
        await context.Users.AddAsync(user);
    }

    public async Task<User?> FindByIdAsync(int id)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<User?> FindByUsernameAsync(string username)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Username.Value == username);
    }

    public async Task<bool> ExistsByUsernameAsync(string username)
    {
        return await context.Users.AnyAsync(u => u.Username.Value == username);
    }

    public async Task<int> CountActiveByRoleAsync(string role)
    {
        return await context.Users
            .Where(u => u.Role.Value == role && u.Status == Domain.Model.Enums.UserStatus.Active)
            .CountAsync();
    }

    public async Task<IEnumerable<User>> ListAsync()
    {
        return await context.Users.ToListAsync();
    }

    public async Task<IEnumerable<User>> ListByScopeAsync(int? hotelId, int? chainId)
    {
        var query = context.Users.AsQueryable();

        if (hotelId.HasValue)
        {
            query = query.Where(u => u.HotelId == hotelId.Value);
        }
        else if (chainId.HasValue)
        {
            query = query.Where(u => u.ChainId == chainId.Value);
        }

        return await query.ToListAsync();
    }

    public void Update(User user)
    {
        context.Users.Update(user);
    }

    public void Remove(User user)
    {
        context.Users.Remove(user);
    }
}
