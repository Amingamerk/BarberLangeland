using BarberLangeland.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <summary>
    /// Usernames used to be the phone number, which blocked a shared or family number from
    /// owning more than one account: the username was implicitly unique per number. Phone is
    /// now the lookup key and usernames are opaque "user_&lt;guid&gt;" identifiers, so the
    /// accounts still carrying a bare numeric username are renamed here to free the number up.
    ///
    /// NEWID() is unique per row within a single statement, so no collision is possible. The
    /// same generated value is reused for UserName and NormalizedUserName so the two stay in
    /// sync, which is what the Identity lookup depends on.
    ///
    /// Side effect: UserName is part of the login identity, so anyone currently signing in by
    /// typing their number into the username box must instead use the phone-based flow.
    /// Passwords are untouched, so no credential is lost.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260926132000_MigrateUsernamesOffPhone")]
    public partial class MigrateUsernamesOffPhone : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE [AspNetUsers]
SET [UserName] = g.[Name],
    [NormalizedUserName] = UPPER(g.[Name])
FROM [AspNetUsers] AS u
CROSS APPLY (SELECT 'user_' + REPLACE(CONVERT(varchar(36), NEWID()), '-', '') AS [Name]) AS g
WHERE u.[PhoneNumber] IS NOT NULL
  AND u.[UserName] NOT LIKE 'user[_]%'
  AND u.[UserName] NOT LIKE '%@%'
  AND u.[UserName] NOT LIKE '%[%';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the original numeric usernames cannot be recovered from the
            // generated GUIDs. Intentionally a no-op rather than a lossy guess.
        }
    }
}
