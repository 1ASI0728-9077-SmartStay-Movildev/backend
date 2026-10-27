using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;

namespace BackendAwSmartstay.API.Accommodations.Domain.Repositories;

/// <summary>
/// Repositorio de opciones maestras de alojamiento (Categorías y Amenities).
/// </summary>
public interface IAccommodationOptionsRepository
{
    Task<IEnumerable<HotelCategory>> ListCategoriesAsync();
    Task<IEnumerable<Amenity>> ListAmenitiesAsync();
    Task<bool> CategoryExistsByNameAsync(string name);
    Task<bool> AmenityExistsByNameAsync(string name);
    Task AddCategoryAsync(HotelCategory category);
    Task AddAmenityAsync(Amenity amenity);
}
