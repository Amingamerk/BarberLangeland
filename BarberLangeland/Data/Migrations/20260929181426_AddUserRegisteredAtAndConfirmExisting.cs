using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberLangeland.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRegisteredAtAndConfirmExisting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Email verification starts now. Every account that exists today has been using the
            // site without it, so it counts as confirmed: nobody is asked again and nothing is
            // removed by the clean-up of unconfirmed accounts.
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [EmailConfirmed] = 1 WHERE [EmailConfirmed] = 0;");

            // A booking that is neither confirmed, cancelled nor a no-show now means "waiting for the
            // customer's email"; none of the existing ones do.
            migrationBuilder.Sql("UPDATE [Bookings] SET [IsConfirmed] = 1 WHERE [IsConfirmed] = 0 AND [IsCancelled] = 0 AND [IsNoShow] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "AspNetUsers");
        }
    }
}
