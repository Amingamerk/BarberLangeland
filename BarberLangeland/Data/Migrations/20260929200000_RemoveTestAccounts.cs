using BarberLangeland.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <summary>
    /// Removes the two accounts (and their bookings) that were created while testing the live site.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260929200000_RemoveTestAccounts")]
    public partial class RemoveTestAccounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE b FROM [Bookings] AS b
INNER JOIN [AspNetUsers] AS u ON u.[Id] = b.[UserId]
WHERE u.[Email] IN ('test-20260929@example.com', 'livetest-20260929@example.com');");

            migrationBuilder.Sql(@"
DELETE FROM [AspNetUsers]
WHERE [Email] IN ('test-20260929@example.com', 'livetest-20260929@example.com');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted test data is not restored.
        }
    }
}
