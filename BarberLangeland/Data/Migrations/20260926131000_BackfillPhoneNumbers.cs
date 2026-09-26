using BarberLangeland.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <summary>
    /// Phone-first auth compares numbers in E.164 form, so existing accounts must be stored
    /// canonically or a returning customer is never found and gets a duplicate account.
    ///
    /// Every non-canonical value in the dev database is a bare 8-digit Danish number
    /// (e.g. "50505050"), which is simply "+45" + the digits. Values already starting with "+"
    /// are left alone, making this safe to re-run. NULL phones are skipped: that account
    /// (e74194c6-5438-4156-b5f6-d8994983c247) has no stored number to normalize and can
    /// still sign in normally, it just cannot register a booking by phone yet.
    ///
    /// Anything that is neither 8 digits nor already prefixed is left untouched rather than
    /// guessed at, so no number is ever mangled.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260926131000_BackfillPhoneNumbers")]
    public partial class BackfillPhoneNumbers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [PhoneNumber] = '+45' + [PhoneNumber], [PhoneNumberConfirmed] = 1
WHERE [PhoneNumber] IS NOT NULL
  AND [PhoneNumber] NOT LIKE '+%'
  AND LEN([PhoneNumber]) = 8
  AND [PhoneNumber] NOT LIKE '%[^0-9]%';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Strips the Danish country code back off, leaving the original 8-digit value.
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [PhoneNumber] = SUBSTRING([PhoneNumber], 4, 8)
WHERE [PhoneNumber] IS NOT NULL
  AND [PhoneNumber] LIKE '+45%'
  AND LEN([PhoneNumber]) = 10
  AND SUBSTRING([PhoneNumber], 4, 8) NOT LIKE '%[^0-9]%';");
        }
    }
}
