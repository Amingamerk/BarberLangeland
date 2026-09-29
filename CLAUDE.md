# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Barber Langeland: a server-rendered ASP.NET Core MVC site (.NET 10) for a Danish barber shop, with an online booking wizard. It is one web project, `BarberLangeland/BarberLangeland.csproj`, in `BarberLangeland.slnx`. It uses Razor views, ASP.NET Core Identity (with an `Admin` role), EF Core with SQL Server, `libphonenumber-csharp`, and Bootstrap. Customer-facing copy is Danish; keep it that way.

## Commands

Run from the repository root (the commands are PowerShell-style, but the `dotnet` CLI works the same anywhere):

```
dotnet restore ./BarberLangeland.slnx
dotnet build ./BarberLangeland.slnx
dotnet run --project ./BarberLangeland/BarberLangeland.csproj --launch-profile http   # http://localhost:5039 (https profile: https://localhost:7040)
dotnet ef migrations add <Name> --project ./BarberLangeland/BarberLangeland.csproj
dotnet ef database update --project ./BarberLangeland/BarberLangeland.csproj
dotnet user-secrets set "Salon:AdminPassword" "<pw>" --project ./BarberLangeland/BarberLangeland.csproj
```

- There is no test project and no lint/format command. Don't invent a test command. At minimum, run the solution build.
- Startup calls `Database.Migrate()`, so a SQL Server instance must be reachable. The default connection string targets `.\SQLEXPRESS`, database `FrisorDb`.
- On Windows, stop the running app before building. A running instance locks `bin\Debug\net10.0\BarberLangeland.exe` and the build fails with MSB3021/MSB3027.
- `Salon:AdminPassword` lives only in user secrets and must never go in `appsettings.json`. Without it, `IdentitySeeder` logs a warning, creates no admin, and still creates the `Admin` role. Only `Salon:AdminEmail` is committed.
- CI (`.github/workflows/main_frisorlangeland.yml`) builds and publishes on push to `main` and deploys to Azure Web App.
- The `Dockerfile` still references the old `Frisør_V2` project and assembly names. Fix those before relying on Docker builds.

## Architecture

- **Layering.** Controllers handle HTTP and pick views. Business rules are in `Services/` behind interfaces registered in `Program.cs`. `Data/ApplicationDbContext` derives from `IdentityDbContext<ApplicationUser>` and holds `Bookings`, `Barbers` and `Services`. It also defines the booking FKs and the seed data.
- **Booking wizard.** `BookingController.Index` (GET and POST) drives the 4-step wizard (service, barber, date/time, contact details) through a single `BookingViewModel`. The step and partial view models are in `ViewModels/` and `Views/Booking/_*.cshtml`.
- **Availability and overlap.** `BookingAvailabilityService` owns the opening hours, which are hard-coded (Mon–Fri 09:30–17:00, Sat 09:30–13:00, Sun closed) and generates the bookable slots. `BookingService.CreateBookingAsync` returns `null` for an unknown barber or an overlapping booking. Two bookings overlap when new start < existing end and new end > existing start. Keep that invariant.
- **Phone-first identity.** The phone number is the customer lookup key, and `PhoneIdentityService` handles lookup, sign-in and registration using `PhoneNumberNormalizer` for normalisation. Usernames are opaque `user_<guid>` values, and phone numbers are intentionally not unique. Only `Email` has a unique index.
- **Auth wiring (all three are required).**
  - `AddIdentity<ApplicationUser, IdentityRole>` registers `RoleManager`, which the seeder needs.
  - `.AddDefaultUI()` adds the Identity UI pages.
  - A separate `AddRazorPages()` serves them.
  - The stock Identity login does a username lookup, so it fails for every account. The cookie paths are therefore pointed at `AccountController.Login`, which resolves users by email via `FindByEmailAsync`. Don't repoint them at `/Identity/Account/Login`.
  - Keep the generic types consistent across `ApplicationDbContext`, the registration and `Booking.User`.
- **Email verification.** `EmailVerificationService` mails a confirmation link (Identity token, 24 hours) to a new customer. Until it is opened the account's booking is held but unconfirmed (`IsConfirmed = false`, shown as "Afventer e-mail"); after 24 hours `UnconfirmedAccountCleanupService` deletes the account and, through the cascading foreign key, its bookings. Mail goes out through `ISiteEmailSender`: `SmtpEmailSender` (MailKit) when `Email:Smtp:Host` is set, otherwise `LoggingEmailSender`, in which case verification is off and new accounts are created confirmed. In Azure set the app settings `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__UserName`, `Email__Smtp__Password`, `Email__Smtp__FromAddress` and `Site__BaseUrl` (for example `https://www.frisorlangeland.dk`); never commit the password.
- **Admin.** `AdminController` provides the shop schedule and `BarbersController` provides barber CRUD. Both are restricted with `[Authorize(Roles = IdentitySeeder.AdminRole)]`.
- **Delete behavior.** Booking → Barber and Booking → Service are restrictive. Booking → Identity user is cascading (see `OnModelCreating`).

## Conventions

- Nullable reference types are on. Required references use `required`, and string properties usually default to `string.Empty`.
- EF access is async everywhere (`ToListAsync`, `FindAsync`, `SaveChangesAsync`, etc.).
- Write actions use `[ValidateAntiForgeryToken]` and explicit `[Bind(...)]` lists.
- Use tag helpers (`asp-controller`, `asp-action`, `asp-route-id`) rather than hard-coded URLs.
- Seed data is migration-managed. After changing it, or any entity, add a new migration. Don't hand-edit generated migrations or the model snapshot. Data-fix migrations (e.g. `ResolveDuplicateEmails`) are hand-written and sit beside the generated ones.
- Styling: Bootstrap plus `wwwroot/css/custom.css`. Don't add another CSS framework. Use the design tokens `--color-dark-slate #2B3A42`, `--color-warm-sand #E8DCC8`, `--color-sage-green #7C9885` (hover `#6B8775`), `--color-cream-white #FAF7F2` and `--bs-border-color #d9cfbe`. Keep the fixed navbar (90px desktop, 75px at ≤768px), the pill-shaped booking buttons, and the sage-green focus ring. Page JS goes in `wwwroot/js/site.js`.
- Home page hero: keep the full-width 75vh video, `object-fit: cover`, and the dark left-to-right overlay. Keep the existing asset paths in `wwwroot/images` and `wwwroot/videos`.
