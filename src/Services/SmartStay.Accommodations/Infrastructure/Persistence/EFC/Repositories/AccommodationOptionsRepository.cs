using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

public class AccommodationOptionsRepository(AccommodationsDbContext context) : IAccommodationOptionsRepository
{
    public async Task<IEnumerable<HotelCategory>> ListCategoriesAsync()
    {
        return await context.HotelCategories.ToListAsync();
    }

    public async Task<IEnumerable<Amenity>> ListAmenitiesAsync()
    {
        return await context.AmenitiesCatalog.ToListAsync();
    }

    public async Task<bool> CategoryExistsByNameAsync(string name)
    {
        return await context.HotelCategories.AnyAsync(c => c.Name == name);
    }

    public async Task<bool> AmenityExistsByNameAsync(string name)
    {
        return await context.AmenitiesCatalog.AnyAsync(a => a.Name == name);
    }

    public async Task AddCategoryAsync(HotelCategory category)
    {
        await context.HotelCategories.AddAsync(category);
    }

    public async Task AddAmenityAsync(Amenity amenity)
    {
        await context.AmenitiesCatalog.AddAsync(amenity);
    }
}
