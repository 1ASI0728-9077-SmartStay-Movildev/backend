using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Persistence;
using Xunit;

namespace SmartStay.Concierge.Tests;

public class PurchaseIntentStateMachineTests
{
    private static ConciergeDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ConciergeDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConciergeTestDb_{Guid.NewGuid()}")
            .Options;
        return new ConciergeDbContext(options);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public void PurchaseIntent_Creation_Has_Correct_Initial_State_And_15Minute_Ttl()
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));
        var checkOut = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(4));
        var intent = new PurchaseIntent(
            id: "cpi_test_01",
            guestUserId: 2,
            idempotencyKey: "key-123",
            demoPaymentMethodId: "dpm_sample",
            checkIn: checkIn,
            checkOut: checkOut,
            guests: 2,
            district: "Cercado de Lima",
            budgetTotal: 500m,
            currency: "USD",
            contactName: "Guest User",
            contactEmail: "guest@example.com",
            contactPhone: "+51999999999",
            cancellationPolicyAccepted: true,
            mandateTtlMinutes: 15
        );

        intent.Status.Should().Be(PurchaseIntentStatus.Pending);
        intent.IsMandateExpired.Should().BeFalse();
        (intent.ExpiresAtUtc - intent.CreatedAtUtc).TotalMinutes.Should().BeApproximately(15, 0.1);
        intent.StableBookingIdempotencyKey.Should().Be("concierge-booking-cpi_test_01");
        intent.StablePaymentIdempotencyKey.Should().Be("concierge-payment-cpi_test_01");
        intent.ConcurrencyToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public void PurchaseIntent_Transitions_Rotate_Concurrency_Token()
    {
        var intent = new PurchaseIntent(
            id: "cpi_test_02",
            guestUserId: 2,
            idempotencyKey: "key-456",
            demoPaymentMethodId: "dpm_sample",
            checkIn: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
            checkOut: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(3)),
            guests: 2,
            district: "San Blas",
            budgetTotal: 400m,
            currency: "USD",
            contactName: "Guest",
            contactEmail: "guest@example.com",
            contactPhone: "+51999999999",
            cancellationPolicyAccepted: true
        );

        var token1 = intent.ConcurrencyToken;

        // Transition to Reserving
        intent.TransitionToReserving("qte_1", DateTime.UtcNow.AddMinutes(2), 102, "Standard", "Hotel A", 200m);
        intent.Status.Should().Be(PurchaseIntentStatus.Reserving);
        intent.ConcurrencyToken.Should().NotBe(token1);
        var token2 = intent.ConcurrencyToken;

        // Transition to AwaitingConfirmation
        intent.TransitionToAwaitingConfirmation(55);
        intent.Status.Should().Be(PurchaseIntentStatus.AwaitingConfirmation);
        intent.BookingId.Should().Be(55);
        intent.ConcurrencyToken.Should().NotBe(token2);
        var token3 = intent.ConcurrencyToken;

        // Transition to Confirmed
        intent.TransitionToConfirmed(88, "VCH-000088");
        intent.Status.Should().Be(PurchaseIntentStatus.Confirmed);
        intent.PaymentId.Should().Be(88);
        intent.ReceiptVoucher.Should().Be("VCH-000088");
        intent.ConcurrencyToken.Should().NotBe(token3);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public void PurchaseIntent_PaidCancellation_Transitions_To_Cancelling_First()
    {
        var intent = new PurchaseIntent(
            id: "cpi_test_03",
            guestUserId: 2,
            idempotencyKey: "key-789",
            demoPaymentMethodId: "dpm_sample",
            checkIn: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
            checkOut: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(3)),
            guests: 2,
            district: "Cercado de Lima",
            budgetTotal: 300m,
            currency: "USD",
            contactName: "Guest",
            contactEmail: "guest@example.com",
            contactPhone: "+51999999999",
            cancellationPolicyAccepted: true
        );

        intent.TransitionToReserving("qte_1", DateTime.UtcNow.AddMinutes(2), 101, "Standard", "Hotel A", 200m);
        intent.TransitionToAwaitingConfirmation(10);
        intent.TransitionToConfirmed(20, "VCH-000020");

        intent.TransitionToCancelling();
        intent.Status.Should().Be(PurchaseIntentStatus.Cancelling);

        intent.TransitionToCancelled();
        intent.Status.Should().Be(PurchaseIntentStatus.Cancelled);
    }

    [Fact]
    [Trait("Service", "Concierge")]
    [Trait("Feature", "F16")]
    public async Task ConciergeDbContext_Can_Persist_And_Query_PurchaseIntent()
    {
        var db = CreateInMemoryDbContext();
        var intent = new PurchaseIntent(
            id: "cpi_db_persist",
            guestUserId: 5,
            idempotencyKey: "key-persist-01",
            demoPaymentMethodId: "dpm_db_01",
            checkIn: new DateOnly(2026, 12, 1),
            checkOut: new DateOnly(2026, 12, 5),
            guests: 2,
            district: "Cercado de Lima",
            budgetTotal: 400m,
            currency: "USD",
            contactName: "Test",
            contactEmail: "test@example.com",
            contactPhone: "+51987654321",
            cancellationPolicyAccepted: true
        );

        await db.PurchaseIntents.AddAsync(intent);
        await db.SaveChangesAsync();

        var retrieved = await db.PurchaseIntents.FirstOrDefaultAsync(p => p.Id == "cpi_db_persist");
        retrieved.Should().NotBeNull();
        retrieved!.GuestUserId.Should().Be(5);
        retrieved.Status.Should().Be(PurchaseIntentStatus.Pending);
        retrieved.BudgetTotal.Should().Be(400m);
    }
}
