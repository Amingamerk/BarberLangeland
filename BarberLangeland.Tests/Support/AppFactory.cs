using System.Net;
using System.Text.RegularExpressions;
using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BarberLangeland.Tests.Support;

/// <summary>
/// The real application pipeline (routing, auth, antiforgery, views) running in-process in the
/// Production environment, with SQL Server swapped for an in-memory SQLite database.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Admin-Test-1234!";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AppFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        // The test clients appear as 10.x.x.x (see CreateBrowser); everything else is outside the allow-list.
        builder.UseSetting("Admin:AllowedIps", "10.0.0.0/8");
        builder.ConfigureServices(services =>
        {
            // EF Core 9+ registers the SQL Server options as IDbContextOptionsConfiguration<TContext>
            // (an internal type), so it is removed by name.
            foreach (var descriptor in services
                         .Where(d => d.ServiceType.IsGenericType
                                     && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")
                                     && d.ServiceType.GetGenericArguments()[0] == typeof(ApplicationDbContext))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));

            // Program.cs runs Database.Migrate() (SQL Server migrations) at start-up; create the
            // schema from the model first. The migration attempt then fails and is logged by Program.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
            using var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();
        });
    }

    private static int _clientCounter;

    /// <summary>
    /// A cookie-keeping client that appears to come from its own address (via X-Forwarded-For,
    /// as behind Azure), so the per-IP rate limit only affects the test that sets out to hit it.
    /// </summary>
    public HttpClient CreateBrowser(string? clientIp = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });
        var n = Interlocked.Increment(ref _clientCounter);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp ?? $"10.{n / 65000}.{n / 250 % 250}.{n % 250 + 1}");
        return client;
    }

    public async Task EnsureAdminAsync()
    {
        using var scope = Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync(IdentitySeeder.AdminRole))
        {
            await roles.CreateAsync(new IdentityRole(IdentitySeeder.AdminRole));
        }

        if (await users.FindByEmailAsync(AdminEmail) == null)
        {
            var admin = new ApplicationUser { UserName = $"user_{Guid.NewGuid():N}", Email = AdminEmail, EmailConfirmed = true };
            var created = await users.CreateAsync(admin, AdminPassword);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
            }

            await users.AddToRoleAsync(admin, IdentitySeeder.AdminRole);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public static class HttpExtensions
{
    private static readonly Regex TokenPattern = new(
        "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<string> GetTokenAsync(this HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = TokenPattern.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {url}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<string> BodyAsync(this HttpResponseMessage response)
        => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    public static Dictionary<string, string> Form(params (string Key, string Value)[] fields)
        => fields.ToDictionary(f => f.Key, f => f.Value);

    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string url, string token, params (string Key, string Value)[] fields)
    {
        var form = Form(fields);
        form["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(form));
    }
}
