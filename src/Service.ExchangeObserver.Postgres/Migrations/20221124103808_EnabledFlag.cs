using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Service.ExchangeObserver.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EnabledFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                schema: "exchangeobserver",
                table: "assets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEnabled",
                schema: "exchangeobserver",
                table: "assets");
        }
    }
}
