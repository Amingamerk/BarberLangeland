using BarberLangeland.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BarberLangeland.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Booking> Bookings { get; set; }

        public DbSet<Barber> Barbers { get; set; }

        public DbSet<Service> Services { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Booking -> Barber
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.Barber)
                .WithMany()
                .HasForeignKey(b => b.BarberId)
                .OnDelete(DeleteBehavior.Restrict);

            // Booking -> Service
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.Service)
                .WithMany()
                .HasForeignKey(b => b.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Booking -> User (Identity)
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.User)
                .WithMany(u => u.Bookings)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Seed Services
            modelBuilder.Entity<Service>().HasData(
                new Service
                {
                    Id = 1,
                    Name = "Herreklip",
                    Description = "Klassisk herreklip",
                    DurationMinutes = 30,
                    Price = 250
                },
                new Service
                {
                    Id = 2,
                    Name = "Børneklip",
                    Description = "Klipning til børn",
                    DurationMinutes = 20,
                    Price = 180
                },
                new Service
                {
                    Id = 3,
                    Name = "Skægtrimning",
                    Description = "Trimning af skæg",
                    DurationMinutes = 15,
                    Price = 120
                }
            );

            // Seed Barbers
            modelBuilder.Entity<Barber>().HasData(
                new Barber
                {
                    Id = 1,
                    Name = "Bashar Al-Haj",
                    Title = "Frisør",
                    ImagePath = "/images/bashar.jpg"
                }
            );
        }
    }
}