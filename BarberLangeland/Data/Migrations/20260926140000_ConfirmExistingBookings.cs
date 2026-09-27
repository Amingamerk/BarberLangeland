using BarberLangeland.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <summary>
    /// Bookings are now confirmed automatically on creation, so a booking row that is neither
    /// cancelled nor a no-show but still carries IsConfirmed = 0 is a leftover from the manual
    /// approval flow rather than a genuinely pending appointment. Marking it confirmed keeps
    /// those customers from seeing a stale "Afventer bekræftelse" badge that can no longer be
    /// cleared, since the manual confirm action has been removed.
    ///
    /// The WHERE clause is deliberately narrow: cancelled and no-show rows are excluded because
    /// they keep their own status and must not be flipped to confirmed. Re-running this is a
    /// no-op once applied, since the predicate stops matching as rows are updated.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260926140000_ConfirmExistingBookings")]
    public partial class ConfirmExistingBookings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [Bookings]
SET [IsConfirmed] = 1
WHERE [IsConfirmed] = 0
  AND [IsCancelled] = 0
  AND [IsNoShow] = 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the pending state for rows confirmed after this migration ran. The dev
            // database holds no bookings, so there is nothing to reverse in practice; the
            // statement is kept so Down is a truthful inverse of Up.
            migrationBuilder.Sql(@"
UPDATE [Bookings]
SET [IsConfirmed] = 0
WHERE [IsConfirmed] = 1
  AND [IsCancelled] = 0
  AND [IsNoShow] = 0;");
        }
    }
}
