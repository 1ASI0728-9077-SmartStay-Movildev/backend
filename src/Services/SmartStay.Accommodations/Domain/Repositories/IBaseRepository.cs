namespace BackendAwSmartstay.API.Accommodations.Domain.Repositories;

/// <summary>
/// Contrato base de repositorio para entidades del dominio Accommodations.
/// </summary>
public interface IBaseRepository<TEntity> where TEntity : class
{
    Task AddAsync(TEntity entity);
    Task<TEntity?> FindByIdAsync(int id);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    Task<IEnumerable<TEntity>> ListAsync();
}
