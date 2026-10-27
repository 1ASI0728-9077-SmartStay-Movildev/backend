using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Contracts.Http;
using SmartStay.Payments.Application.Internal.CommandServices;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Infrastructure.Clients;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Payments.Tests;

public class PaymentIdempotencyAndOutboxTests
{
    private PaymentsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PaymentsDbContext(options);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Outbox")]
    public async Task Successful_Payment_Creates_Completed_Payment_And_Outbox_Message()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(50, 10, 1, 300.00m, "USD", "PendingPayment", true));

        var service = new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object);

        var cmd = new ProcessPaymentCommand(
            BookingId: 50,
            Amount: 300.00m,
            PaymentMethod: "CreditCard",
            CardNumber: "4111111111111234",
            CardHolderName: "Carlos Santana",
            ExpirationDate: "12/28",
            Cvv: "123",
            IdempotencyKey: "key-12345",
            ActorId: 1
        );

        // Act
        var (payment, isDuplicate, conflictReason) = await service.Handle(cmd);

        // Assert
        payment.Should().NotBeNull();
        isDuplicate.Should().BeFalse();
        conflictReason.Should().BeNull();
        payment!.Status.Should().Be(PaymentStatus.Completed);
        payment.Amount.Should().Be(300.00m);
        payment.CardNumberMasked.Should().Be("**** **** **** 1234");

        // Verify OutboxMessage
        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("payments.payment-completed");
        outbox.AggregateId.Should().Be(payment.Id.ToString());
        outbox.PublishedAtUtc.Should().BeNull(); // Pending publish by worker
        outbox.Payload.Should().Contain("\"bookingId\":50");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Idempotency")]
    public async Task Idempotent_Replay_Returns_Existing_Payment_Without_Duplicating_Records()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(50, 10, 1, 300.00m, "USD", "PendingPayment", true));

        var service = new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object);

        var cmd = new ProcessPaymentCommand(
            BookingId: 50,
            Amount: 300.00m,
            PaymentMethod: "CreditCard",
            CardNumber: "4111111111111234",
            CardHolderName: "Carlos Santana",
            ExpirationDate: "12/28",
            Cvv: "123",
            IdempotencyKey: "key-unique-replay",
            ActorId: 1
        );

        // First call
        var (payment1, isDuplicate1, _) = await service.Handle(cmd);
        payment1.Should().NotBeNull();
        isDuplicate1.Should().BeFalse();

        // Second call with identical payload & key
        var (payment2, isDuplicate2, conflictReason2) = await service.Handle(cmd);
        payment2.Should().NotBeNull();
        isDuplicate2.Should().BeTrue();
        conflictReason2.Should().BeNull();
        payment2!.Id.Should().Be(payment1!.Id);

        // Total payments in database must be exactly 1
        (await context.Payments.CountAsync()).Should().Be(1);
        (await context.OutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Idempotency")]
    public async Task Idempotent_Key_Collision_With_Different_Payload_Returns_Conflict()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(50, 10, 1, 300.00m, "USD", "PendingPayment", true));

        var service = new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object);

        var cmd1 = new ProcessPaymentCommand(50, 300.00m, "CreditCard", "4111111111111234", "Carlos", "12/28", "123", "reused-key", 1);
        await service.Handle(cmd1);

        // Different amount with SAME idempotency key
        var cmd2 = new ProcessPaymentCommand(50, 450.00m, "CreditCard", "4111111111111234", "Carlos", "12/28", "123", "reused-key", 1);
        var (payment2, isDuplicate2, conflictReason2) = await service.Handle(cmd2);

        payment2.Should().BeNull();
        isDuplicate2.Should().BeFalse();
        conflictReason2.Should().Contain("Idempotency key already used for a different request payload");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "BusinessRules")]
    public async Task Ineligible_Booking_Payment_Is_Rejected()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(99, 10, 1, 100m, "USD", "Cancelled", false));

        var service = new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object);

        var cmd = new ProcessPaymentCommand(99, 100m, "CreditCard", "4111111111111234", "Carlos", "12/28", "123", "key-ineligible", 1);
        var (payment, _, conflictReason) = await service.Handle(cmd);

        payment.Should().BeNull();
        conflictReason.Should().Contain("not eligible for payment");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Simulation")]
    public async Task Declined_Card_Ends_With_0000_Marks_Failed_And_Emits_Failed_Outbox()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(60, 10, 1, 200m, "USD", "PendingPayment", true));

        var service = new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object);

        var cmd = new ProcessPaymentCommand(60, 200m, "CreditCard", "4111111111110000", "Carlos", "12/28", "123", "key-fail", 1);
        var (payment, _, _) = await service.Handle(cmd);

        payment.Should().NotBeNull();
        payment!.Status.Should().Be(PaymentStatus.Failed);

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("payments.payment-failed");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Compensation")]
    public async Task Refund_Payment_Sets_Status_Refunded_And_Queues_Outbox()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(70, 10, 1, 150m, "USD", "PendingPayment", true));

        var service = new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object);

        var cmd = new ProcessPaymentCommand(70, 150m, "CreditCard", "4111111111111234", "Carlos", "12/28", "123", "key-refund", 1);
        var (payment, _, _) = await service.Handle(cmd);
        payment.Should().NotBeNull();

        // Act - Refund payment
        var result = await service.Handle(new RefundPaymentCommand(payment!.Id, "Booking cancelled late"));

        // Assert
        result.Should().BeTrue();
        var updated = await repo.FindByIdAsync(payment.Id);
        updated!.Status.Should().Be(PaymentStatus.Refunded);

        var refundOutbox = await context.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == "payments.payment-refunded");
        refundOutbox.Should().NotBeNull();
    }
}
