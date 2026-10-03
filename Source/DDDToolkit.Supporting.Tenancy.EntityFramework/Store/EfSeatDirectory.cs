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
    public async Task<IReadOnlyList<SeatOfCaller<TTenantId, TSeatId>>> AllOfAsync(Guid identity, CancellationToken cancellationToken)
    {
        var found = await SeatsWithIdentity(identity, slug: null).ToListAsync(cancellationToken).ConfigureAwait(false);
        return found.Select(seat => seat.ToSeatOfCaller()).ToList();
    }

    /// <summary>The identity's seats with their tenants, in the tenant with <paramref name="slug"/> when there is one; past the tenant filter and no other.</summary>
    private IQueryable<FoundSeat> SeatsWithIdentity(Guid identity, TenantSlug? slug)
    {
        var tenants = context.Set<TTenant>().IgnoreQueryFilters([TenancyQueryFilter.Name]);
        if (slug is not null)
        {
            tenants = tenants.Where(tenant => tenant.Slug == slug);
        }

        return from seat in context.Set<TSeat>().IgnoreQueryFilters([TenancyQueryFilter.Name])
               where seat.Identity == identity
               join tenant in tenants on seat.TenantId equals tenant.Id
               join organization in context.Set<TOrganization>().IgnoreQueryFilters([TenancyQueryFilter.Name]) on seat.TenantId equals organization.Id
               select new FoundSeat(tenant.Id, tenant.Slug, organization.Name, tenant.Status, seat.Id, seat.DisplayName, seat.Status);
    }

    /// <summary>What the query reads, before the slug becomes the string the directory answers with.</summary>
    private sealed record FoundSeat(
        TTenantId Tenant,
        TenantSlug Slug,
        string OrganizationName,
        TenantStatus TenantStatus,
        TSeatId Seat,
        string DisplayName,
        SeatStatus SeatStatus)
    {
        public SeatOfCaller<TTenantId, TSeatId> ToSeatOfCaller()
            => new(Tenant, Slug.Value, OrganizationName, TenantStatus, Seat, DisplayName, SeatStatus);
    }
}
