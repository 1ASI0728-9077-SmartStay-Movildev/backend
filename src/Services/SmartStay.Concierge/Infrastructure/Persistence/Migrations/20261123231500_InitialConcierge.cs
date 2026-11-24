using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Concierge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialConcierge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_intents",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    guest_user_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    check_in = table.Column<DateOnly>(type: "date", nullable: false),
                    check_out = table.Column<DateOnly>(type: "date", nullable: false),
                    district = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    guests = table.Column<int>(type: "int", nullable: false),
                    budget_total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "USD")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    demo_payment_method_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    idempotency_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contact_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contact_email = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contact_phone = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cancellation_policy_accepted = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    selected_room_id = table.Column<int>(type: "int", nullable: true),
                    selected_room_type_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    selected_hotel_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stay_total = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    quote_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quote_expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    booking_id = table.Column<int>(type: "int", nullable: true),
                    payment_id = table.Column<int>(type: "int", nullable: true),
                    receipt_voucher = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    candidate_attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    retry_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    next_retry_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    concurrency_token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_intents", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_intents_guest_idempotency",
                table: "purchase_intents",
                columns: new[] { "guest_user_id", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_intents_guest_user_id",
                table: "purchase_intents",
                column: "guest_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_intents_status",
                table: "purchase_intents",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_intents");
        }
    }
}
