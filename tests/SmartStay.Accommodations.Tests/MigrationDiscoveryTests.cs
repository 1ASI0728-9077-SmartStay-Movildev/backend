using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Accommodations.Tests;

public class MigrationDiscoveryTests
{
    [Fact]
    public void RoomLayoutMigrationIsDiscoverableAndProducesSchemaAndSeedSql()
    {
        // Generate SQL without a network connection; catches missing migration metadata.
        var options = new DbContextOptionsBuilder<AccommodationsDbContext>()
            .UseMySql("Server=unused;Database=unused;User ID=unused;Password=unused;",
                new MySqlServerVersion(new Version(5, 7, 38)))
            .Options;
        using var context = new AccommodationsDbContext(options);

        Assert.Contains("20261109000000_AddRoomLayout", context.Database.GetMigrations());
        var sql = context.GetService<IMigrator>().GenerateScript(
            "20261027162513_AddRoomMvpAttributes", "20261109000000_AddRoomLayout");

        Assert.Contains("ADD `layout` json", sql);
        Assert.Contains("UPDATE `rooms` SET `layout`", sql);
        Assert.Contains("INSERT INTO `hotel_categories`", sql);
    }
}
