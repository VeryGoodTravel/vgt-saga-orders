using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vgt_saga_orders.Migrations
{
    /// <inheritdoc />
    public partial class FullService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TripTo",
                table: "Transactions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TripTo",
                table: "Transactions");
        }
    }
}
