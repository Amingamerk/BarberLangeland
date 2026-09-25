# Copilot instructions for BarberLangeland

## Project overview

BarberLangeland is a server-rendered ASP.NET Core MVC application targeting .NET 10. The solution contains one web project, `BarberLangeland/BarberLangeland.csproj`, and uses Razor views, Bootstrap, jQuery validation assets, ASP.NET Core Identity, and Entity Framework Core with SQL Server.

The public-facing experience is centered on `Views/Home/Index.cshtml`: it is primarily static marketing content with local video/images and links for booking, pricing, and company information. Conventional MVC routing is configured in `Program.cs` with `Home/Index` as the default route. Razor Pages are also mapped for the built-in Identity UI.

The current controller-backed feature is barber CRUD in `Controllers/BarbersController.cs` and `Views/Barber/*`. Booking domain objects and persistence logic exist, but there is no `BookingController` or booking view yet. `Services/BookingService.cs` creates a booking only when the barber exists and the requested time range does not overlap an existing booking.

## Build, run, and validation commands

Run commands from the repository root:

```powershell
dotnet restore .\BarberLangeland.slnx
dotnet build .\BarberLangeland.slnx
dotnet run --project .\BarberLangeland\BarberLangeland.csproj --launch-profile http
```

The configured development URLs are `http://localhost:5039` and `https://localhost:7040` (see `BarberLangeland/Properties/launchSettings.json`). Development startup enables the EF migrations endpoint, so the configured SQL Server must be available.

There is currently no test project in the solution and no lint/format command configured. Consequently, there is no single-test command yet; do not invent one. For code changes, at minimum run the solution build. If tests are added later, place them in a test project and document the project-specific `dotnet test --filter` syntax here.

Useful EF Core commands, run from the repository root:

```powershell
dotnet ef migrations add <MigrationName> --project .\BarberLangeland\BarberLangeland.csproj
dotnet ef database update --project .\BarberLangeland\BarberLangeland.csproj
```

The default connection is SQL Server Express in `BarberLangeland/appsettings.json`. Do not commit credentials or machine-specific connection strings; use user secrets or environment-specific configuration when needed.

## Architecture and data flow

- `Program.cs` configures dependency injection, SQL Server EF Core, Identity, MVC controllers/views, static assets, conventional routes, and Razor Pages.
- `Data/ApplicationDbContext.cs` is the persistence boundary. It derives from `IdentityDbContext<ApplicationUser>`, exposes `Bookings`, `Barbers`, and `Services`, configures booking foreign keys, and seeds the initial barber and services.
- `Models/` contains the EF/domain entities. `Booking` relates to `Barber`, `Service`, and an Identity user; booking duration is stored as `TimeSpan`, while service duration is stored as integer minutes.
- `Data/Migrations/` contains generated EF migrations and the model snapshot. Changes to entity relationships, properties, or seeded values should be reflected through a new migration rather than editing generated migration files manually.
- `Services/` contains booking business rules separate from controllers. `BookingService.CreateBookingAsync` returns `null` for an unknown barber or an overlapping booking and persists valid bookings asynchronously.
- `Controllers/` handles HTTP concerns and selects Razor views. The barber controller follows scaffolded async CRUD patterns, uses `[ValidateAntiForgeryToken]` on writes, and uses explicit `[Bind(...)]` property lists.
- `Views/Shared/_Layout.cshtml` owns the shared navigation/footer and loads Bootstrap, `site.css`, `custom.css`, jQuery, and Bootstrap JavaScript. Site-specific visual styling is concentrated in `wwwroot/css/custom.css`; page-specific behavior belongs in `wwwroot/js/site.js`.
- Static media is served from `wwwroot/images` and `wwwroot/videos`. Preserve the existing asset paths used by Razor views when changing content.

## Repository-specific conventions and caveats

- Use nullable reference types and the existing model style: required reference properties use `required`, optional values use nullable types, and simple string properties generally initialize to `string.Empty`.
- Keep EF access asynchronous in controllers and services (`ToListAsync`, `FindAsync`, `FirstOrDefaultAsync`, `AnyAsync`, `SaveChangesAsync`).
- Preserve the existing booking overlap invariant: a booking conflicts when its start is before an existing booking’s end and its end is after the existing booking’s start. Validate barber existence before saving.
- Keep booking relationships restrictive for barber/service deletion and cascading for the owning Identity user, as configured in `ApplicationDbContext.OnModelCreating`.
- Add or change seed data in `ApplicationDbContext.OnModelCreating`; because seed values are migration-managed, create and apply an EF migration after changing them.
- Use conventional MVC route/controller names and Razor tag helpers (`asp-controller`, `asp-action`, `asp-route-id`) rather than hard-coded application URLs where a controller action exists.
- Preserve Danish customer-facing copy and the existing visual theme in `custom.css`. Bootstrap is already the UI foundation; avoid introducing a second styling framework.
- Treat the following CSS custom properties as the design tokens:
  - `--color-dark-slate: #2B3A42` for body text, headings, footer backgrounds, and dark overlays.
  - `--color-warm-sand: #E8DCC8` for the page background, sections, and navbar.
  - `--color-sage-green: #7C9885` for primary actions, links, accents, and Bootstrap primary.
  - `--color-cream-white: #FAF7F2` for light text, cards, and light surfaces.
  - `--bs-border-color: #d9cfbe` for borders and separators.
- Keep the established component styling: rounded pill booking buttons, sage-green hover state `#6B8775`, dark-slate headings, cream-white cards, a fixed 90px desktop navbar, a 75px navbar on screens up to 768px, and body top padding that reserves space for the fixed header.
- Maintain the responsive behavior already present: sections use `60px 20px` padding by default and `40px 20px` below 768px; the logo is 60px square on desktop and 45px square on mobile; desktop navigation items are hidden below the large breakpoint.
- For the home page hero, preserve the full-width `75vh` video section, `object-fit: cover`, dark left-to-right overlay gradient, cream-white hero title/description, sage-green uppercase subtitle, and responsive wrapping of hero buttons.
- Reuse the existing focus treatment for buttons and form controls: a white inner ring followed by a sage-green outer ring. Keep footer text cream-white on a dark-slate background and links underlined there.
- The application currently has an Identity type mismatch to treat deliberately: `ApplicationDbContext` is based on `ApplicationUser`, while `Program.cs` registers `AddDefaultIdentity<IdentityUser>`. When changing authentication or booking ownership, keep the Identity generic types consistent across the context, registration, and `Booking.User`.
- The checked-in `Dockerfile` still references the old `Frisør_V2` project and assembly names. Update those references before relying on Docker builds; normal local builds use `BarberLangeland.slnx`.
