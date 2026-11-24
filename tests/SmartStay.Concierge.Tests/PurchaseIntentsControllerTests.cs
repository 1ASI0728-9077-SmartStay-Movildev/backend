using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Concierge.Infrastructure.Persistence;
using SmartStay.Concierge.Infrastructure.Security;
using SmartStay.Concierge.Interfaces.REST;
using SmartStay.Concierge.Interfaces.REST.Resources;
using SmartStay.Contracts.Http;
using Xunit;

namespace SmartStay.Concierge.Tests;

public class PurchaseIntentsControllerTests
{
    private static ConciergeDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ConciergeDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConciergeControllerTestDb_{Guid.NewGuid():N}")
            .Options;
        return new ConciergeDbContext(options);
    }

    private static PurchaseIntentsController CreateController(
        ConciergeDbContext db,
        Mock<IPaymentsValidationClient> paymentsValidationMock,
        Mock<IBookingsClient> bookingsMock,
        int actorUserId = 10,
        string role = "guest")
    {
        var controller = new PurchaseIntentsController(
            db,
            paymentsValidationMock.Object,
            bookingsMock.Object,
            NullLogger<PurchaseIntentsController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["CurrentActor"] = new CurrentActor(actorUserId, "testuser", role, null, null);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    private static CreatePurchaseIntentResource ValidResource(
        bool cancellationAccepted = true,
        string demoCardId = "dpm_valid_card")
    {
        return new CreatePurchaseIntentResource(
            CheckIn: "2026-12-01",
            CheckOut: "2026-12-03",
            District: "Miraflores",
            Guests: 2,
            BudgetTotal: 500m,
            Currency: "USD",
            ContactName: "John Guest",
            ContactEmail: "john@example.com",
            ContactPhone: "+51999888777",
            CancellationPolicyAccepted: cancellationAccepted,
            DemoPaymentMethodId: demoCardId
        );
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Create_Without_CancellationPolicy_Returns_400BadRequest()
    {
        using var db = CreateInMemoryDbContext();
        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();
        var controller = CreateController(db, paymentsValidationMock, bookingsMock);

        var result = await controller.CreatePurchaseIntent(ValidResource(cancellationAccepted: false));

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        db.PurchaseIntents.Should().BeEmpty("0 records must be created when cancellation policy is rejected");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Create_Without_DemoPaymentMethodId_Returns_422UnprocessableEntity()
    {
        using var db = CreateInMemoryDbContext();
        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();
        var controller = CreateController(db, paymentsValidationMock, bookingsMock);

        var result = await controller.CreatePurchaseIntent(ValidResource(demoCardId: ""));

        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        db.PurchaseIntents.Should().BeEmpty();
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Create_With_Invalid_DemoCard_Returns_422_And_Creates_Zero_Records()
    {
        using var db = CreateInMemoryDbContext();
        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();

        paymentsValidationMock.Setup(p => p.ValidateDemoPaymentMethodAsync("dpm_unowned", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateDemoPaymentMethodResponse(false, Reason: "Card not owned by guest"));

        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 10);

        var result = await controller.CreatePurchaseIntent(ValidResource(demoCardId: "dpm_unowned"));

        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        // Verification: 0 records inserted, flow stopped before creating intent
        db.PurchaseIntents.Should().BeEmpty("0 records must be stored when card validation fails");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Create_With_Valid_Parameters_Returns_202Accepted_And_Sets_15Minute_TTL()
    {
        using var db = CreateInMemoryDbContext();
        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();

        paymentsValidationMock.Setup(p => p.ValidateDemoPaymentMethodAsync("dpm_valid_card", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateDemoPaymentMethodResponse(true, Status: "Active", Balance: 1000m, Currency: "USD"));

        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 10);

        var result = await controller.CreatePurchaseIntent(ValidResource());

        var acceptedResult = result.Should().BeOfType<AcceptedResult>().Subject;
        acceptedResult.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var tracking = acceptedResult.Value.Should().BeOfType<PurchaseIntentTrackingResource>().Subject;
        tracking.Status.Should().Be("Pending");
        tracking.GuestUserId.Should().Be(10);
        (tracking.ExpiresAtUtc - tracking.CreatedAtUtc).TotalMinutes.Should().BeApproximately(15, 0.5);

        db.PurchaseIntents.Should().HaveCount(1);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Create_With_Same_IdempotencyKey_Returns_Existing_202()
    {
        using var db = CreateInMemoryDbContext();
        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();

        paymentsValidationMock.Setup(p => p.ValidateDemoPaymentMethodAsync("dpm_valid_card", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateDemoPaymentMethodResponse(true, Status: "Active", Balance: 1000m, Currency: "USD"));

        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 10);
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "idem-key-999";

        // First call
        var result1 = await controller.CreatePurchaseIntent(ValidResource());
        var accepted1 = result1.Should().BeOfType<AcceptedResult>().Subject;
        var tracking1 = accepted1.Value.Should().BeOfType<PurchaseIntentTrackingResource>().Subject;

        // Second call with same key
        var result2 = await controller.CreatePurchaseIntent(ValidResource());
        var accepted2 = result2.Should().BeOfType<AcceptedResult>().Subject;
        var tracking2 = accepted2.Value.Should().BeOfType<PurchaseIntentTrackingResource>().Subject;

        tracking2.Id.Should().Be(tracking1.Id, "Replay with identical idempotency key must return the existing intent");
        db.PurchaseIntents.Should().HaveCount(1, "No duplicate records must be inserted");
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task GetById_By_Different_Guest_Returns_403Forbidden()
    {
        using var db = CreateInMemoryDbContext();
        var intent = new PurchaseIntent("cpi_guest_10", 10, "key1", "dpm_1", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), 2, "Miraflores", 500m, "USD", "Guest", "g@e.com", "123", true);
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();

        // Actor is user 99 (not owner, not admin)
        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 99, role: "guest");

        var result = await controller.GetPurchaseIntentById("cpi_guest_10");

        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Cancel_By_Different_Guest_Returns_403Forbidden()
    {
        using var db = CreateInMemoryDbContext();
        var intent = new PurchaseIntent("cpi_guest_10", 10, "key1", "dpm_1", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), 2, "Miraflores", 500m, "USD", "Guest", "g@e.com", "123", true);
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();

        // Actor is user 99
        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 99, role: "guest");

        var result = await controller.CancelPurchaseIntent("cpi_guest_10");

        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Cancel_Unpaid_Intent_Cancels_Booking_And_Returns_200OK_With_Cancelled_Status()
    {
        using var db = CreateInMemoryDbContext();
        var intent = new PurchaseIntent("cpi_unpaid", 10, "key1", "dpm_1", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), 2, "Miraflores", 500m, "USD", "Guest", "g@e.com", "123", true)
        {
            BookingId = 404,
            Status = PurchaseIntentStatus.Reserving
        };
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();
        bookingsMock.Setup(b => b.CancelBookingAsync(404, It.IsAny<string>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 10);

        var result = await controller.CancelPurchaseIntent("cpi_unpaid");

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var tracking = okResult.Value.Should().BeOfType<PurchaseIntentTrackingResource>().Subject;
        tracking.Status.Should().Be("Cancelled");

        // Verify that cancel booking was called to release physical locks
        bookingsMock.Verify(b => b.CancelBookingAsync(404, "Cancelled by user before payment", 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task Cancel_Paid_Intent_Initiates_Refund_And_Returns_202Accepted_With_Cancelling_Status()
    {
        using var db = CreateInMemoryDbContext();
        var intent = new PurchaseIntent("cpi_paid", 10, "key1", "dpm_1", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), 2, "Miraflores", 500m, "USD", "Guest", "g@e.com", "123", true)
        {
            BookingId = 404,
            PaymentId = 707,
            Status = PurchaseIntentStatus.Confirmed
        };
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var paymentsValidationMock = new Mock<IPaymentsValidationClient>();
        var bookingsMock = new Mock<IBookingsClient>();
        bookingsMock.Setup(b => b.CancelBookingAsync(404, It.IsAny<string>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(db, paymentsValidationMock, bookingsMock, actorUserId: 10);

        var result = await controller.CancelPurchaseIntent("cpi_paid");

        var acceptedResult = result.Should().BeOfType<AcceptedResult>().Subject;
        acceptedResult.StatusCode.Should().Be(StatusCodes.Status202Accepted);

        var tracking = acceptedResult.Value.Should().BeOfType<PurchaseIntentTrackingResource>().Subject;
        tracking.Status.Should().Be("Cancelling");

        // Verify booking cancellation is requested
        bookingsMock.Verify(b => b.CancelBookingAsync(404, "Cancelled by user (Full refund demo policy)", 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
