using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Profiles.Application.Internal.CommandServices;
using SmartStay.Profiles.Application.Internal.QueryServices;
using SmartStay.Profiles.Domain.Model.Commands;
using SmartStay.Profiles.Domain.Model.Queries;
using SmartStay.Profiles.Domain.Model.ValueObjects;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Profiles.Tests;

public class ProfileCreationAndIsolationTests
{
    private ProfilesDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ProfilesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ProfilesDbContext(options);
    }

    [Fact]
    [Trait("Service", "Profiles")]
    [Trait("Category", "Domain")]
    public void Profile_Format_Computes_FullName_And_FullAddress()
    {
        var profile = new Domain.Model.Aggregates.Profile(
            "Maria", "Gomez", "maria@example.com", "Av. Larco", "456", "Lima", "15074", "Peru", 12);

        profile.FullName.Should().Be("Maria Gomez");
        profile.EmailAddress.Should().Be("maria@example.com");
        profile.StreetAddress.Should().Be("Av. Larco 456, Lima, 15074, Peru");
        profile.UserId.Should().Be(12);
    }

    [Fact]
    [Trait("Service", "Profiles")]
    [Trait("Category", "Idempotency")]
    public async Task Duplicate_Email_Registration_Is_Idempotent_And_Links_User()
    {
        using var context = CreateInMemoryContext();
        var repo = new ProfileRepository(context);
        var uow = new ProfilesUnitOfWork(context);
        var mockLogger = new Mock<ILogger<ProfileCommandService>>();

        var service = new ProfileCommandService(repo, uow, mockLogger.Object);

        var cmd1 = new CreateProfileCommand("Maria", "Gomez", "maria@example.com", "Av. Larco", "456", "Lima", "15074", "Peru", null);
        var created1 = await service.Handle(cmd1);
        created1.Should().NotBeNull();
        created1!.UserId.Should().BeNull();

        // Second registration with same email but now with userId 55
        var cmd2 = new CreateProfileCommand("Maria", "Gomez", "maria@example.com", "Av. Larco", "456", "Lima", "15074", "Peru", 55);
        var created2 = await service.Handle(cmd2);

        created2.Should().NotBeNull();
        created2!.Id.Should().Be(created1.Id);
        created2.UserId.Should().Be(55);

        (await context.Profiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    [Trait("Service", "Profiles")]
    [Trait("Category", "Query")]
    public async Task QueryService_Retrieves_Profile_By_Id_And_User_Id()
    {
        using var context = CreateInMemoryContext();
        var repo = new ProfileRepository(context);
        var uow = new ProfilesUnitOfWork(context);

        var profile = new Domain.Model.Aggregates.Profile(
            "Juan", "Perez", "juan@example.com", "Calle Real", "101", "Cusco", "08000", "Peru", 77);
        await repo.AddAsync(profile);
        await uow.CompleteAsync();

        var queryService = new ProfileQueryService(repo);

        var byId = await queryService.Handle(new GetProfileByIdQuery(profile.Id));
        byId.Should().NotBeNull();
        byId!.FullName.Should().Be("Juan Perez");

        var byUserId = await queryService.Handle(new GetProfileByUserIdQuery(77));
        byUserId.Should().NotBeNull();
        byUserId!.EmailAddress.Should().Be("juan@example.com");
    }
}
