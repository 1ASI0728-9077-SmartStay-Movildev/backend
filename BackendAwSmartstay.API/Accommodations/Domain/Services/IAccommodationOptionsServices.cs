using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;

namespace BackendAwSmartstay.API.Accommodations.Domain.Services;

public interface IAccommodationOptionsQueryService
{
    Task<IEnumerable<string>> GetAllCategoriesAsync();
    Task<IEnumerable<string>> GetAllAmenitiesAsync();
}

public interface IAccommodationOptionsCommandService
{
    Task<(bool Success, string? ErrorMessage)> CreateCategoryAsync(string name);
    Task<(bool Success, string? ErrorMessage)> CreateAmenityAsync(string name);
}
