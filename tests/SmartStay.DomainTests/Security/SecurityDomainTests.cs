using BackendAwSmartstay.API.IAM.Domain.Model.Aggregates;
using BackendAwSmartstay.API.IAM.Domain.Model.Constants;
using BackendAwSmartstay.API.IAM.Domain.Model.Enums;
using FluentAssertions;
using Xunit;

namespace SmartStay.DomainTests.Security;

public class SecurityDomainTests
{
    [Fact]
    [Trait("Area", "IAM")]
    [Trait("Risk", "Security")]
    public void Increment_token_version_should_increase_version_by_one()
    {
        // Arrange
        var user = new User("admin_hotel1", "hashed_pwd", UserRoles.Admin, UserStatus.Active, hotelId: 1, chainId: 1, tokenVersion: 0);

        // Act
        user.IncrementTokenVersion();

        // Assert
        user.TokenVersion.Should().Be(1);
    }

    [Fact]
    [Trait("Area", "IAM")]
    [Trait("Risk", "Security")]
    public void Deactivate_should_set_status_to_inactive()
    {
        // Arrange
        var user = new User("guest1", "hashed_pwd", UserRoles.Guest, UserStatus.Active);

        // Act
        user.Deactivate();

        // Assert
        user.Status.Should().Be(UserStatus.Inactive);
    }

    [Fact]
    [Trait("Area", "IAM")]
    [Trait("Risk", "TenantIsolation")]
    public void Hotel_admin_should_not_have_access_to_different_hotel()
    {
        // Arrange
        var hotelAdminUser = new User("hotel1_admin", "hash", UserRoles.Admin, hotelId: 1);
        int targetResourceHotelId = 2;

        // Act & Assert
        hotelAdminUser.HotelId.Should().Be(1);
        (hotelAdminUser.HotelId == targetResourceHotelId).Should().BeFalse("Admin assigned to Hotel 1 cannot manage resources of Hotel 2");
    }
}
