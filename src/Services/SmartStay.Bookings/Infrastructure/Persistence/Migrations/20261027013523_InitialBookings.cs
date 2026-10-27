using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    room_id = table.Column<int>(type: "int", nullable: false),
                    hotel_id = table.Column<int>(type: "int", nullable: false),
                    guest_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    guest_email = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    check_in_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    check_out_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    nightly_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "USD")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "room_night_locks",
                columns: table => new
                {
                    room_id = table.Column<int>(type: "int", nullable: false),
                    stay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    booking_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_night_locks", x => new { x.room_id, x.stay_date });
                    table.ForeignKey(
                        name: "fk_room_night_locks_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_room_night_booking",
                table: "room_night_locks",
                column: "booking_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_night_locks");

            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}
