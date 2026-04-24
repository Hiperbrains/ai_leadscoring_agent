using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Data;

public class LeadScoringDbContext(DbContextOptions<LeadScoringDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadEvent> Events => Set<LeadEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lead>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<LeadEvent>().HasIndex(x => new { x.LeadId, x.Type, x.TimestampUtc });
    }
}
