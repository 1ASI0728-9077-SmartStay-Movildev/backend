using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStay.Accommodations.Infrastructure.Persistence.Migrations
{
    public partial class AddRoomLayout : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "layout",
                table: "rooms",
                type: "json",
                nullable: true);

            migrationBuilder.InsertData(
                table: "hotel_categories",
                columns: new[] { "id", "name" },
                values: new object[] { 7, "Departamento" });

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 101,
                column: "layout",
                value: "{\"widthM\":3.0,\"lengthM\":3.5,\"heightM\":2.6,\"floor\":\"Wood\",\"wallColor\":\"#F5F0E8\",\"beds\":[{\"type\":\"OnePlaza\",\"x\":0.55,\"z\":1.1,\"rotationDeg\":0}],\"windows\":[{\"wall\":\"N\",\"offset\":1.0,\"width\":1.0}],\"door\":{\"wall\":\"S\",\"offset\":0.3,\"width\":0.9}}"
                );

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 102,
                column: "layout",
                value: "{\"widthM\":4.0,\"lengthM\":5.0,\"heightM\":2.7,\"floor\":\"Wood\",\"wallColor\":\"#EFE9DF\",\"beds\":[{\"type\":\"TwoPlazas\",\"x\":2.0,\"z\":1.3,\"rotationDeg\":0}],\"windows\":[{\"wall\":\"N\",\"offset\":1.0,\"width\":2.0}],\"door\":{\"wall\":\"S\",\"offset\":0.4,\"width\":0.9}}"
                );

            migrationBuilder.UpdateData(
                table: "rooms",
                keyColumn: "id",
                keyValue: 201,
                column: "layout",
                value: "{\"widthM\":6.0,\"lengthM\":7.0,\"heightM\":3.0,\"floor\":\"Porcelain\",\"wallColor\":\"#EDE7DD\",\"beds\":[{\"type\":\"King\",\"x\":3.0,\"z\":1.4,\"rotationDeg\":0},{\"type\":\"OnePlaza\",\"x\":0.8,\"z\":5.8,\"rotationDeg\":0},{\"type\":\"OnePlaza\",\"x\":2.0,\"z\":5.8,\"rotationDeg\":0}],\"windows\":[{\"wall\":\"E\",\"offset\":1.5,\"width\":2.5},{\"wall\":\"N\",\"offset\":1.0,\"width\":1.5}],\"door\":{\"wall\":\"S\",\"offset\":4.5,\"width\":1.0}}"
                );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "layout",
                table: "rooms");

            migrationBuilder.DeleteData(
                table: "hotel_categories",
                keyColumn: "id",
                keyValue: 7);
        }
    }
}
