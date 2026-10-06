using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Finds a person's seats by their verified identity, in Tenancy's context. It is the one read that looks for
/// seats across tenants, past the tenant filter, because the caller has no tenant yet: that is what it is
/// finding. It never looks for anything but the identity it is given, and joins the tenant and its
/// organization only for the seats that identity has. The application's own filters still apply: a seat the
/// application hides is not found.
/// </summary>
internal sealed class EfSeatDirectory<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRoleId, TContext>(TContext context)
    : ISeatDirectory<TTenantId, TSeatId>
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task<SeatOfCaller<TTenantId, TSeatId>?> FindAsync(Guid identity, string tenantSlug, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantSlug);

        var found = await SeatsWithIdentity(identity, TenantSlug.Create(tenantSlug))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found?.ToSeatOfCaller();
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement, with the seats read whole beside their tenants, and tracked by nobody: nothing done to a seat is
    /// saved. The identity is still the one thing looked for past the tenant filter; the application's own filters
    /// apply.
    /// </remarks>
    public async Task<IReadOnlyList<SeatInTenant<TAsked>>> AllOfAsync<TAsked>(Guid identity, CancellationToken cancellationToken)
        where TAsked : class
    {
        if (!typeof(TAsked).IsAssignableFrom(typeof(TSeat)))
        {
            throw new InvalidOperationException(
                $"Tenancy's seats are {typeof(TSeat).FullName}, which is no {typeof(TAsked).FullName}: "
                + "a person's seats are answered as the seat class Tenancy was added with, or a class it derives from.");
        }

        var found = await (from seat in SeatsOf(identity)
                           join tenant in Tenants(slug: null) on seat.TenantId equals tenant.Id
                           join organization in Organizations() on seat.TenantId equals organization.Id
                           select new { Seat = seat, tenant.Slug, organization.Name, tenant.Status })
            .AsNoTracking()
            .AsSingleQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. found.Select(row => new SeatInTenant<TAsked>((TAsked)(object)row.Seat, row.Slug.Value, row.Name, row.Status))];
    }

    /// <summary>The identity's seats with their tenants, in the tenant with <paramref name="slug"/>; past the tenant filter and no other.</summary>
    private IQueryable<FoundSeat> SeatsWithIdentity(Guid identity, TenantSlug slug)
        => from seat in SeatsOf(identity)
           join tenant in Tenants(slug) on seat.TenantId equals tenant.Id
           join organization in Organizations() on seat.TenantId equals organization.Id
           select new FoundSeat(tenant.Id, tenant.Slug, organization.Name, tenant.Status, seat.Id, seat.Status);

    /// <summary>The identity's seats in every tenant: past the tenant filter, and no other.</summary>
    private IQueryable<TSeat> SeatsOf(Guid identity)
        => context.Set<TSeat>().IgnoreQueryFilters([TenancyQueryFilter.Name]).Where(seat => seat.Identity == identity);

    /// <summary>Every tenant, or the one with <paramref name="slug"/> when there is one: past the tenant filter, and no other.</summary>
    private IQueryable<TTenant> Tenants(TenantSlug? slug)
    {
        var tenants = context.Set<TTenant>().IgnoreQueryFilters([TenancyQueryFilter.Name]);
        return slug is null ? tenants : tenants.Where(tenant => tenant.Slug == slug);
    }

    /// <summary>Every organization, for the tenant's name: past the tenant filter, and no other.</summary>
    private IQueryable<TOrganization> Organizations()
        => context.Set<TOrganization>().IgnoreQueryFilters([TenancyQueryFilter.Name]);

    /// <summary>What the query reads, before the slug becomes the string the directory answers with.</summary>
    private sealed record FoundSeat(
        TTenantId Tenant,
        TenantSlug Slug,
        string OrganizationName,
        TenantStatus TenantStatus,
        TSeatId Seat,
        SeatStatus SeatStatus)
    {
        public SeatOfCaller<TTenantId, TSeatId> ToSeatOfCaller()
            => new(Tenant, Slug.Value, OrganizationName, TenantStatus, Seat, SeatStatus);
    }
}
