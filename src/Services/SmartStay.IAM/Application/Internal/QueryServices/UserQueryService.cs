using SmartStay.IAM.Domain.Model.Aggregates;
using SmartStay.IAM.Domain.Model.Queries;
using SmartStay.IAM.Domain.Model.ValueObjects;
using SmartStay.IAM.Domain.Repositories;
using SmartStay.IAM.Domain.Services;

namespace SmartStay.IAM.Application.Internal.QueryServices;

public class UserQueryService(
    IUserRepository userRepository,
    IUserScopeService userScopeService) : IUserQueryService
{
    public async Task<User?> Handle(GetUserByIdQuery query)
    {
        var target = await userRepository.FindByIdAsync(query.Id);
        if (target == null) return null;

        // Scope verification
        if (query.ActorUserId.HasValue)
        {
            var actor = await userRepository.FindByIdAsync(query.ActorUserId.Value);
            if (actor == null || !userScopeService.CanAccessUser(actor, target))
                return null; 
        }

        return target;
    }

    public async Task<IEnumerable<User>> Handle(GetUsersByScopeQuery query)
    {
        var actor = await userRepository.FindByIdAsync(query.ActorUserId);
        if (actor == null) return Enumerable.Empty<User>();

        IEnumerable<User> scopedUsers;
        if (string.Equals(actor.Role.Value, Domain.Model.Constants.UserRoles.ChainAdmin, StringComparison.OrdinalIgnoreCase))
        {
            scopedUsers = await userRepository.ListByScopeAsync(null, actor.ChainId);
        }
        else if (string.Equals(actor.Role.Value, Domain.Model.Constants.UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            scopedUsers = await userRepository.ListByScopeAsync(actor.HotelId, null);
        }
        else
        {
            return new[] { actor };
        }

        return scopedUsers.Where(target => userScopeService.CanAccessUser(actor, target));
    }

    public async Task<User?> Handle(GetUserByUsernameQuery query)
    {
        var username = new Username(query.Username);
        return await userRepository.FindByUsernameAsync(username);
    }
}