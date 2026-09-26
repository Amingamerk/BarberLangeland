using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            // AddIdentity (not AddDefaultIdentity) so RoleManager is registered and the
            // Admin role can be seeded and used for [Authorize(Roles = "Admin")].
            // AddDefaultUI chains the Identity.UI login/register pages onto that builder.
            builder.Services
                .AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders()
                .AddDefaultUI();
            builder.Services.AddControllersWithViews();
            // AddIdentity no longer implies this the way AddDefaultIdentity did, but the
            // built-in Identity UI is served from Razor Pages.
            builder.Services.AddRazorPages();
            builder.Services.AddScoped<IBookingService, BookingService>();
            builder.Services.AddScoped<IBookingAvailabilityService, BookingAvailabilityService>();
            builder.Services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
            builder.Services.AddScoped<IPhoneIdentityService, PhoneIdentityService>();
            builder.Services.AddScoped<IdentitySeeder>();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                dbContext.Database.Migrate();

                var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
                var salon = app.Configuration.GetSection("Salon");
                // No fallback: the password is expected from user secrets or the environment,
                // and an absent one makes the seeder skip rather than guess one.
                await seeder.SeedAsync(
                    salon["AdminEmail"],
                    salon["AdminPassword"]);
            }

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            app.Run();
        }
    }
}
