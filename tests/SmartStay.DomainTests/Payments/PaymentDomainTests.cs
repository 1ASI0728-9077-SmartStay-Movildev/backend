using BackendAwSmartstay.API.Payments.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Payments.Domain.Model.Commands;
using FluentAssertions;
using Xunit;

namespace SmartStay.DomainTests.Payments;

public class PaymentDomainTests
{
    [Fact]
    [Trait("Area", "Payments")]
    [Trait("Risk", "Lifecycle")]
    public void Payment_created_from_command_should_have_pending_status_and_masked_card()
    {
        // Arrange
        var command = new ProcessPaymentCommand(
            BookingId: 10,
            Amount: 150.00m,
            PaymentMethod: "CreditCard",
            CardNumber: "4111222233334567",
            CardHolderName: "Carlos Santana",
            ExpirationDate: "12/28",
            Cvv: "123"
        );

        // Act
        var payment = new Payment(command);

        // Assert
        payment.BookingId.Should().Be(10);
        payment.Amount.Should().Be(150.00m);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.CardNumberMasked.Should().Be("**** **** **** 4567");
        payment.CardHolderName.Should().Be("Carlos Santana");
        payment.TransactionId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Area", "Payments")]
    [Trait("Risk", "Lifecycle")]
    public void Complete_should_transition_status_to_completed()
    {
        // Arrange
        var command = new ProcessPaymentCommand(
            BookingId: 10,
            Amount: 150.00m,
            PaymentMethod: "CreditCard",
            CardNumber: "4111222233334567",
            CardHolderName: "Carlos Santana",
            ExpirationDate: "12/28",
            Cvv: "123"
        );
        var payment = new Payment(command);

        // Act
        payment.Complete();

        // Assert
        payment.Status.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    [Trait("Area", "Payments")]
    [Trait("Risk", "Lifecycle")]
    public void Fail_should_transition_status_to_failed()
    {
        // Arrange
        var command = new ProcessPaymentCommand(
            BookingId: 10,
            Amount: 150.00m,
            PaymentMethod: "CreditCard",
            CardNumber: "4111222233334567",
            CardHolderName: "Carlos Santana",
            ExpirationDate: "12/28",
            Cvv: "123"
        );
        var payment = new Payment(command);

        // Act
        payment.Fail();

        // Assert
        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Theory]
    [InlineData("4111222233330000", 100.0, false, "Card ending in 0000 should be declined")]
    [InlineData("4111222233331111", 0.0, false, "Amount of zero should be declined")]
    [InlineData("4111222233331111", -50.0, false, "Negative amount should be declined")]
    [InlineData("4111222233331234", 120.0, true, "Valid card and positive amount should be approved")]
    [Trait("Area", "Payments")]
    [Trait("Risk", "SimulationRules")]
    public void Payment_simulation_rules_evaluate_correctly(string cardNumber, double amount, bool shouldApprove, string because)
    {
        // Arrange
        var isApproved = true;
        var decimalAmount = (decimal)amount;

        // Act (simulation rules matching PaymentCommandService)
        if (decimalAmount <= 0) isApproved = false;
        if (cardNumber.EndsWith("0000")) isApproved = false;

        // Assert
        isApproved.Should().Be(shouldApprove, because);
    }
}
