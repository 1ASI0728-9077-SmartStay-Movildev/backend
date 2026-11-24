using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingQuotesAndPurchaseIntentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purchase_intent_id",
                table: "bookings",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_purchase_intent_id",
                table: "bookings",
                column: "purchase_intent_id");

            migrationBuilder.CreateTable(
                name: "booking_quotes",
                columns: table => new
                {
                    quote_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    room_id = table.Column<int>(type: "int", nullable: false),
                    hotel_id = table.Column<int>(type: "int", nullable: false),
                    guest_user_id = table.Column<int>(type: "int", nullable: false),
                    purchase_intent_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    check_in_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    check_out_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    guests = table.Column<int>(type: "int", nullable: false),
                    nightly_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    stay_total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "USD")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_used = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_quotes", x => x.quote_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_booking_quotes_purchase_intent_id",
                table: "booking_quotes",
                column: "purchase_intent_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_quotes_guest_user_id",
                table: "booking_quotes",
                column: "guest_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_quotes");

            migrationBuilder.DropIndex(
                name: "ix_bookings_purchase_intent_id",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "purchase_intent_id",
                table: "bookings");
        }
    }
}
