using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Finds a person's seats by their verified identity, in Tenancy's context. It is the one read that looks for
/// seats across tenants, past the tenant filter, because the caller has no tenant yet: that is what it is
/// finding. It never looks for anything but the identity it is given, and joins the tenant and what its
/// organization is called only for the seats that identity has. The application's own filters still apply: a seat
/// the application hides is not found. For an operator it finds the tenant a request names, by its slug alone.
/// <para>
/// On a database that keeps its own policies the names come from Tenancy's function that answers what the
/// identity's tenants are called, <see cref="TenancyFunctionNames.TenantNamesOfIdentity"/>, which runs as its owner:
/// a restrictive rule of the application's on the organization, which keeps a seat out of it, and which has no seat
/// to ask about before a tenant is chosen, leaves the names. Elsewhere they are read from the organizations.
/// </para>
/// </summary>
internal sealed class EfSeatDirectory<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRoleId, TContext>(
    TContext context,
    IOptions<TenancyStoreOptions> options)
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
    public async Task<SeatOfUser<TTenantId, TSeatId>?> FindAsync(Guid identity, string tenantSlug, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantSlug);

        var found = await SeatsWithIdentity(identity, TenantSlug.Create(tenantSlug))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found?.ToSeatOfUser();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Past the tenant filter, as the operator it is asked for reads every tenant. On a database that keeps its own
    /// policies the read runs as the operator's own database role, whose policy reads every tenant.
    /// </remarks>
    public async Task<TTenantId?> FindTenantAsync(string tenantSlug, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantSlug);

        var found = await Tenants(TenantSlug.Create(tenantSlug))
            .Select(tenant => tenant.Id)
            .Take(1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return found.Count == 0 ? null : found[0];
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

        // The name asked as a value of each row, not joined: a seat comes whole, with its collections, which a row
        // without a key beside it would keep Entity Framework from telling apart.
        var names = Names();
        var found = await (from seat in SeatsOf(identity)
                           join tenant in Tenants(slug: null) on seat.TenantId equals tenant.Id
                           select new
                           {
                               Seat = seat,
                               tenant.Slug,
                               Name = names.Where(named => named.Id.Equals(seat.TenantId)).Select(named => named.Name).FirstOrDefault(),
                               tenant.Status,
                           })
            .AsNoTracking()
            .AsSingleQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. found.Select(row => new SeatInTenant<TAsked>((TAsked)(object)row.Seat, row.Slug.Value, row.Name ?? row.Slug.Value, row.Status))];
    }

    /// <summary>
    /// The identity's seats with their tenants, in the tenant with <paramref name="slug"/>; past the tenant filter and
    /// no other. The organization only names the tenant, so a seat is found whether or not a name is; a tenant with no
    /// name is named by its slug.
    /// </summary>
    private IQueryable<FoundSeat> SeatsWithIdentity(Guid identity, TenantSlug slug)
    {
        var names = Names();
        return from seat in SeatsOf(identity)
               join tenant in Tenants(slug) on seat.TenantId equals tenant.Id
               select new FoundSeat(
                   tenant.Id,
                   tenant.Slug,
                   names.Where(named => named.Id.Equals(seat.TenantId)).Select(named => named.Name).FirstOrDefault(),
                   tenant.Status,
                   seat.Id,
                   seat.Status);
    }

    /// <summary>The identity's seats in every tenant: past the tenant filter, and no other.</summary>
    private IQueryable<TSeat> SeatsOf(Guid identity)
        => context.Set<TSeat>().IgnoreQueryFilters([TenancyQueryFilter.Name]).Where(seat => seat.Identity == identity);

    /// <summary>Every tenant, or the one with <paramref name="slug"/> when there is one: past the tenant filter, and no other.</summary>
    private IQueryable<TTenant> Tenants(TenantSlug? slug)
    {
        var tenants = context.Set<TTenant>().IgnoreQueryFilters([TenancyQueryFilter.Name]);
        return slug is null ? tenants : tenants.Where(tenant => tenant.Slug == slug);
    }

    /// <summary>
    /// What the tenants are called, by id: on a database that keeps its own policies, those of the caller's identity,
    /// from Tenancy's function that runs as its owner; elsewhere every organization's name, past the tenant filter and
    /// no other.
    /// </summary>
    private IQueryable<TenantNameRow<TTenantId>> Names()
        => options.Value.DatabaseKeepsRights && context.Database.IsRelational()
            ? context.Set<TenantNameRow<TTenantId>>().FromSqlRaw(
                TenancyFunctionSql.Select(
                    context,
                    schema: null,
                    TenancyFunctionNames.TenantNamesOfIdentity,
                    parameters: 0,
                    nameof(TenantNameRow<TTenantId>.Id),
                    nameof(TenantNameRow<TTenantId>.Slug),
                    nameof(TenantNameRow<TTenantId>.Name)))
            : context.Set<TOrganization>().IgnoreQueryFilters([TenancyQueryFilter.Name])
                .Select(organization => new TenantNameRow<TTenantId> { Id = organization.Id, Slug = string.Empty, Name = organization.Name });

    /// <summary>What the query reads, before the slug becomes the string the directory answers with.</summary>
    private sealed record FoundSeat(
        TTenantId Tenant,
        TenantSlug Slug,
        string? OrganizationName,
        TenantStatus TenantStatus,
        TSeatId Seat,
        SeatStatus SeatStatus)
    {
        public SeatOfUser<TTenantId, TSeatId> ToSeatOfUser()
            => new(Tenant, Slug.Value, OrganizationName ?? Slug.Value, TenantStatus, Seat, SeatStatus);
    }
}
