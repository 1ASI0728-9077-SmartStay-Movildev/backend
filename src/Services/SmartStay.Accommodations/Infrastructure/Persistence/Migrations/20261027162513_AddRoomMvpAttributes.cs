using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Accommodations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomMvpAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "capacity",
                table: "rooms",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "rooms",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "is_bookable",
                table: "rooms",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "tour_path",
                table: "rooms",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "district",
                table: "hotels",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "hotels",
                keyColumn: "id",
                keyValue: 1,
                column: "district",
                value: "Cercado de Lima");

            migrationBuilder.UpdateData(
                table: "hotels",
                keyColumn: "id",
                keyValue: 2,
                column: "district",
                value: "San Blas");

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 101,
                columns: new[] { "capacity", "currency", "description", "is_bookable", "tour_path" },
                values: new object[] { 1, "USD", "Room 101 - Standard cozy single room.", true, null });

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 102,
                columns: new[] { "capacity", "currency", "description", "is_bookable", "tour_path" },
                values: new object[] { 2, "USD", "Room 102 - Plaza view deluxe room with 360 virtual tour.", true, "/tours/room-102/manifest.json" });

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 201,
                columns: new[] { "capacity", "currency", "description", "is_bookable", "tour_path" },
                values: new object[] { 4, "USD", "Suite 201 - Panoramic mountain view presidential suite.", true, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capacity",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "is_bookable",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "tour_path",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "district",
                table: "hotels");

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 101,
                column: "description",
                value: "Room 101 - Standard view.");

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 102,
                column: "description",
                value: "Room 102 - Plaza view with balcony.");

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 201,
                column: "description",
                value: "Suite 201 - Panoramic mountain view.");
        }
    }
}
