using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmartStay.Contracts.Constants;
using SmartStay.Contracts.Http;
using SmartStay.Payments.Application.Internal.CommandServices;
using SmartStay.Payments.Application.Internal.QueryServices;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Queries;
using SmartStay.Payments.Infrastructure.Clients;
using SmartStay.Payments.Infrastructure.Configuration;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Repositories;
using SmartStay.Payments.Infrastructure.Security;
using SmartStay.Payments.Interfaces.REST;
using SmartStay.Payments.Interfaces.REST.Resources;
using Xunit;

namespace SmartStay.Payments.Tests;

public class DemoPaymentMethodTests
{
    private PaymentsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PaymentsDbContext(options);
    }

    private PaymentCommandService CreateCommandService(
        PaymentsDbContext context,
        bool enableDemoPayment = true,
        decimal bookingTotal = 300.00m,
        int bookingId = 50)
    {
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var mockLogger = new Mock<ILogger<PaymentCommandService>>();

        var mockEligibility = new Mock<IBookingEligibilityClient>();
        mockEligibility.Setup(c => c.CheckEligibilityAsync(bookingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookingEligibilityResponse(bookingId, 10, 1, bookingTotal, "USD", "PendingPayment", true));

        var options = Options.Create(new PaymentFeaturesOptions { EnableDemoPayment = enableDemoPayment });
        return new PaymentCommandService(repo, uow, mockEligibility.Object, mockLogger.Object, options);
    }

    // =========================================================================
    // FEATURE 8: DemoPaymentMethod Aggregate & Endpoints
    // =========================================================================

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "8")]
    public void DemoPaymentMethod_Aggregate_Initializes_With_Required_Properties_And_No_Card_Fields()
    {
        // Act
        var method = new DemoPaymentMethod(guestUserId: 42);

        // Assert
        method.Id.Should().StartWith("dpm_");
        method.Id.Length.Should().BeLessThanOrEqualTo(64);
        method.GuestUserId.Should().Be(42);
        method.Label.Should().Be("SmartStay Demo");
        method.Status.Should().Be("Active");
        method.Balance.Should().Be(1_000_000.00m);
        method.Currency.Should().Be("USD");
        method.ConcurrencyToken.Should().NotBeEmpty();
        method.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        method.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));

        // Verify strictly that NO sensitive card information properties exist on this class
        var propertyNames = typeof(DemoPaymentMethod).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        propertyNames.Should().NotContain("cardnumber");
        propertyNames.Should().NotContain("cvv");
        propertyNames.Should().NotContain("expirationdate");
        propertyNames.Should().NotContain("pan");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "8")]
    public async Task RequireDemoPaymentAttribute_Returns_403_When_Demo_Payment_Disabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new PaymentFeaturesOptions { EnableDemoPayment = false }));
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        var filter = new RequireDemoPaymentAttribute();
        var nextExecuted = false;

        // Act
        await filter.OnActionExecutionAsync(context, () =>
        {
            nextExecuted = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Assert
        nextExecuted.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result!;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "8")]
    public async Task PaymentsController_ActivateDemoPaymentMethod_Returns_Ok_With_Resource()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true);
        var queryService = new PaymentQueryService(new PaymentRepository(context));
        var controller = new PaymentsController(commandService, queryService);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["Actor"] = new CurrentActor(7, "guest_seven", UserRoles.Guest, 1);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await controller.ActivateDemoPaymentMethod();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var resource = okResult.Value.Should().BeOfType<DemoPaymentMethodResource>().Subject;
        resource.Id.Should().StartWith("dpm_");
        resource.GuestUserId.Should().Be(7);
        resource.Label.Should().Be("SmartStay Demo");
        resource.Status.Should().Be("Active");
        resource.Balance.Should().Be(1_000_000.00m);
        resource.Currency.Should().Be("USD");
    }

    // =========================================================================
    // FEATURE 9: Demo Payment Method Guest Isolation & Idempotency
    // =========================================================================

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "9")]
    public async Task Repeated_Activations_Return_Same_Method_And_Preserve_Existing_Balance()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true);

        // First activation
        var first = await commandService.Handle(new ActivateDemoPaymentMethodCommand(10));
        first.Balance.Should().Be(1_000_000.00m);

        // Simulate a debit of $300.00
        first.Debit(300.00m);
        context.DemoPaymentMethods.Update(first);
        await context.SaveChangesAsync();

        first.Balance.Should().Be(999_700.00m);

        // Second activation for the same guest
        var second = await commandService.Handle(new ActivateDemoPaymentMethodCommand(10));

        // Assert
        second.Id.Should().Be(first.Id);
        second.Balance.Should().Be(999_700.00m); // Balance must NOT be reset to $1M!
        (await context.DemoPaymentMethods.CountAsync(m => m.GuestUserId == 10)).Should().Be(1);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "9")]
    public async Task GetDemoPaymentMethod_Enforces_Guest_Isolation()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var uow = new PaymentsUnitOfWork(context);
        var queryService = new PaymentQueryService(repo);
        var commandService = CreateCommandService(context, enableDemoPayment: true);

        // Guest 1 creates a demo method
        var guest1Method = await commandService.Handle(new ActivateDemoPaymentMethodCommand(1));

        // Controller invocation as Guest 2
        var controller = new PaymentsController(commandService, queryService);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["Actor"] = new CurrentActor(2, "guest_two", UserRoles.Guest, 1);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act - Guest 2 queries demo method
        var result = await controller.GetDemoPaymentMethod();

        // Assert - Guest 2 receives 404 NotFound (cannot see Guest 1's method)
        result.Should().BeOfType<NotFoundObjectResult>();

        // Query directly for Guest 1 returns their own method
        httpContext.Items["Actor"] = new CurrentActor(1, "guest_one", UserRoles.Guest, 1);
        var guest1Result = await controller.GetDemoPaymentMethod();
        guest1Result.Should().BeOfType<OkObjectResult>();
        var guest1Resource = ((OkObjectResult)guest1Result).Value.Should().BeOfType<DemoPaymentMethodResource>().Subject;
        guest1Resource.Id.Should().Be(guest1Method.Id);
        guest1Resource.GuestUserId.Should().Be(1);
    }

    // =========================================================================
    // FEATURE 10: Payment Ledger & Single Atomic Transaction
    // =========================================================================

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "10")]
    public async Task ProcessPayment_DemoCard_Atomic_Debit_And_Ledger()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true, bookingTotal: 300.00m, bookingId: 50);

        var demoMethod = await commandService.Handle(new ActivateDemoPaymentMethodCommand(5));
        demoMethod.Balance.Should().Be(1_000_000.00m);

        var cmd = new ProcessPaymentCommand(
            BookingId: 50,
            Amount: 300.00m,
            PaymentMethod: "DemoCard",
            CardNumber: string.Empty,
            CardHolderName: "Demo Guest",
            ExpirationDate: string.Empty,
            Cvv: string.Empty,
            IdempotencyKey: "key-demo-50",
            ActorId: 5,
            DemoPaymentMethodId: demoMethod.Id
        );

        // Act
        var (payment, isDuplicate, conflictReason) = await commandService.Handle(cmd);

        // Assert
        payment.Should().NotBeNull();
        isDuplicate.Should().BeFalse();
        conflictReason.Should().BeNull();
        payment!.Status.Should().Be(PaymentStatus.Completed);
        payment.DemoPaymentMethodId.Should().Be(demoMethod.Id);

        // Check updated demo method balance
        var updatedMethod = await context.DemoPaymentMethods.FindAsync(demoMethod.Id);
        updatedMethod!.Balance.Should().Be(999_700.00m);

        // Verify exactly one Debit ledger movement
        var movements = await context.PaymentLedgerMovements
            .Where(m => m.DemoPaymentMethodId == demoMethod.Id)
            .ToListAsync();
        movements.Should().HaveCount(1);
        var movement = movements[0];
        movement.PaymentId.Should().Be(payment.Id);
        movement.OperationType.Should().Be("Debit");
        movement.Amount.Should().Be(300.00m);
        movement.BalanceAfter.Should().Be(999_700.00m);

        // Verify outbox event
        var outbox = await context.OutboxMessages.FirstOrDefaultAsync();
        outbox.Should().NotBeNull();
        outbox!.EventType.Should().Be("payments.payment-completed");
        outbox.AggregateId.Should().Be(payment.Id.ToString());
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "10")]
    public async Task ProcessPayment_DemoCard_Rejects_When_Feature_Flag_Disabled()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: false, bookingTotal: 300.00m, bookingId: 50);

        var cmd = new ProcessPaymentCommand(
            BookingId: 50,
            Amount: 300.00m,
            PaymentMethod: "DemoCard",
            CardNumber: string.Empty,
            CardHolderName: "Demo Guest",
            ExpirationDate: string.Empty,
            Cvv: string.Empty,
            IdempotencyKey: "key-demo-flag-off",
            ActorId: 5,
            DemoPaymentMethodId: "dpm_any"
        );

        // Act
        var (payment, isDuplicate, conflictReason) = await commandService.Handle(cmd);

        // Assert
        payment.Should().BeNull();
        isDuplicate.Should().BeFalse();
        conflictReason.Should().Contain("Demo payment feature is disabled");
    }

    // =========================================================================
    // FEATURE 11: Balance Overdraft Prevention Under Concurrency & Idempotency
    // =========================================================================

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "11")]
    public async Task ProcessPayment_DemoCard_Idempotent_Replay_Preserves_Balance()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true, bookingTotal: 300.00m, bookingId: 50);

        var demoMethod = await commandService.Handle(new ActivateDemoPaymentMethodCommand(5));

        var cmd = new ProcessPaymentCommand(
            BookingId: 50,
            Amount: 300.00m,
            PaymentMethod: "DemoCard",
            CardNumber: string.Empty,
            CardHolderName: "Demo Guest",
            ExpirationDate: string.Empty,
            Cvv: string.Empty,
            IdempotencyKey: "key-demo-replay",
            ActorId: 5,
            DemoPaymentMethodId: demoMethod.Id
        );

        // First pass
        var (payment1, isDuplicate1, _) = await commandService.Handle(cmd);
        payment1.Should().NotBeNull();
        isDuplicate1.Should().BeFalse();

        // Second pass with same idempotency key
        var (payment2, isDuplicate2, conflict2) = await commandService.Handle(cmd);
        payment2.Should().NotBeNull();
        isDuplicate2.Should().BeTrue();
        conflict2.Should().BeNull();
        payment2!.Id.Should().Be(payment1!.Id);

        // Assert: Balance is debited ONLY once (USD 999,700.00, not USD 999,400.00)
        var method = await context.DemoPaymentMethods.FindAsync(demoMethod.Id);
        method!.Balance.Should().Be(999_700.00m);

        // Assert: Exactly 1 ledger movement in total
        (await context.PaymentLedgerMovements.CountAsync()).Should().Be(1);
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "11")]
    public async Task ProcessPayment_DemoCard_Overdraft_Prevention_Rejects_When_Insufficient_Balance()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true, bookingTotal: 500.00m, bookingId: 55);

        // Create demo method and manually deplete its balance to 200.00m
        var demoMethod = new DemoPaymentMethod(guestUserId: 8, initialCredit: 200.00m);
        context.DemoPaymentMethods.Add(demoMethod);
        await context.SaveChangesAsync();

        var cmd = new ProcessPaymentCommand(
            BookingId: 55,
            Amount: 500.00m, // requires 500, but only 200 available
            PaymentMethod: "DemoCard",
            CardNumber: string.Empty,
            CardHolderName: "Demo Guest",
            ExpirationDate: string.Empty,
            Cvv: string.Empty,
            IdempotencyKey: "key-overdraft-test",
            ActorId: 8,
            DemoPaymentMethodId: demoMethod.Id
        );

        // Act
        var (payment, isDuplicate, conflictReason) = await commandService.Handle(cmd);

        // Assert
        payment.Should().BeNull();
        isDuplicate.Should().BeFalse();
        conflictReason.Should().Contain("Insufficient balance");

        // Verify balance was untouched
        var reloaded = await context.DemoPaymentMethods.FindAsync(demoMethod.Id);
        reloaded!.Balance.Should().Be(200.00m);

        // No ledger movement recorded
        (await context.PaymentLedgerMovements.CountAsync()).Should().Be(0);
    }

    // =========================================================================
    // FEATURE 12: Refund Balance Restoration
    // =========================================================================

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "12")]
    public async Task RefundPayment_DemoCard_Restores_Balance_Atomically()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true, bookingTotal: 300.00m, bookingId: 60);

        var demoMethod = await commandService.Handle(new ActivateDemoPaymentMethodCommand(9));

        // 1. Process payment for $300.00 -> balance becomes $999,700.00
        var cmd = new ProcessPaymentCommand(60, 300.00m, "DemoCard", "", "Demo Guest", "", "", "key-rf-1", 9, demoMethod.Id);
        var (payment, _, _) = await commandService.Handle(cmd);
        payment.Should().NotBeNull();

        var midMethod = await context.DemoPaymentMethods.FindAsync(demoMethod.Id);
        midMethod!.Balance.Should().Be(999_700.00m);

        // 2. Act - Refund payment
        var refundResult = await commandService.Handle(new RefundPaymentCommand(payment!.Id, "Customer cancellation"));

        // 3. Assert
        refundResult.Should().BeTrue();

        // Payment status is Refunded
        var updatedPayment = await context.Payments.FindAsync(payment.Id);
        updatedPayment!.Status.Should().Be(PaymentStatus.Refunded);

        // Balance restored to exactly $1,000,000.00
        var finalMethod = await context.DemoPaymentMethods.FindAsync(demoMethod.Id);
        finalMethod!.Balance.Should().Be(1_000_000.00m);

        // Exactly 2 movements: 1 Debit, 1 Refund
        var movements = await context.PaymentLedgerMovements
            .Where(m => m.PaymentId == payment.Id)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync();
        movements.Should().HaveCount(2);
        movements[0].OperationType.Should().Be("Debit");
        movements[0].Amount.Should().Be(300.00m);
        movements[0].BalanceAfter.Should().Be(999_700.00m);

        movements[1].OperationType.Should().Be("Refund");
        movements[1].Amount.Should().Be(300.00m);
        movements[1].BalanceAfter.Should().Be(1_000_000.00m);

        // Outbox contains payment-refunded
        var refundOutbox = await context.OutboxMessages.FirstOrDefaultAsync(m => m.EventType == "payments.payment-refunded");
        refundOutbox.Should().NotBeNull();
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "12")]
    public async Task RefundPayment_DemoCard_Duplicate_Refund_Is_Idempotent()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var commandService = CreateCommandService(context, enableDemoPayment: true, bookingTotal: 300.00m, bookingId: 65);

        var demoMethod = await commandService.Handle(new ActivateDemoPaymentMethodCommand(11));

        var cmd = new ProcessPaymentCommand(65, 300.00m, "DemoCard", "", "Demo Guest", "", "", "key-dup-rf", 11, demoMethod.Id);
        var (payment, _, _) = await commandService.Handle(cmd);

        // First refund
        await commandService.Handle(new RefundPaymentCommand(payment!.Id, "Reason 1"));
        var balanceAfterFirstRefund = (await context.DemoPaymentMethods.FindAsync(demoMethod.Id))!.Balance;
        balanceAfterFirstRefund.Should().Be(1_000_000.00m);

        // Second refund for same payment
        var secondResult = await commandService.Handle(new RefundPaymentCommand(payment.Id, "Reason 2"));
        secondResult.Should().BeTrue();

        // Balance must remain $1,000,000.00 (NOT credited twice to $1,000,300.00)
        var balanceAfterSecondRefund = (await context.DemoPaymentMethods.FindAsync(demoMethod.Id))!.Balance;
        balanceAfterSecondRefund.Should().Be(1_000_000.00m);

        // Exactly 1 Refund ledger movement
        var refundMovements = await context.PaymentLedgerMovements
            .Where(m => m.PaymentId == payment.Id && m.OperationType == "Refund")
            .ToListAsync();
        refundMovements.Should().HaveCount(1);
    }

    // =========================================================================
    // FEATURE 13: Demo Method Internal Validation Endpoint
    // =========================================================================

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "13")]
    public async Task ValidateDemoPaymentMethod_When_Valid_Returns_200_With_Contract()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var queryService = new PaymentQueryService(repo);
        var controller = new InternalPaymentsController(queryService);

        var method = new DemoPaymentMethod(guestUserId: 15);
        context.DemoPaymentMethods.Add(method);
        await context.SaveChangesAsync();

        // Act
        var result = await controller.ValidateDemoPaymentMethod(method.Id, guestUserId: 15);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value.Should().BeOfType<ValidateDemoPaymentMethodResponse>().Subject;
        response.IsValid.Should().BeTrue();
        response.Status.Should().Be("Active");
        response.Balance.Should().Be(1_000_000.00m);
        response.Currency.Should().Be("USD");
        response.Reason.Should().BeNull();
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "13")]
    public async Task ValidateDemoPaymentMethod_When_NotFound_Returns_404()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var queryService = new PaymentQueryService(repo);
        var controller = new InternalPaymentsController(queryService);

        // Act
        var result = await controller.ValidateDemoPaymentMethod("dpm_nonexistent", guestUserId: 15);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
        var notFoundResult = (NotFoundObjectResult)result;
        var response = notFoundResult.Value.Should().BeOfType<ValidateDemoPaymentMethodResponse>().Subject;
        response.IsValid.Should().BeFalse();
        response.Reason.Should().Contain("Payment method not found");
    }

    [Fact]
    [Trait("Service", "Payments")]
    [Trait("Feature", "13")]
    public async Task ValidateDemoPaymentMethod_When_Unowned_Or_Inactive_Returns_422()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new PaymentRepository(context);
        var queryService = new PaymentQueryService(repo);
        var controller = new InternalPaymentsController(queryService);

        var method = new DemoPaymentMethod(guestUserId: 20);
        context.DemoPaymentMethods.Add(method);
        await context.SaveChangesAsync();

        // Case A: Wrong guestUserId (Unowned)
        var unownedResult = await controller.ValidateDemoPaymentMethod(method.Id, guestUserId: 99);
        unownedResult.Should().BeOfType<UnprocessableEntityObjectResult>();
        var unownedResponse = ((UnprocessableEntityObjectResult)unownedResult).Value.Should().BeOfType<ValidateDemoPaymentMethodResponse>().Subject;
        unownedResponse.IsValid.Should().BeFalse();
        unownedResponse.Reason.Should().Contain("not owned by guest");

        // Case B: Inactive method
        method.Deactivate();
        context.DemoPaymentMethods.Update(method);
        await context.SaveChangesAsync();

        var inactiveResult = await controller.ValidateDemoPaymentMethod(method.Id, guestUserId: 20);
        inactiveResult.Should().BeOfType<UnprocessableEntityObjectResult>();
        var inactiveResponse = ((UnprocessableEntityObjectResult)inactiveResult).Value.Should().BeOfType<ValidateDemoPaymentMethodResponse>().Subject;
        inactiveResponse.IsValid.Should().BeFalse();
        inactiveResponse.Reason.Should().Contain("inactive");
    }
}
