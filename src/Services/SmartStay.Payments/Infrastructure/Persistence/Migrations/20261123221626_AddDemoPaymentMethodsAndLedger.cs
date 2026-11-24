using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoPaymentMethodsAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "demo_payment_method_id",
                table: "payments",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "demo_payment_methods",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    guest_user_id = table.Column<int>(type: "int", nullable: false),
                    label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    currency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demo_payment_methods", x => x.id);
                    table.CheckConstraint("chk_demo_payment_methods_balance", "balance >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payment_ledger_movements",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    demo_payment_method_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payment_id = table.Column<int>(type: "int", nullable: false),
                    operation_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_ledger_movements", x => x.id);
                    table.CheckConstraint("chk_payment_ledger_movements_amount", "amount > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_payments_demo_payment_method_id",
                table: "payments",
                column: "demo_payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ux_demo_payment_methods_guest_user_id",
                table: "demo_payment_methods",
                column: "guest_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_ledger_movements_demo_payment_method_id",
                table: "payment_ledger_movements",
                column: "demo_payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_ledger_movements_payment_operation",
                table: "payment_ledger_movements",
                columns: new[] { "payment_id", "operation_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "demo_payment_methods");

            migrationBuilder.DropTable(
                name: "payment_ledger_movements");

            migrationBuilder.DropIndex(
                name: "ix_payments_demo_payment_method_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "demo_payment_method_id",
                table: "payments");
        }
    }
}
