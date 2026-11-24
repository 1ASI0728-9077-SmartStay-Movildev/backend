using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartStay.Concierge.Application;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Concierge.Infrastructure.Persistence;
using SmartStay.Concierge.Infrastructure.Workers;
using SmartStay.Contracts.Http;
using Xunit;

namespace SmartStay.Concierge.Tests;

public class ConciergePurchaseIntentWorkerTests
{
    private static ConciergeDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ConciergeDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConciergeTestDb_{Guid.NewGuid():N}")
            .Options;
        return new ConciergeDbContext(options);
    }

    private static PurchaseIntent CreateTestIntent(string id = "cpi_worker_1", PurchaseIntentStatus status = PurchaseIntentStatus.Pending)
    {
        var intent = new PurchaseIntent(
            id: id,
            guestUserId: 10,
            idempotencyKey: $"key-{id}",
            demoPaymentMethodId: "dpm_sample_card",
            checkIn: new DateOnly(2026, 12, 1),
            checkOut: new DateOnly(2026, 12, 3),
            guests: 2,
            district: "Miraflores",
            budgetTotal: 500m,
            currency: "USD",
            contactName: "John Guest",
            contactEmail: "john@example.com",
            contactPhone: "+51999888777",
            cancellationPolicyAccepted: true
        )
        {
            Status = status
        };

        return intent;
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_Pending_Advances_To_Reserving_When_Candidate_Selected()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent();
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        var selectedCandidate = new RoomOffer(
            RoomId: 101,
            HotelId: 1,
            HotelName: "Hotel Miraflores",
            RoomTypeName: "Superior Suite",
            District: "Miraflores",
            NightlyPrice: 125m,
            StayTotal: 250m,
            Currency: "USD",
            Capacity: 2,
            TourPath: null,
            Amenities: []
        );
        var quote = new BookingQuoteResponse("qte_101", 101, 250m, "USD", DateTime.UtcNow.AddMinutes(2));

        candidateEvaluatorMock.Setup(e => e.EvaluateCandidatesAsync(intent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateEvaluationResult.Succeeded(selectedCandidate, quote));

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Reserving);
        intent.SelectedRoomId.Should().Be(101);
        intent.QuoteId.Should().Be("qte_101");
        intent.StayTotal.Should().Be(250m);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_Reserving_QueryBeforeRetry_Recovers_Existing_Booking_Without_Duplicate_Create()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(status: PurchaseIntentStatus.Reserving);
        intent.SelectedRoomId = 101;
        intent.QuoteId = "qte_101";
        intent.QuoteExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        // Query-before-retry: simulated crash recovery where booking was already created
        bookingsClientMock.Setup(b => b.GetBookingByIntentIdAsync(intent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingByIntentResponse(888, 101, 1, 10, 250m, "USD", "Confirmed", null, null, DateTime.UtcNow));

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.AwaitingConfirmation);
        intent.BookingId.Should().Be(888);

        // Crucial verification: CreateBookingAsync was NEVER called because query-before-retry found it!
        bookingsClientMock.Verify(b => b.CreateBookingAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_Reserving_Creates_Booking_With_Stable_Idempotency_Key()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(id: "cpi_stable_key_test", status: PurchaseIntentStatus.Reserving);
        intent.SelectedRoomId = 101;
        intent.QuoteId = "qte_101";
        intent.QuoteExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        bookingsClientMock.Setup(b => b.GetBookingByIntentIdAsync(intent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingByIntentResponse?)null);

        bookingsClientMock.Setup(b => b.CreateBookingAsync(
            101,
            intent.ContactName,
            intent.ContactEmail,
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            "qte_101",
            intent.Id,
            intent.GuestUserId,
            "concierge-booking-cpi_stable_key_test",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingReservationResult(true, 501, false, null));

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.AwaitingConfirmation);
        intent.BookingId.Should().Be(501);

        // Verify the stable idempotency key format: concierge-booking-{intentId}
        bookingsClientMock.Verify(b => b.CreateBookingAsync(
            101,
            intent.ContactName,
            intent.ContactEmail,
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            "qte_101",
            intent.Id,
            intent.GuestUserId,
            "concierge-booking-cpi_stable_key_test",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_AwaitingConfirmation_QueryBeforeRetry_Recovers_Existing_Payment()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(status: PurchaseIntentStatus.AwaitingConfirmation);
        intent.BookingId = 501;
        intent.StayTotal = 250m;
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        // Existing completed payment found
        paymentsClientMock.Setup(p => p.GetPaymentByBookingIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLookupResult(901, 501, "Completed", 250m, "VCH-000901"));

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Confirmed);
        intent.PaymentId.Should().Be(901);
        intent.ReceiptVoucher.Should().Be("VCH-000901");

        // Verify no duplicate payment was submitted
        paymentsClientMock.Verify(p => p.ProcessDemoPaymentAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_AwaitingConfirmation_Processes_Payment_With_Stable_Idempotency_Key()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(id: "cpi_pay_key_test", status: PurchaseIntentStatus.AwaitingConfirmation);
        intent.BookingId = 501;
        intent.StayTotal = 250m;
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        paymentsClientMock.Setup(p => p.GetPaymentByBookingIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentLookupResult?)null);

        paymentsClientMock.Setup(p => p.ProcessDemoPaymentAsync(
            501,
            250m,
            "USD",
            "dpm_sample_card",
            intent.GuestUserId,
            "concierge-payment-cpi_pay_key_test",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentExecutionResult(true, 777, "VCH-000777", false, null));

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Confirmed);
        intent.PaymentId.Should().Be(777);
        intent.ReceiptVoucher.Should().Be("VCH-000777");

        // Verify the stable idempotency key format: concierge-payment-{intentId}
        paymentsClientMock.Verify(p => p.ProcessDemoPaymentAsync(
            501,
            250m,
            "USD",
            "dpm_sample_card",
            intent.GuestUserId,
            "concierge-payment-cpi_pay_key_test",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_AwaitingConfirmation_Releases_Night_Locks_When_Payment_Rejected()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(status: PurchaseIntentStatus.AwaitingConfirmation);
        intent.BookingId = 501;
        intent.StayTotal = 250m;
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        paymentsClientMock.Setup(p => p.GetPaymentByBookingIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentLookupResult?)null);

        paymentsClientMock.Setup(p => p.ProcessDemoPaymentAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentExecutionResult(false, null, null, false, "Insufficient card balance for simulator transaction."));

        bookingsClientMock.Setup(b => b.CancelBookingAsync(501, It.IsAny<string>(), intent.GuestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Failed);
        intent.FailureReason.Should().Contain("Insufficient card balance");

        // Crucial requirement: booking MUST be cancelled to release physical night locks
        bookingsClientMock.Verify(b => b.CancelBookingAsync(501, "Payment authorization rejected", intent.GuestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_Mandate_Expired_Cancels_Booking_And_Transitions_To_Expired()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(status: PurchaseIntentStatus.Reserving);
        intent.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1); // Expired mandate!
        intent.BookingId = 333;
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        bookingsClientMock.Setup(b => b.CancelBookingAsync(333, It.IsAny<string>(), intent.GuestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Expired);
        intent.FailureReason.Should().Contain("Autonomous purchase mandate expired");

        // Mandate expiry MUST cancel booking to free up room night locks
        bookingsClientMock.Verify(b => b.CancelBookingAsync(333, "Mandate expired after 15 minutes", intent.GuestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    public async Task Worker_Paid_Cancellation_Awaits_Refund_Confirmation_Before_Cancelled()
    {
        using var db = CreateInMemoryDbContext();
        var intent = CreateTestIntent(status: PurchaseIntentStatus.Cancelling);
        intent.BookingId = 333;
        intent.PaymentId = 777;
        db.PurchaseIntents.Add(intent);
        await db.SaveChangesAsync();

        var bookingsClientMock = new Mock<IBookingsClient>();
        var paymentsClientMock = new Mock<IPaymentsClient>();
        var candidateEvaluatorMock = new Mock<ICandidateEvaluator>();

        bookingsClientMock.Setup(b => b.CancelBookingAsync(333, It.IsAny<string>(), intent.GuestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Step 1: Refund not yet confirmed -> remains Cancelling
        paymentsClientMock.Setup(p => p.GetPaymentByBookingIdAsync(333, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLookupResult(777, 333, "Completed", 250m, "VCH-000777"));

        var worker = new ConciergePurchaseIntentWorker(
            Mock.Of<IServiceScopeFactory>(),
            bookingsClientMock.Object,
            paymentsClientMock.Object,
            candidateEvaluatorMock.Object,
            NullLogger<ConciergePurchaseIntentWorker>.Instance);

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Cancelling, "Must remain in Cancelling while refund is not completed");
        intent.NextRetryAtUtc.Should().NotBeNull();

        // Step 2: Payments confirms refund -> transitions to Cancelled
        paymentsClientMock.Setup(p => p.GetPaymentByBookingIdAsync(333, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLookupResult(777, 333, "Refunded", 250m, "VCH-000777"));

        await worker.ProcessIntentAsync(db, intent, CancellationToken.None);

        intent.Status.Should().Be(PurchaseIntentStatus.Cancelled, "Must transition to Cancelled once payment is Refunded");
    }

    [Theory]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F18")]
    [InlineData(1, 1)]  // 1s
    [InlineData(2, 2)]  // 2s
    [InlineData(3, 4)]  // 4s
    [InlineData(4, 30)] // 30s periodic reconciliation
    [InlineData(5, 30)] // 30s periodic reconciliation
    public void Transient_Retry_Schedule_Matches_Specification(int retryAttempt, int expectedDelaySeconds)
    {
        var intent = CreateTestIntent();
        intent.RetryCount = retryAttempt - 1;

        var before = DateTime.UtcNow;
        ConciergePurchaseIntentWorker.ScheduleTransientRetry(intent);

        intent.RetryCount.Should().Be(retryAttempt);
        intent.NextRetryAtUtc.Should().NotBeNull();

        var actualDelay = (intent.NextRetryAtUtc!.Value - before).TotalSeconds;
        actualDelay.Should().BeInRange(expectedDelaySeconds - 0.5, expectedDelaySeconds + 0.5);
    }
}
