using BarberLangeland.Data;
using BarberLangeland.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BarberLangeland.Tests.Support;

/// <summary>
/// An in-memory SQLite database with the real EF model (including the seeded barber and
/// services). With <paramref name="retryingStrategy"/> the context uses a retrying execution
/// strategy, mirroring the <c>EnableRetryOnFailure</c> configuration in Program.cs.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _retryingStrategy;

    public TestDb(bool retryingStrategy = false)
    {
        _retryingStrategy = retryingStrategy;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public ApplicationDbContext CreateContext() => new(Options());

    public DbContextOptions<ApplicationDbContext> Options()
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.UseSqlite(_connection, sqlite =>
        {
            if (_retryingStrategy)
            {
                sqlite.ExecutionStrategy(dependencies => new RetryingStrategy(dependencies));
            }
        });
        return builder.Options;
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Behaves like SqlServerRetryingExecutionStrategy for transaction purposes.</summary>
    private sealed class RetryingStrategy : ExecutionStrategy
    {
        public RetryingStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, 3, TimeSpan.FromMilliseconds(1))
        {
        }

        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}

public static class TestData
{
    public const int BarberId = 1;
    public const int HaircutServiceId = 1;   // 30 minutes
    public const int ChildCutServiceId = 2;  // 20 minutes
    public const int BeardServiceId = 3;     // 15 minutes

    /// <summary>A date at least a week ahead that falls on the given weekday.</summary>
    public static DateTime FutureDate(DayOfWeek day)
    {
        var date = DateTime.Today.AddDays(8);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    public static ApplicationUser AddUser(ApplicationDbContext context, string? id = null)
    {
        var user = new ApplicationUser
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            UserName = $"user_{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);
        context.SaveChanges();
        return user;
    }

    public static Booking AddBooking(
        ApplicationDbContext context,
        DateTime start,
        int serviceId = HaircutServiceId,
        bool cancelled = false,
        bool noShow = false,
        ApplicationUser? user = null)
    {
        user ??= AddUser(context);
        var service = context.Services.Find(serviceId)!;
        var booking = new Booking
        {
            BookingTime = start,
            Duration = TimeSpan.FromMinutes(service.DurationMinutes),
            BarberId = BarberId,
            Barber = context.Barbers.Find(BarberId)!,
            ServiceId = serviceId,
            Service = service,
            CustomerName = "Test",
            IsConfirmed = !cancelled && !noShow,
            IsCancelled = cancelled,
            IsNoShow = noShow,
            UserId = user.Id,
            User = user
        };
        context.Bookings.Add(booking);
        context.SaveChanges();
        return booking;
    }
}
