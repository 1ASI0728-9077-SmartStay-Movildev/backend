using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniquePaymentBookingIdAndActorIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_booking_id",
                table: "payments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_payment_idempotency_records",
                table: "payment_idempotency_records");

            migrationBuilder.AddPrimaryKey(
                name: "pk_payment_idempotency_records",
                table: "payment_idempotency_records",
                columns: new[] { "actor_id", "idempotency_key" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_booking_id",
                table: "payments",
                column: "booking_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payments_booking_id",
                table: "payments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_payment_idempotency_records",
                table: "payment_idempotency_records");

            migrationBuilder.AddPrimaryKey(
                name: "pk_payment_idempotency_records",
                table: "payment_idempotency_records",
                column: "idempotency_key");

            migrationBuilder.CreateIndex(
                name: "ix_payments_booking_id",
                table: "payments",
                column: "booking_id");
        }
    }
}
