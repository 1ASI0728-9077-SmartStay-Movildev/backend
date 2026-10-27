using SmartStay.IAM.Domain.Model.Aggregates;
using SmartStay.IAM.Domain.Model.Queries;

namespace SmartStay.IAM.Domain.Services;

public interface IUserQueryService
{
    Task<User?> Handle(GetUserByIdQuery query);
    Task<IEnumerable<User>> Handle(GetUsersByScopeQuery query);
    Task<User?> Handle(GetUserByUsernameQuery query);
}