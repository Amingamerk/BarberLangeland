using BarberLangeland.Data;
using BarberLangeland.Models;
using BarberLangeland.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BarberLangeland.Tests.Support;

/// <summary>Identity wired like Program.cs, on top of an in-memory SQLite database.</summary>
public sealed class IdentityHarness : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;

    public IdentityHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_db.Options().Extensions.OfType<Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal.SqliteOptionsExtension>().First().Connection!));
        services
            .AddIdentity<ApplicationUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = false)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
        services.AddScoped<IPhoneIdentityService, PhoneIdentityService>();

        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();

        var accessor = _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
    }

    public IPhoneIdentityService Phone => _scope.ServiceProvider.GetRequiredService<IPhoneIdentityService>();

    public UserManager<ApplicationUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        _db.Dispose();
    }
}
