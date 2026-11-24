using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartStay.Concierge.Application;
using SmartStay.Concierge.Application.Ports;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Contracts.Http;
using Xunit;

namespace SmartStay.Concierge.Tests;

public class CandidateEvaluatorTests
{
    private static PurchaseIntent CreateTestIntent(decimal budget = 500m)
    {
        return new PurchaseIntent(
            id: "cpi_eval_test",
            guestUserId: 2,
            idempotencyKey: "key-eval-1",
            demoPaymentMethodId: "dpm_sample",
            checkIn: new DateOnly(2026, 12, 10),
            checkOut: new DateOnly(2026, 12, 12),
            guests: 2,
            district: "Cercado de Lima",
            budgetTotal: budget,
            currency: "USD",
            contactName: "Guest",
            contactEmail: "guest@example.com",
            contactPhone: "+51999999999",
            cancellationPolicyAccepted: true
        );
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F17")]
    public async Task Lowest_StayTotal_Room_Selected_First()
    {
        var lodgingSearchMock = new Mock<ILodgingSearch>();
        var bookingsClientMock = new Mock<IBookingsClient>();

        var offers = new List<RoomOffer>
        {
            new(RoomId: 101, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "Deluxe", District: "Cercado de Lima", NightlyPrice: 150m, StayTotal: 300m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 102, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "Standard", District: "Cercado de Lima", NightlyPrice: 100m, StayTotal: 200m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 103, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "Suite", District: "Cercado de Lima", NightlyPrice: 180m, StayTotal: 360m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: [])
        };

        lodgingSearchMock.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LodgingSearchResult(offers, AvailabilityServiceAvailable: true, BudgetExceeded: false, 200m, 3));

        bookingsClientMock.Setup(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 102), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteRequestResult(true, new BookingQuoteResponse("qte_102", 102, 200m, "USD", DateTime.UtcNow.AddMinutes(2)), null));

        var evaluator = new CandidateEvaluator(lodgingSearchMock.Object, bookingsClientMock.Object, NullLogger<CandidateEvaluator>.Instance);

        var result = await evaluator.EvaluateCandidatesAsync(CreateTestIntent());

        result.Success.Should().BeTrue();
        result.SelectedCandidate.Should().NotBeNull();
        result.SelectedCandidate!.RoomId.Should().Be(102, "Room with lowest StayTotal ($200) must be selected first");
        result.Quote!.QuoteId.Should().Be("qte_102");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F17")]
    public async Task Authoritative_TieBreaking_On_Lowest_RoomId()
    {
        var lodgingSearchMock = new Mock<ILodgingSearch>();
        var bookingsClientMock = new Mock<IBookingsClient>();

        // Both rooms tie at StayTotal = $240, but Room 104 < Room 205
        var offers = new List<RoomOffer>
        {
            new(RoomId: 205, HotelId: 2, HotelName: "Hotel B", RoomTypeName: "Deluxe", District: "Cercado de Lima", NightlyPrice: 120m, StayTotal: 240m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 104, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "Standard", District: "Cercado de Lima", NightlyPrice: 120m, StayTotal: 240m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: [])
        };

        lodgingSearchMock.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LodgingSearchResult(offers, AvailabilityServiceAvailable: true, BudgetExceeded: false, 240m, 2));

        bookingsClientMock.Setup(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 104), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteRequestResult(true, new BookingQuoteResponse("qte_104", 104, 240m, "USD", DateTime.UtcNow.AddMinutes(2)), null));

        var evaluator = new CandidateEvaluator(lodgingSearchMock.Object, bookingsClientMock.Object, NullLogger<CandidateEvaluator>.Instance);

        var result = await evaluator.EvaluateCandidatesAsync(CreateTestIntent());

        result.Success.Should().BeTrue();
        result.SelectedCandidate!.RoomId.Should().Be(104, "When prices tie, lower RoomId 104 must break the tie over 205");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F17")]
    public async Task Falls_Back_To_Next_Candidate_When_First_Quote_Fails()
    {
        var lodgingSearchMock = new Mock<ILodgingSearch>();
        var bookingsClientMock = new Mock<IBookingsClient>();

        var offers = new List<RoomOffer>
        {
            new(RoomId: 101, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "Economy", District: "Cercado de Lima", NightlyPrice: 100m, StayTotal: 200m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 102, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "Standard", District: "Cercado de Lima", NightlyPrice: 120m, StayTotal: 240m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: [])
        };

        lodgingSearchMock.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LodgingSearchResult(offers, AvailabilityServiceAvailable: true, BudgetExceeded: false, 200m, 2));

        // First candidate quote fails
        bookingsClientMock.Setup(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 101), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteRequestResult(false, null, "Conflict on requested dates"));

        // Second candidate quote succeeds
        bookingsClientMock.Setup(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 102), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteRequestResult(true, new BookingQuoteResponse("qte_102", 102, 240m, "USD", DateTime.UtcNow.AddMinutes(2)), null));

        var evaluator = new CandidateEvaluator(lodgingSearchMock.Object, bookingsClientMock.Object, NullLogger<CandidateEvaluator>.Instance);

        var result = await evaluator.EvaluateCandidatesAsync(CreateTestIntent());

        result.Success.Should().BeTrue();
        result.SelectedCandidate!.RoomId.Should().Be(102, "Must fall back to candidate 2 when candidate 1 quote fails");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F17")]
    public async Task Immediate_Failure_When_Zero_Candidates_Within_Budget()
    {
        var lodgingSearchMock = new Mock<ILodgingSearch>();
        var bookingsClientMock = new Mock<IBookingsClient>();

        lodgingSearchMock.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LodgingSearchResult([], AvailabilityServiceAvailable: true, BudgetExceeded: true, 400m, 5));

        var evaluator = new CandidateEvaluator(lodgingSearchMock.Object, bookingsClientMock.Object, NullLogger<CandidateEvaluator>.Instance);

        var result = await evaluator.EvaluateCandidatesAsync(CreateTestIntent(budget: 100m));

        result.Success.Should().BeFalse();
        result.ImmediateBudgetFailure.Should().BeTrue();
        result.FailureReason.Should().Be("No available rooms within the specified budget.");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F17")]
    public async Task Evaluates_At_Most_3_Candidates_And_Never_Candidate_4()
    {
        var lodgingSearchMock = new Mock<ILodgingSearch>();
        var bookingsClientMock = new Mock<IBookingsClient>();

        var offers = new List<RoomOffer>
        {
            new(RoomId: 1, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "R1", District: "Cercado de Lima", NightlyPrice: 50m, StayTotal: 100m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 2, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "R2", District: "Cercado de Lima", NightlyPrice: 60m, StayTotal: 120m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 3, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "R3", District: "Cercado de Lima", NightlyPrice: 70m, StayTotal: 140m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: []),
            new(RoomId: 4, HotelId: 1, HotelName: "Hotel A", RoomTypeName: "R4", District: "Cercado de Lima", NightlyPrice: 80m, StayTotal: 160m, Currency: "USD", Capacity: 2, TourPath: null, Amenities: [])
        };

        lodgingSearchMock.Setup(s => s.SearchAsync(It.IsAny<StayPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LodgingSearchResult(offers, AvailabilityServiceAvailable: true, BudgetExceeded: false, 100m, 4));

        // All candidate quotes fail
        bookingsClientMock.Setup(b => b.RequestQuoteAsync(It.IsAny<CreateBookingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuoteRequestResult(false, null, "Room unavailable"));

        var evaluator = new CandidateEvaluator(lodgingSearchMock.Object, bookingsClientMock.Object, NullLogger<CandidateEvaluator>.Instance);

        var result = await evaluator.EvaluateCandidatesAsync(CreateTestIntent());

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("All eligible room candidates (up to 3) are unavailable or quotes expired.");

        // SPEC REQUIREMENT: Candidate 4 is NEVER evaluated!
        bookingsClientMock.Verify(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 1), It.IsAny<CancellationToken>()), Times.Once);
        bookingsClientMock.Verify(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 2), It.IsAny<CancellationToken>()), Times.Once);
        bookingsClientMock.Verify(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 3), It.IsAny<CancellationToken>()), Times.Once);
        bookingsClientMock.Verify(b => b.RequestQuoteAsync(It.Is<CreateBookingQuoteRequest>(r => r.RoomId == 4), It.IsAny<CancellationToken>()), Times.Never);
    }
}
