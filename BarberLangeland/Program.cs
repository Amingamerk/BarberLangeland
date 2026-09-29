using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

namespace BarberLangeland
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 10,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
        }));
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            // AddIdentity (not AddDefaultIdentity) so RoleManager is registered and the
            // Admin role can be seeded and used for [Authorize(Roles = "Admin")].
            // AddDefaultUI chains the Identity.UI login/register pages onto that builder.
            builder.Services
                .AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = false;
                    // Applies to both sign-in paths: /Account/Login and the email sign-in on the
                    // booking form. Five wrong passwords lock the account for fifteen minutes.
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders()
                .AddDefaultUI();

            // Without this the cookie handler keeps its built-in defaults, so a Challenge from
            // any [Authorize] action sends the visitor to the Identity.UI Razor Page at
            // /Identity/Account/Login. That page signs in through PasswordSignInAsync, which
            // does a username lookup - and usernames here are opaque "user_<guid>" values, so
            // the lookup never matches and the page is a dead end. Pointing the challenge at
            // AccountController.Login is what makes [Authorize] sign-in actually possible.
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                // No AccessDenied view exists; the login page is the only sensible target, and
                // it states plainly what went wrong if we add one later.
                options.AccessDeniedPath = "/Account/Login";
            });

            // Azure terminates TLS and forwards the request, so without this every visitor would
            // share the proxy's address. ForwardLimit stays at its default of 1: only the address
            // appended by the trusted front end is used, never a value the client put in the header.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            // Throttles the unauthenticated endpoints that look up accounts or check passwords
            // (email check, booking form, login) per client address.
            var lookupsPerMinute = builder.Configuration.GetValue("RateLimiting:CustomerLookupPerMinute", 10);
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("customer-lookup", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = lookupsPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await context.HttpContext.Response.WriteAsync(
                        "For mange forsøg. Vent et øjeblik og prøv igen.", cancellationToken);
                };
            });

            builder.Services.AddControllersWithViews();
            // AddIdentity no longer implies this the way AddDefaultIdentity did, but the
            // built-in Identity UI is served from Razor Pages.
            builder.Services.AddRazorPages();
            builder.Services.AddScoped<IBookingService, BookingService>();
            builder.Services.AddScoped<IBookingAvailabilityService, BookingAvailabilityService>();
            builder.Services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
            builder.Services.AddScoped<ICustomerIdentityService, CustomerIdentityService>();
            builder.Services.AddScoped<IdentitySeeder>();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    dbContext.Database.Migrate();

                    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
                    var salon = app.Configuration.GetSection("Salon");

                    await seeder.SeedAsync(
                        salon["AdminEmail"],
                        salon["AdminPassword"]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            // Configure the HTTP request pipeline.
            app.UseForwardedHeaders();

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
            app.UseRateLimiter();

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
