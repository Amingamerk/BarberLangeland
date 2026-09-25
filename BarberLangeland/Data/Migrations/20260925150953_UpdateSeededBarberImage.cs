using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSeededBarberImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Barbers",
                keyColumn: "Id",
                keyValue: 1,
                column: "ImagePath",
                value: "/images/bcd24d41-f381-41f8-836e-d9489dc56724_19_90_0_316_6016_3384_880_495_81aa63b1.jpg");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Barbers",
                keyColumn: "Id",
                keyValue: 1,
                column: "ImagePath",
                value: "/images/bashar.jpg");
        }
    }
}
