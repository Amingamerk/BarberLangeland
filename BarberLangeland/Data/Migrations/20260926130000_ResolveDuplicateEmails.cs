using BarberLangeland.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <summary>
    /// Two pairs of test accounts share an email address, which blocks the unique index on
    /// Email added in AddBookingStatusFlagsAndUniqueEmail. Neither colliding account owns a
    /// booking (the only booking belongs to 42aa0c7e-...), so the least destructive fix is to
    /// rename the *email* of the second account of each pair rather than merge or delete.
    /// The original address is unambiguous, so the rename is straightforward to reverse.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260926130000_ResolveDuplicateEmails")]
    public partial class ResolveDuplicateEmails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Test account already stored as +4512345678 keeps test@example.com; the plain
            // "12345678" account is the one renamed.
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [Email] = 'test+2@example.com'
WHERE [Id] = 'c071a7b0-52cb-48ac-9d85-ec28a08bd899' AND [Email] = 'test@example.com';");

            // Both Amin081204@gmail.com accounts are unconfirmed test data with no bookings.
            // The NULL-phone one (e74194c6-...) keeps the address; the other is renamed.
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [Email] = 'Amin081204+2@gmail.com'
WHERE [Id] = 'd1276c62-5c6d-46a4-bba7-1e19641c448e' AND [Email] = 'Amin081204@gmail.com';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [Email] = 'test@example.com'
WHERE [Id] = 'c071a7b0-52cb-48ac-9d85-ec28a08bd899' AND [Email] = 'test+2@example.com';");

            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [Email] = 'Amin081204@gmail.com'
WHERE [Id] = 'd1276c62-5c6d-46a4-bba7-1e19641c448e' AND [Email] = 'Amin081204+2@gmail.com';");
        }
    }
}
