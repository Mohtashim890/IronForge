using IronForge.Application.Gym.Billing;
using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IronForge.Infrastructure.Persistence;

/// <summary>
/// Increments the gym's invoice counter in one statement, so concurrent
/// requests and the billing job never issue the same number. Inside a
/// transaction the increment rolls back with it.
/// </summary>
public sealed class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    private readonly IronForgeDbContext _dbContext;

    public InvoiceNumberGenerator(IronForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> NextAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var values = await _dbContext.Database
            .SqlQuery<int>(
                $"""
                UPDATE "GymSettings"
                SET "LastInvoiceNumber" = "LastInvoiceNumber" + 1
                WHERE "TenantId" = {tenantId}
                RETURNING "LastInvoiceNumber" AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return values.Count == 1
            ? values[0]
            : throw new InvalidOperationException(
                $"Gym settings for tenant {tenantId} were not found.");
    }
}
