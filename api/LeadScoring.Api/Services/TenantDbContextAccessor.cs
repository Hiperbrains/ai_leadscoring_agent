using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LeadScoring.Api.Services;

/// <summary>
/// All companies share the PostgreSQL <c>public</c> schema; isolation is by <c>CompanyName</c> / tenant id on rows.
/// </summary>
public class TenantDbContextAccessor(IConfiguration configuration) : ITenantDbContextAccessor
{
    public LeadScoringDbContext GetDbContext()
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        var options = new DbContextOptionsBuilder<LeadScoringDbContext>()
            .UseNpgsql(masterConnection, npg =>
                npg.MigrationsHistoryTable("__EFMigrationsHistory", TenantConnectionStringBuilder.SharedSchemaName))
            .AddInterceptors(new PublicSchemaConnectionInterceptor())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new LeadScoringDbContext(options);
    }
}
