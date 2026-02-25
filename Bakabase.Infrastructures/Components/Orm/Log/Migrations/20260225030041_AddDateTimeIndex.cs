using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakabase.Infrastructures.Components.Orm.Log.Migrations
{
    /// <inheritdoc />
    public partial class AddDateTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Logs_DateTime",
                table: "Logs",
                column: "DateTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Logs_DateTime",
                table: "Logs");
        }
    }
}
