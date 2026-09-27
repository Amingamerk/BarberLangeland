using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameBookingDescriptionToCustomerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Bookings",
                newName: "CustomerName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CustomerName",
                table: "Bookings",
                newName: "Description");
        }
    }
}
