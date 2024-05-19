using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vgt_saga_orders.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<int>(type: "integer", nullable: false),
                    BookFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TripFrom = table.Column<string>(type: "text", nullable: false),
                    TripTo = table.Column<string>(type: "text", nullable: false),
                    HotelName = table.Column<string>(type: "text", nullable: false),
                    RoomType = table.Column<string>(type: "text", nullable: false),
                    AdultCount = table.Column<int>(type: "integer", nullable: false),
                    OldChildren = table.Column<int>(type: "integer", nullable: false),
                    MidChildren = table.Column<int>(type: "integer", nullable: false),
                    LesserChildren = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    TempBookHotel = table.Column<bool>(type: "boolean", nullable: true),
                    TempBookFlight = table.Column<bool>(type: "boolean", nullable: true),
                    FullBookHotel = table.Column<bool>(type: "boolean", nullable: true),
                    FullBookFlight = table.Column<bool>(type: "boolean", nullable: true),
                    Payment = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.TransactionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
