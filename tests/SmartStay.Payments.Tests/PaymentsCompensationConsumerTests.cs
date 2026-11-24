using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartStay.Contracts.Http;
using SmartStay.Payments.Application.Internal.CommandServices;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Entities;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Infrastructure.Clients;
using SmartStay.Payments.Infrastructure.Messaging;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Payments.Tests;

public class PaymentsCompensationConsumerTests
{
    private (IServiceProvider Provider, PaymentsDbContext Context, PaymentsCompensationConsumerWorker Worker) CreateTestRig()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<PaymentsDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName)
               .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentsUnitOfWork, PaymentsUnitOfWork>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        services.AddSingleton(mockEligibility.Object);

        var mockCommandLogger = new Mock<ILogger<PaymentCommandService>>();
        services.AddSingleton(mockCommandLogger.Object);

        services.AddScoped<IPaymentCommandService, PaymentCommandService>();

        var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<PaymentsDbContext>();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var mockConfig = new Mock<IConfiguration>();
        var mockWorkerLogger = new Mock<ILogger<PaymentsCompensationConsumerWorker>>();

        var worker = new PaymentsCompensationConsumerWorker(scopeFactory, mockConfig.Object, mockWorkerLogger.Object);
        return (provider, context, worker);
    }

    private static string CreateRefundRequestedJson(Guid eventId, int paymentId, int bookingId, string reason = "Booking cancelled")
    {
        var payload = new
        {
            eventId = eventId,
            eventType = "bookings.payment-refund-requested",
            data = new
            {
                paymentId = paymentId,
                bookingId = bookingId,
                reason = reason,
                occurredAtUtc = DateTime.UtcNow
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Compensation")]
    public async Task ProcessCompensationEvent_Refunds_Payment_And_Enqueues_Outbox_And_Marks_Inbox_In_Single_Transaction()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();

        var payment = new Payment(bookingId: 45, amount: 300m, paymentMethod: "CreditCard",
            cardHolderName: "Carlos Santana", cardNumber: "4111111111111234");
        payment.Complete();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var json = CreateRefundRequestedJson(eventId, paymentId: payment.Id, bookingId: 45, reason: "Customer requested cancellation.");

        // Act
        await worker.ProcessCompensationEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(PaymentStatus.Refunded);

        // Verify outbox
        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("payments.payment-refunded");
        outbox.Payload.Should().Contain($"\"paymentId\":{payment.Id}");

        // Verify inbox
        var inbox = await context.InboxMessages.FirstOrDefaultAsync(m => m.EventId == eventId);
        inbox.Should().NotBeNull();
        inbox!.Status.Should().Be("Processed");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Compensation")]
    public async Task ProcessCompensationEvent_Fallback_To_BookingId_When_PaymentId_Is_Zero()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();

        var payment = new Payment(bookingId: 88, amount: 250m, paymentMethod: "CreditCard",
            cardHolderName: "Ana Gomez", cardNumber: "4111111111115678");
        payment.Complete();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        // PaymentId is 0, but bookingId is 88
        var json = CreateRefundRequestedJson(eventId, paymentId: 0, bookingId: 88, reason: "Refund by booking reference.");

        // Act
        await worker.ProcessCompensationEventAsync(json, CancellationToken.None);

        // Assert
        var updated = await context.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        updated!.Status.Should().Be(PaymentStatus.Refunded);

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("payments.payment-refunded");
        outbox.Payload.Should().Contain($"\"paymentId\":{payment.Id}");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Compensation")]
    public async Task ProcessCompensationEvent_Duplicate_EventId_Is_Idempotent()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();

        var payment = new Payment(bookingId: 99, amount: 400m, paymentMethod: "CreditCard",
            cardHolderName: "Maria Lopez", cardNumber: "4111111111119999");
        payment.Complete();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var json = CreateRefundRequestedJson(eventId, paymentId: payment.Id, bookingId: 99);

        // First pass
        await worker.ProcessCompensationEventAsync(json, CancellationToken.None);
        var initialOutboxCount = await context.OutboxMessages.CountAsync();
        initialOutboxCount.Should().Be(1);

        // Second pass with same eventId
        await worker.ProcessCompensationEventAsync(json, CancellationToken.None);

        // Assert no duplicate outbox event
        var finalOutboxCount = await context.OutboxMessages.CountAsync();
        finalOutboxCount.Should().Be(1);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "TransactionAware")]
    public async Task PaymentCommandService_Refund_Respects_Ambient_Transaction()
    {
        // Arrange
        var (provider, context, _) = CreateTestRig();
        var commandService = provider.GetRequiredService<IPaymentCommandService>();
        var uow = provider.GetRequiredService<IPaymentsUnitOfWork>();

        var payment = new Payment(bookingId: 105, amount: 180m, paymentMethod: "CreditCard",
            cardHolderName: "Pedro Pascal", cardNumber: "4111111111114321");
        payment.Complete();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        // Open ambient transaction
        await using var tx = await uow.BeginTransactionAsync();

        // Act
        var result = await commandService.Handle(new RefundPaymentCommand(payment.Id, "Testing ambient tx."));
        await uow.CompleteAsync();
        await tx.CommitAsync();

        // Assert
        result.Should().BeTrue();
        var updated = await context.Payments.FindAsync(payment.Id);
        updated!.Status.Should().Be(PaymentStatus.Refunded);

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync(m => m.AggregateId == payment.Id.ToString());
        outbox.Should().NotBeNull();
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Category", "Compensation")]
    [Trait("Feature", "12")]
    public async Task ProcessCompensationEvent_DemoCard_Restores_Balance_And_Records_Refund_Ledger()
    {
        // Arrange
        var (_, context, worker) = CreateTestRig();

        var demoMethod = new DemoPaymentMethod(guestUserId: 33);
        demoMethod.Debit(300m);
        context.DemoPaymentMethods.Add(demoMethod);

        var payment = new Payment(
            bookingId: 110,
            amount: 300m,
            paymentMethod: "DemoCard",
            cardHolderName: "Demo Guest",
            cardNumber: string.Empty,
            demoPaymentMethodId: demoMethod.Id);
        payment.Complete();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        var debitMovement = new PaymentLedgerMovement(demoMethod.Id, payment.Id, "Debit", 300m, 999_700.00m);
        context.PaymentLedgerMovements.Add(debitMovement);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var json = CreateRefundRequestedJson(eventId, paymentId: payment.Id, bookingId: 110, reason: "Demo compensation refund");

        // Act
        await worker.ProcessCompensationEventAsync(json, CancellationToken.None);

        // Assert
        var updatedPayment = await context.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id);
        updatedPayment.Should().NotBeNull();
        updatedPayment!.Status.Should().Be(PaymentStatus.Refunded);

        var restoredMethod = await context.DemoPaymentMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == demoMethod.Id);
        restoredMethod.Should().NotBeNull();
        restoredMethod!.Balance.Should().Be(1_000_000.00m);

        var refundMovement = await context.PaymentLedgerMovements
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PaymentId == payment.Id && m.OperationType == "Refund");
        refundMovement.Should().NotBeNull();
        refundMovement!.Amount.Should().Be(300m);
        refundMovement.BalanceAfter.Should().Be(1_000_000.00m);

        var inbox = await context.InboxMessages.FirstOrDefaultAsync(m => m.EventId == eventId);
        inbox.Should().NotBeNull();
        inbox!.Status.Should().Be("Processed");

        var outbox = await context.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == "payments.payment-refunded");
        outbox.Should().NotBeNull();
    }
}
