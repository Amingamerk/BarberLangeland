using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceActiveSortAndBookingPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Services",
                type: "bit",
                nullable: false,
                // Every existing service is offered today, so they all start out active.
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Services",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "Services",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "IsActive", "SortOrder" },
                values: new object[] { true, 1 });

            migrationBuilder.UpdateData(
                table: "Services",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "IsActive", "SortOrder" },
                values: new object[] { true, 2 });

            migrationBuilder.UpdateData(
                table: "Services",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "IsActive", "SortOrder" },
                values: new object[] { true, 3 });

            // A service added outside the seed data keeps the order it had (by id).
            migrationBuilder.Sql("UPDATE [Services] SET [SortOrder] = [Id] WHERE [SortOrder] = 0;");

            // Existing bookings were made at the service's current price; record it on each booking.
            migrationBuilder.Sql(
                "UPDATE b SET b.[Price] = s.[Price] FROM [Bookings] AS b INNER JOIN [Services] AS s ON s.[Id] = b.[ServiceId];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Bookings");
        }
    }
}
