using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Service.ExchangeObserver.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Fee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FireblockToBinanceFee",
                schema: "exchangeobserver",
                table: "assets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FireblockToBinanceFee",
                schema: "exchangeobserver",
                table: "assets");
        }
    }
}
