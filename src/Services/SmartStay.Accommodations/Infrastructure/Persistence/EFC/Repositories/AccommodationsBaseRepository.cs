using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

public abstract class AccommodationsBaseRepository<TEntity>(AccommodationsDbContext context)
    where TEntity : class
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
