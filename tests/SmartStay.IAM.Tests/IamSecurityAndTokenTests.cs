using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmartStay.Contracts.Http;
using SmartStay.IAM.Application.Internal.CommandServices;
using SmartStay.IAM.Application.Internal.QueryServices;
using SmartStay.IAM.Domain.Model.Aggregates;
using SmartStay.IAM.Domain.Model.Commands;
using SmartStay.IAM.Domain.Model.Constants;
using SmartStay.IAM.Domain.Model.Enums;
using SmartStay.IAM.Domain.Model.Queries;
using SmartStay.IAM.Domain.Services;
using SmartStay.IAM.Infrastructure.Hashing.BCrypt.Services;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.IAM.Infrastructure.Tokens.JWT.Configuration;
using SmartStay.IAM.Infrastructure.Tokens.JWT.Services;
using SmartStay.IAM.Interfaces.REST;
using Xunit;

namespace SmartStay.IAM.Tests;

public class IamSecurityAndTokenTests
{
    private IamDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<IamDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new IamDbContext(options);
    }

    private TokenService CreateTokenService()
    {
        var settings = Options.Create(new TokenSettings
        {
            Secret = "EstaEsUnaClaveSecretaMuyLargaYSeguraParaSmartStay123!",
            Audience = "http://localhost:5173",
            Issuer = "http://localhost:5192",
            ExpirationInHours = 24
        });
        return new TokenService(settings);
    }

    [Fact]
    [Trait("Service", "IAM")]
    [Trait("Category", "Security")]
    public async Task SignUp_Creates_User_With_Hashed_Password_And_Default_Guest_Role()
    {
        using var context = CreateInMemoryContext();
        var repo = new UserRepository(context);
        var uow = new IamUnitOfWork(context);
        var hashing = new HashingService();
        var tokenService = CreateTokenService();
        var scopeService = new UserScopeService();
        var roleAuth = new RoleAuthorizationService(scopeService);

        var service = new UserCommandService(repo, tokenService, hashing, roleAuth, scopeService, uow);

        var cmd = new SignUpCommand("guestuser@smartstay.com", "Password123!");
        await service.Handle(cmd);

        var user = await repo.FindByUsernameAsync("guestuser@smartstay.com");
        user.Should().NotBeNull();
        user!.Role.Value.Should().Be(UserRoles.Guest);
        user.Status.Should().Be(UserStatus.Active);
        hashing.VerifyPassword("Password123!", user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    [Trait("Service", "IAM")]
    [Trait("Category", "Security")]
    public async Task Token_Version_Revocation_Invalidates_Internal_Validation()
    {
        using var context = CreateInMemoryContext();
        var repo = new UserRepository(context);
        var uow = new IamUnitOfWork(context);
        var tokenService = CreateTokenService();
        var queryService = new UserQueryService(repo, new UserScopeService());
        var mockLogger = new Mock<ILogger<InternalIdentityController>>();

        var hashing = new HashingService();
        var user = new User("admin@smartstay.com", hashing.HashPassword("Pass123!"), UserRoles.Admin, tokenVersion: 0);
        await repo.AddAsync(user);
        await uow.CompleteAsync();

        // Generate token for version 0
        var token = tokenService.GenerateToken(user);
        token.Should().NotBeNullOrEmpty();

        var controller = new InternalIdentityController(tokenService, queryService, mockLogger.Object);

        // Initial validation: valid
        var result1 = await controller.ValidateToken(new ValidateIdentityRequest(token)) as OkObjectResult;
        result1.Should().NotBeNull();
        var resp1 = result1!.Value as ValidateIdentityResponse;
        resp1.Should().NotBeNull();
        resp1!.IsValid.Should().BeTrue();

        // Act: Revoke session by bumping TokenVersion
        user.IncrementTokenVersion();
        repo.Update(user);
        await uow.CompleteAsync();

        // Subsequent validation of old token: rejected
        var result2 = await controller.ValidateToken(new ValidateIdentityRequest(token)) as OkObjectResult;
        result2.Should().NotBeNull();
        var resp2 = result2!.Value as ValidateIdentityResponse;
        resp2.Should().NotBeNull();
        resp2!.IsValid.Should().BeFalse();
        resp2.Reason.Should().Be("Token revoked");
    }

    [Fact]
    [Trait("Service", "IAM")]
    [Trait("Category", "Security")]
    public async Task Inactive_User_Token_Validation_Returns_Account_Inactive()
    {
        using var context = CreateInMemoryContext();
        var repo = new UserRepository(context);
        var uow = new IamUnitOfWork(context);
        var tokenService = CreateTokenService();
        var queryService = new UserQueryService(repo, new UserScopeService());
        var mockLogger = new Mock<ILogger<InternalIdentityController>>();

        var hashing = new HashingService();
        var user = new User("inactive@smartstay.com", hashing.HashPassword("Pass123!"), UserRoles.Guest, status: UserStatus.Inactive);
        await repo.AddAsync(user);
        await uow.CompleteAsync();

        var token = tokenService.GenerateToken(user);
        var controller = new InternalIdentityController(tokenService, queryService, mockLogger.Object);

        var result = await controller.ValidateToken(new ValidateIdentityRequest(token)) as OkObjectResult;
        var resp = result!.Value as ValidateIdentityResponse;
        resp.Should().NotBeNull();
        resp!.IsValid.Should().BeFalse();
        resp.Reason.Should().Be("Account is inactive");
    }
}
