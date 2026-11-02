using SmartStay.IAM.Domain.Model.Aggregates;

namespace SmartStay.IAM.Domain.Repositories;

public interface IUserRepository
{
    Task AddAsync(User user);
    Task<User?> FindByIdAsync(int id);
    Task<User?> FindByUsernameAsync(string username);
    Task<bool> ExistsByUsernameAsync(string username);
    Task<int> CountActiveByRoleAsync(string role);
    Task<IEnumerable<User>> ListAsync();
    Task<IEnumerable<User>> ListByScopeAsync(int? hotelId, int? chainId);
    void Update(User user);
    void Remove(User user);
}
