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
    public async Task RecommendRooms_Returns_Validated_Offers_When_Within_Budget()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        var testOffers = new List<RoomOffer>
        {
            new(101, 1, "Grand Hotel Bolivar", "Single Standard", "Cercado de Lima", 85.00m, 170.00m, "USD", 1, null, ["Wifi", "TV"]),
            new(102, 1, "Grand Hotel Bolivar", "Double Deluxe", "Cercado de Lima", 150.00m, 300.00m, "USD", 2, "/tours/room-102/manifest.json", ["Wifi", "TV", "Minibar"])
        };

        var searchResult = new LodgingSearchResult(
            AvailableOffers: testOffers,
            AvailabilityServiceAvailable: true,
            BudgetExceeded: false,
            LowestAvailableTotal: 170.00m,
            TotalCatalogMatches: 2
        );

        mockSearch.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        // Structured JSON from model with room 102 as best match
        var modelJson = """
        {
          "recommendations": [
            { "roomId": 102, "reasonCode": "BEST_MATCH", "summary": "Ideal para 2 personas con balcón y recorrido virtual 360°." },
            { "roomId": 101, "reasonCode": "ECONOMY", "summary": "Opción más accesible." }
          ]
        }
        """;

        mockModel.Setup(m => m.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(modelJson);

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
        result.AssistantMessage.Should().Contain("Double Deluxe");
        result.AssistantMessage.Should().Contain("Tarifas y disponibilidad autoritativas calculadas por SmartStay");
        result.Offers.Should().HaveCount(2);
        result.Offers[0].RoomId.Should().Be(102); // Order guided by AI's top match
        result.Offers[0].StayTotal.Should().Be(300.00m);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Category", "Security")]
    public async Task RecommendRooms_Discards_Hallucinated_Ids_From_Model()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        var testOffers = new List<RoomOffer>
        {
            new(101, 1, "Grand Hotel Bolivar", "Single Standard", "Cercado de Lima", 85.00m, 170.00m, "USD", 1, null, ["Wifi"])
        };

        var searchResult = new LodgingSearchResult(
            AvailableOffers: testOffers,
            AvailabilityServiceAvailable: true,
            BudgetExceeded: false,
            LowestAvailableTotal: 170.00m,
            TotalCatalogMatches: 1
        );

        mockSearch.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        // Model hallucinates room 999 which does not exist in catalog
        var hallucinatedJson = """
        {
          "recommendations": [
            { "roomId": 999, "reasonCode": "HALLUCINATED", "summary": "Habitación inventada con precio ficticio." }
          ]
        }
        """;

        mockModel.Setup(m => m.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hallucinatedJson);

        var handler = new RecommendRooms(mockSearch.Object, mockModel.Object, NullLogger<RecommendRooms>.Instance);

        var request = new ConciergeRecommendationRequest(
            Message: "Habitación barata",
            CheckIn: new DateOnly(2026, 11, 1),
            CheckOut: new DateOnly(2026, 11, 3),
            Guests: 1,
            District: "Cercado de Lima",
            BudgetTotal: 200.00m
        );

        // Act
        var result = await handler.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FallbackUsed.Should().BeTrue(); // Hallucination discarded -> fell back to server deterministic
        result.Offers.Should().HaveCount(1);
        result.Offers[0].RoomId.Should().Be(101); // Only the authentic room is returned
        result.Offers[0].RoomId.Should().NotBe(999);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Category", "Resilience")]
    public async Task RecommendRooms_Discriminates_Availability_Service_Failure()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        var searchResult = new LodgingSearchResult(
            AvailableOffers: [],
            AvailabilityServiceAvailable: false, // Bookings service down
            BudgetExceeded: false,
            LowestAvailableTotal: 0m,
            TotalCatalogMatches: 2
        );

        mockSearch.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

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
        result.AssistantMessage.Should().Contain("No pudimos consultar la disponibilidad en tiempo real con el sistema de reservas");
        result.Offers.Should().BeEmpty();
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Category", "Budget")]
    public async Task RecommendRooms_Discriminates_Budget_Exceeded_And_Prompts_User()
    {
        // Arrange
        var mockSearch = new Mock<ILodgingSearch>();
        var mockModel = new Mock<IConciergeModel>();

        var searchResult = new LodgingSearchResult(
            AvailableOffers: [],
            AvailabilityServiceAvailable: true,
            BudgetExceeded: true,
            LowestAvailableTotal: 300.00m,
            TotalCatalogMatches: 1
        );

        mockSearch.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        var handler = new RecommendRooms(mockSearch.Object, mockModel.Object, NullLogger<RecommendRooms>.Instance);

        var request = new ConciergeRecommendationRequest(
            Message: "Habitación económica",
            CheckIn: new DateOnly(2026, 11, 1),
            CheckOut: new DateOnly(2026, 11, 3),
            Guests: 2,
            District: "Cercado de Lima",
            BudgetTotal: 100.00m // User budget is $100, but lowest is $300
        );

        // Act
        var result = await handler.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FallbackUsed.Should().BeTrue();
        result.AssistantMessage.Should().Contain("todas exceden tu presupuesto de $100.00 USD");
        result.AssistantMessage.Should().Contain("La menor tarifa disponible para tus fechas es de $300.00 USD");
        result.AssistantMessage.Should().Contain("¿Deseas ampliar tu presupuesto");
        result.Offers.Should().BeEmpty();
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

        var searchResult = new LodgingSearchResult(
            AvailableOffers: testOffers,
            AvailabilityServiceAvailable: true,
            BudgetExceeded: false,
            LowestAvailableTotal: 300.00m,
            TotalCatalogMatches: 1
        );

        mockSearch.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        // Model returns empty (e.g. timeout or offline)
        mockModel.Setup(m => m.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        result.AssistantMessage.Should().Contain("Tarifas autoritativas garantizadas por el servidor");
        result.Offers.Should().HaveCount(1);
        result.Offers[0].RoomId.Should().Be(102);
    }
}
