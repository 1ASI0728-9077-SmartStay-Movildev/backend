using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SmartStay.Accommodations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccommodations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "amenities_catalogs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_amenities_catalogs", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "hotel_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hotel_categories", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "hotels",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    host_id = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    address = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    country = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    image_url = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amenities = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hotels", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "room_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_types", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    room_type_id = table.Column<int>(type: "int", nullable: false),
                    hotel_id = table.Column<int>(type: "int", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amenities = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_rooms_hotels_hotel_id",
                        column: x => x.hotel_id,
                        principalTable: "hotels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rooms_room_types_room_type_id",
                        column: x => x.room_type_id,
                        principalTable: "room_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "amenities_catalogs",
                columns: new[] { "id", "category", "name" },
                values: new object[,]
                {
                    { 1, "General", "Wifi" },
                    { 2, "General", "Piscina" },
                    { 3, "General", "Gimnasio" },
                    { 4, "General", "Restaurante" },
                    { 5, "General", "Parking" },
                    { 6, "General", "Spa" },
                    { 7, "General", "Bar" },
                    { 8, "General", "Desayuno" }
                });

            migrationBuilder.InsertData(
                table: "hotel_categories",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { 1, "Hotel" },
                    { 2, "Posada" },
                    { 3, "Lodge" },
                    { 4, "Hostal" },
                    { 5, "Cabaña" },
                    { 6, "Resort" }
                });

            migrationBuilder.InsertData(
                table: "hotels",
                columns: new[] { "id", "address", "amenities", "city", "country", "description", "host_id", "image_url", "name", "type" },
                values: new object[,]
                {
                    { 1, "Jr. de la Unión 958", "[\"Wifi\",\"Restaurante\",\"Bar\"]", "Lima", "Peru", "Historic hotel in the center of Lima.", 1, "https://placehold.co/600x400/3498DB/FFFFFF?text=Bolivar", "Grand Hotel Bolivar", "Hotel" },
                    { 2, "San Blas 123", "[\"Desayuno\",\"Wifi\",\"Gimnasio\"]", "Cusco", "Peru", "Experience the mystic energy of the Andes.", 1, "https://placehold.co/600x400/E67E22/FFFFFF?text=Andean", "Cusco Andean Lodge", "Lodge" }
                });

            migrationBuilder.InsertData(
                table: "room_types",
                columns: new[] { "id", "description", "name" },
                values: new object[,]
                {
                    { 1, "Cozy room for solo travelers.", "Single Standard" },
                    { 2, "Spacious room for couples or business.", "Double Deluxe" },
                    { 3, "Luxury suite with best views.", "Presidential Suite" }
                });

            migrationBuilder.InsertData(
                table: "rooms",
                columns: new[] { "id", "amenities", "description", "hotel_id", "price", "room_type_id" },
                values: new object[,]
                {
                    { 101, "[\"Wifi\",\"TV\"]", "Room 101 - Standard view.", 1, 85.00m, 1 },
                    { 102, "[\"Wifi\",\"TV\",\"Minibar\"]", "Room 102 - Plaza view with balcony.", 1, 150.00m, 2 },
                    { 201, "[\"Jacuzzi\",\"Wifi\",\"Desayuno\",\"Chimenea\"]", "Suite 201 - Panoramic mountain view.", 2, 320.00m, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_rooms_hotel_id",
                table: "rooms",
                column: "hotel_id");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_room_type_id",
                table: "rooms",
                column: "room_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "amenities_catalogs");

            migrationBuilder.DropTable(
                name: "hotel_categories");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "hotels");

            migrationBuilder.DropTable(
                name: "room_types");
        }
    }
}
