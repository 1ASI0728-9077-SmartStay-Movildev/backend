using SmartStay.IAM.Application.ACL.Services;
using SmartStay.IAM.Application.Internal.CommandServices;
using SmartStay.IAM.Application.Internal.QueryServices;
using SmartStay.IAM.Application.OutboundServices;
using SmartStay.IAM.Domain.Repositories;
using SmartStay.IAM.Domain.Services;
using SmartStay.IAM.Infrastructure.Hashing.BCrypt.Services;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.IAM.Infrastructure.Tokens.JWT.Configuration;
using SmartStay.IAM.Infrastructure.Tokens.JWT.Services;
using SmartStay.IAM.Interfaces.ACL;

namespace SmartStay.IAM.Infrastructure.Interfaces.ASP.Configuration.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void AddIamContextServices(this WebApplicationBuilder builder)
    {
        // TokenSettings Configuration

        builder.Services.Configure<TokenSettings>(builder.Configuration.GetSection("TokenSettings"));

        // IAM Bounded Context Injection Configuration

        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IUserCommandService, UserCommandService>();
        builder.Services.AddScoped<IUserQueryService, UserQueryService>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IHashingService, HashingService>();
        builder.Services.AddScoped<IIamContextFacade, IamContextFacade>();
        
        builder.Services.AddScoped<IRoleAuthorizationService, RoleAuthorizationService>();
        builder.Services.AddScoped<IUserScopeService, UserScopeService>();
    }
}