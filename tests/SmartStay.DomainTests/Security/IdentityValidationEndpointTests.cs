using BackendAwSmartstay.API.IAM.Application.OutboundServices;
using BackendAwSmartstay.API.IAM.Domain.Model.Aggregates;
using BackendAwSmartstay.API.IAM.Domain.Model.Constants;
using BackendAwSmartstay.API.IAM.Domain.Model.Enums;
using BackendAwSmartstay.API.IAM.Domain.Model.Queries;
using BackendAwSmartstay.API.IAM.Domain.Services;
using BackendAwSmartstay.API.IAM.Interfaces.REST;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SmartStay.Contracts.Http;
using Xunit;

namespace SmartStay.DomainTests.Security;

public class IdentityValidationEndpointTests
{
    private class FakeTokenService(int? validatedUserId) : ITokenService
    {
        public string GenerateToken(User user) => "dummy_token";
        public Task<int?> ValidateToken(string token) => Task.FromResult(token == "valid_token" ? validatedUserId : null);
    }

    private class FakeUserQueryService(User? userToReturn) : IUserQueryService
    {
        public Task<User?> Handle(GetUserByIdQuery query) => Task.FromResult(userToReturn);
        public Task<User?> Handle(GetUserByUsernameQuery query) => Task.FromResult(userToReturn);
        public Task<IEnumerable<User>> Handle(GetUsersByScopeQuery query) => Task.FromResult<IEnumerable<User>>([userToReturn!]);
    }

    [Fact]
    [Trait("Area", "IAM")]
    [Trait("Risk", "Security")]
    public async Task ValidateToken_returns_invalid_when_token_is_empty()
    {
        // Arrange
        var controller = new InternalIdentityController(
            new FakeTokenService(null),
            new FakeUserQueryService(null),
            NullLogger<InternalIdentityController>.Instance);

        // Act
        var result = await controller.ValidateToken(new ValidateIdentityRequest(""));

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ValidateIdentityResponse>().Subject;
        response.IsValid.Should().BeFalse();
        response.Reason.Should().Be("Token cannot be empty");
    }

    [Fact]
    [Trait("Area", "IAM")]
    [Trait("Risk", "Security")]
    public async Task ValidateToken_returns_invalid_when_token_signature_is_untrusted()
    {
        // Arrange
        var controller = new InternalIdentityController(
            new FakeTokenService(null),
            new FakeUserQueryService(null),
            NullLogger<InternalIdentityController>.Instance);

        // Act
        var result = await controller.ValidateToken(new ValidateIdentityRequest("untrusted_token"));

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ValidateIdentityResponse>().Subject;
        response.IsValid.Should().BeFalse();
        response.Reason.Should().Be("Invalid or expired token");
    }

    [Fact]
    [Trait("Area", "IAM")]
    [Trait("Risk", "Security")]
    public async Task ValidateToken_returns_invalid_when_user_is_inactive()
    {
        // Arrange
        var inactiveUser = new User("inactive_user", "hash", UserRoles.Guest, UserStatus.Inactive);
        var controller = new InternalIdentityController(
            new FakeTokenService(1),
            new FakeUserQueryService(inactiveUser),
            NullLogger<InternalIdentityController>.Instance);

        // Act
        var result = await controller.ValidateToken(new ValidateIdentityRequest("valid_token"));

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ValidateIdentityResponse>().Subject;
        response.IsValid.Should().BeFalse();
        response.Reason.Should().Be("Account is inactive");
    }
}
