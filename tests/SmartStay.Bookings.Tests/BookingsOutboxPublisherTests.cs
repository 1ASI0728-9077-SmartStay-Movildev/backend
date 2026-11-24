using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;
using Xunit;

namespace SmartStay.Bookings.Tests;

public class BookingsOutboxPublisherTests
{
    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Outbox")]
    public void OutboxMessage_MarkPublished_Sets_PublishedAtUtc()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "bookings.test", 1, "1", 1, "{}");
        msg.PublishedAtUtc.Should().BeNull();

        msg.MarkPublished();

        msg.PublishedAtUtc.Should().NotBeNull();
        msg.LeaseOwner.Should().BeNull();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Outbox")]
    public void OutboxMessage_RecordFailure_Increments_Attempts_And_Sets_NextAttempt()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "bookings.test", 1, "1", 1, "{}");
        msg.Attempts.Should().Be(0);

        msg.RecordFailure("Network timeout");

        msg.Attempts.Should().Be(1);
        msg.LastError.Should().Be("Network timeout");
        msg.NextAttemptAtUtc.Should().NotBeNull();
        msg.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(-1));
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Outbox")]
    public async Task GetPendingOutboxMessagesAsync_Returns_Only_Pending_Messages()
    {
        var options = new DbContextOptionsBuilder<BookingsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new BookingsDbContext(options);
        var repo = new BookingRepository(context);

        var msgPending = new OutboxMessage(Guid.NewGuid(), "bookings.pending", 1, "1", 1, "{}");
        var msgPublished = new OutboxMessage(Guid.NewGuid(), "bookings.published", 1, "2", 1, "{}");
        msgPublished.MarkPublished();

        await repo.AddOutboxMessageAsync(msgPending);
        await repo.AddOutboxMessageAsync(msgPublished);
        await context.SaveChangesAsync();

        var pending = await repo.GetPendingOutboxMessagesAsync(10);

        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be(msgPending.Id);
    }
}
