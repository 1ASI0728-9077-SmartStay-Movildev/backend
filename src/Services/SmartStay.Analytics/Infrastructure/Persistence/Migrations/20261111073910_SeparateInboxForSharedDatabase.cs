using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Analytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateInboxForSharedDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_inbox_messages",
                table: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "analytics_inbox_messages");

            migrationBuilder.AddPrimaryKey(
                name: "pk_analytics_inbox_messages",
                table: "analytics_inbox_messages",
                columns: new[] { "consumer_name", "event_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_analytics_inbox_messages",
                table: "analytics_inbox_messages");

            migrationBuilder.RenameTable(
                name: "analytics_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.AddPrimaryKey(
                name: "pk_inbox_messages",
                table: "inbox_messages",
                columns: new[] { "consumer_name", "event_id" });
        }
    }
}
