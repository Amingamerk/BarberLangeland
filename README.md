# Barber Langeland

A server-rendered ASP.NET Core MVC website for a barber shop, with online booking.

## Features

- Public pages: home, prices and about
- 4-step booking wizard (service, barber, date/time, contact details)
- Phone-number based customer sign-in and a "My bookings" page
- Admin area: shop schedule and barber management (create, edit, delete)
- Double-booking protection: a booking is only created if the time range doesn't overlap an existing one

## Tech stack

- .NET 10, ASP.NET Core MVC and Razor views
- ASP.NET Core Identity (with an `Admin` role)
- Entity Framework Core with SQL Server
- `libphonenumber-csharp` for phone number normalisation
- Bootstrap and jQuery validation

## Getting started

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a SQL Server instance
(the default connection string in `BarberLangeland/appsettings.json` points to SQL Server Express at `.\SQLEXPRESS`).

Run from the repository root:

```powershell
dotnet restore .\BarberLangeland.slnx
dotnet build .\BarberLangeland.slnx
dotnet run --project .\BarberLangeland\BarberLangeland.csproj --launch-profile http
```

The site is served at <http://localhost:5039> (or <https://localhost:7040> with the `https` profile).

### Admin account

The admin email is set by `Salon:AdminEmail` in `appsettings.json`. The password is never stored in the
repository; set it with user secrets before the first run:

```powershell
dotnet user-secrets set "Salon:AdminPassword" "<your-password>" --project .\BarberLangeland\BarberLangeland.csproj
```

### Database migrations

```powershell
dotnet ef migrations add <MigrationName> --project .\BarberLangeland\BarberLangeland.csproj
dotnet ef database update --project .\BarberLangeland\BarberLangeland.csproj
```

## Project layout

| Folder | Contents |
| --- | --- |
| `Controllers/` | Home, Booking, Account, Admin and Barbers controllers |
| `Services/` | Booking, availability, phone identity and admin seeding logic |
| `Models/`, `ViewModels/` | Entities and view models |
| `Data/` | `ApplicationDbContext` and EF Core migrations |
| `Views/` | Razor views and partials |

## Deployment

Pushes to `main` build and deploy the app to Azure App Service through
`.github/workflows/main_frisorlangeland.yml`.
