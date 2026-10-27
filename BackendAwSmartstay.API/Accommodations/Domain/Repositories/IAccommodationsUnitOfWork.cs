namespace BackendAwSmartstay.API.Accommodations.Domain.Repositories;

/// <summary>
/// Unidad de trabajo exclusiva del contexto de Accommodations.
/// </summary>
public interface IAccommodationsUnitOfWork
{
    Task CompleteAsync();
}
