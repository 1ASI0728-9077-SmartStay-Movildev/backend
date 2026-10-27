using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartStay.Concierge.Application;
using SmartStay.Concierge.Application.Ports;
using SmartStay.Concierge.Domain;
using Xunit;

namespace SmartStay.Concierge.Tests;

public class ConciergeRecommendationTests
{
    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Category", "Recommendation")]
    public async Task RecommendRooms_Returns_Deterministic_Ordered_Offers_When_Within_Budget()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        var testOffers = new List<RoomOffer>
        {
            new(101, 1, "Grand Hotel Bolivar", "Single Standard", "Cercado de Lima", 85.00m, 170.00m, "USD", 1, null, ["Wifi", "TV"]),
            new(102, 1, "Grand Hotel Bolivar", "Double Deluxe", "Cercado de Lima", 150.00m, 300.00m, "USD", 2, "/tours/room-102/manifest.json", ["Wifi", "TV", "Minibar"])
        };

        mockSearch.Setup(s => s.FindAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(testOffers);

        mockModel.Setup(m => m.CompleteChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Te recomiendo especialmente la Habitación 102 con balcón y tour 360° para tu estadía de 2 noches.");

        var handler = new RecommendRooms(mockSearch.Object, mockModel.Object, NullLogger<RecommendRooms>.Instance);

        var request = new ConciergeRecommendationRequest(
            Message: "Busco una habitación cómoda para 2 personas en Lima",
            CheckIn: new DateOnly(2026, 11, 1),
            CheckOut: new DateOnly(2026, 11, 3),
            Guests: 2,
            District: "Cercado de Lima",
            BudgetTotal: 400.00m
        );

        // Act
        var result = await handler.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FallbackUsed.Should().BeFalse();
        result.AssistantMessage.Should().Contain("Habitación 102");
        result.Offers.Should().HaveCount(2);
        result.Offers[0].RoomId.Should().Be(101);
        result.Offers[1].RoomId.Should().Be(102);
        result.Offers[1].TourPath.Should().Be("/tours/room-102/manifest.json");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Category", "Resilience")]
    public async Task RecommendRooms_Uses_Fallback_When_Model_Returns_Empty_Or_Fails()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        var testOffers = new List<RoomOffer>
        {
            new(102, 1, "Grand Hotel Bolivar", "Double Deluxe", "Cercado de Lima", 150.00m, 300.00m, "USD", 2, "/tours/room-102/manifest.json", ["Wifi", "TV"])
        };

        mockSearch.Setup(s => s.FindAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(testOffers);

        // Model returns empty (e.g. timeout, rate limit or no key)
        mockModel.Setup(m => m.CompleteChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var handler = new RecommendRooms(mockSearch.Object, mockModel.Object, NullLogger<RecommendRooms>.Instance);

        var request = new ConciergeRecommendationRequest(
            Message: "Habitación para 2",
            CheckIn: new DateOnly(2026, 11, 1),
            CheckOut: new DateOnly(2026, 11, 3),
            Guests: 2,
            District: "Cercado de Lima",
            BudgetTotal: 500.00m
        );

        // Act
        var result = await handler.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FallbackUsed.Should().BeTrue();
        result.AssistantMessage.Should().Contain("Hemos seleccionado las 1 mejores opciones verificadas");
        result.Offers.Should().HaveCount(1);
        result.Offers[0].RoomId.Should().Be(102);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Category", "EmptyState")]
    public async Task RecommendRooms_Returns_Empty_And_Friendly_Message_When_No_Rooms_Available()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        mockSearch.Setup(s => s.FindAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoomOffer>());

        var handler = new RecommendRooms(mockSearch.Object, mockModel.Object, NullLogger<RecommendRooms>.Instance);

        var request = new ConciergeRecommendationRequest(
            Message: "Habitación en Arequipa",
            CheckIn: new DateOnly(2026, 11, 1),
            CheckOut: new DateOnly(2026, 11, 3),
            Guests: 2,
            District: "Arequipa",
            BudgetTotal: 200.00m
        );

        // Act
        var result = await handler.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Offers.Should().BeEmpty();
        result.FallbackUsed.Should().BeTrue();
        result.AssistantMessage.Should().Contain("No encontramos habitaciones disponibles");
    }
}
