using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

public class RoomTypeRepository(AccommodationsDbContext context)
    : AccommodationsBaseRepository<RoomType>(context), IRoomTypeRepository;
