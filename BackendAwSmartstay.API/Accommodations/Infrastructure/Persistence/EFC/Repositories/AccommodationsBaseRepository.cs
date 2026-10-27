using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using BackendAwSmartstay.API.Shared.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

/// <summary>
/// Repositorio base ligado exclusivamente a AccommodationsDbContext.
/// </summary>
public abstract class AccommodationsBaseRepository<TEntity>(AccommodationsDbContext context)
    : IBaseRepository<TEntity> where TEntity : class
{
    protected readonly AccommodationsDbContext Context = context;

    public async Task AddAsync(TEntity entity)
    {
        await Context.Set<TEntity>().AddAsync(entity);
    }

    public async Task<TEntity?> FindByIdAsync(int id)
    {
        return await Context.Set<TEntity>().FindAsync(id);
    }

    public void Update(TEntity entity)
    {
        Context.Set<TEntity>().Update(entity);
    }

    public void Remove(TEntity entity)
    {
        Context.Set<TEntity>().Remove(entity);
    }

    public async Task<IEnumerable<TEntity>> ListAsync()
    {
        return await Context.Set<TEntity>().ToListAsync();
    }
}
