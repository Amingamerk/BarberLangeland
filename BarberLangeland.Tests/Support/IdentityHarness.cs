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

    public IdentityHarness(bool emailConfigured = false, TimeSpan? tokenLifespan = null, TimeProvider? clock = null)
    {
        Email = new FakeEmailSender(emailConfigured);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_db.Connection));
        services
            .AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
        services.AddSingleton<ISiteEmailSender>(Email);
        services.AddSingleton<TimeProvider>(clock ?? new CopenhagenTimeProvider());
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = tokenLifespan ?? TimeSpan.FromHours(24));
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<ICustomerIdentityService, CustomerIdentityService>();

        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();

        var accessor = _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
    }

    public FakeEmailSender Email { get; }

    public ApplicationDbContext Db => _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    public IEmailVerificationService Verification => _scope.ServiceProvider.GetRequiredService<IEmailVerificationService>();

    public ICustomerIdentityService Customers => _scope.ServiceProvider.GetRequiredService<ICustomerIdentityService>();

    public UserManager<ApplicationUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        _db.Dispose();
    }
}
