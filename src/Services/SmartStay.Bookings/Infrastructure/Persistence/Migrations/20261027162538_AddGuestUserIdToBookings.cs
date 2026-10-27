using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestUserIdToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "guest_user_id",
                table: "bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_guest_user_id",
                table: "bookings",
                column: "guest_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_guest_user_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "guest_user_id",
                table: "bookings");
        }
    }
}
