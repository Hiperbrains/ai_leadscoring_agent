using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Data;

public class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CompanySubscription> Subscriptions => Set<CompanySubscription>();
    public DbSet<CompanyPayment> Payments => Set<CompanyPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().HasIndex(x => x.CompanyName).IsUnique();
        modelBuilder.Entity<Tenant>().HasIndex(x => x.DatabaseName).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<AppUser>()
            .HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanySubscription>(e =>
        {
            e.ToTable("Subscriptions");
            e.HasKey(x => x.SubscriptionId);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.CompanyId)
                .IsUnique()
                .HasFilter("\"IsActive\" = true");

            e.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyPayment>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(x => x.PaymentId);
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.SubscriptionId);
            e.HasIndex(x => x.PaymentDate);

            e.Property(x => x.Currency).HasMaxLength(10);

            e.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Subscription)
                .WithMany(s => s.Payments)
                .HasForeignKey(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
