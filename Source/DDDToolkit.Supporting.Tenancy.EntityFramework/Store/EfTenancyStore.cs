using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Where the use cases load and save, over Tenancy's context: one context is one unit of work. Every load goes
/// through the context's sets, so the tenant filter keeps it to the current caller's tenant, and a seat or role
/// of another tenant is not found; an application's own filters apply as well.
/// <para>
/// The checks that stand in for a unique index, whether a slug, an identity in a tenant or a role name in a
/// tenant is taken, look past every filter instead, because the index sees every row: a row a filter hides
/// would otherwise look free, and the insert would fail on the index rather than be refused. Each answers yes
/// or no, and the identity and the role name are asked within the caller's tenant.
/// </para>
/// <para>
/// The tenants' directory looks past the tenant filter alone, and runs as whoever asks: it is an operator's, and
/// what an operator may read is the database's to say where it keeps tenants apart.
/// </para>
/// <para>
/// A save that breaks one of Tenancy's unique indexes is refused with the code the index declares, by the
/// toolkit's own interceptor, on any database: the answer the use case gives for the same rule, with the
/// failure as its inner exception. Such a refusal, and any other, leaves the store as it arrived.
/// </para>
/// <para>
/// Any other failure of a save is offered to the registered <see cref="ITenancySaveFailures"/>, in order, and
/// the first refusal one of them makes of it is thrown instead, with the failure as its inner exception: where
/// the database keeps a rule no index states, such as one a trigger checks at commit, the caller still gets a
/// coded refusal. A translator that throws is a fault of its own, and is not taken for "not one I know".
/// </para>
/// <para>
/// A save that moves a seat's primary placement writes the demotion before the promotion, in one transaction:
/// with the context's database, a unique index keeps a seat to a single primary placement, and Entity Framework
/// orders the two rows by key, not by an index filter it cannot read.
/// </para>
/// <para>
/// What the use cases ask about every seat's rights, a tenant's administrators and the rights a move changes, is
/// read from the rights themselves, unless the database keeps them (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>)
/// and the caller is a seat: such a database shows a seat only its own rights, and answers these two questions
/// through functions of its own (<see cref="TenancyFunctionNames"/>), which the store then asks.
/// </para>
/// </summary>
internal sealed class EfTenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>(
    TContext context,
    IEnumerable<ITenancySaveFailures> failures,
    IOptions<TenancyStoreOptions> options)
    : TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.IStore
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TContext : DbContext
{
    private readonly ITenancySaveFailures[] _failures = [.. failures];

    /// <inheritdoc />
    public ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> Reads { get; }
        = new EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>(context, options.Value.DatabaseKeepsRights);

    /// <inheritdoc />
    public IQueryExecutor Queries => EfQueryExecutor.Instance;

    /// <inheritdoc />
    public Task<TTenant?> FindTenantAsync(TTenantId id, CancellationToken cancellationToken)
        => context.Set<TTenant>().Where(tenant => tenant.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);

        // Every tenant's slug, the caller's or not: a slug is unique across tenants. The answer is all that leaves.
        var taken = TenantSlug.Create(slug);
        return context.Set<TTenant>().IgnoreQueryFilters().AnyAsync(tenant => tenant.Slug == taken, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement, past the tenant filter and no other: the directory looks across tenants, and an
    /// application's own filter, a soft delete say, still hides what it hides. It runs as whoever is calling, so on
    /// a database that keeps tenants apart by itself the caller's own policies decide the rows. The seats are
    /// counted in the database, and nothing of a seat leaves it.
    /// </remarks>
    public async Task<IReadOnlyList<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.TenantListing>> ListTenantsAsync(
        string? afterSlug,
        int take,
        CancellationToken cancellationToken)
    {
        var tenants = context.Set<TTenant>().AsNoTracking().IgnoreQueryFilters([TenancyQueryFilter.Name]);
        if (afterSlug is not null)
        {
            tenants = tenants.Where(SlugComesAfter(TenantSlug.Create(afterSlug)));
        }

        var listed = from tenant in tenants
                     join organization in context.Set<TOrganization>() on tenant.Id equals organization.Id
                     orderby tenant.Slug
                     select new
                     {
                         tenant.Id,
                         tenant.Slug,
                         organization.Name,
                         tenant.Status,
                         ActiveSeats = context.Set<TSeat>().Count(seat => seat.TenantId.Equals(tenant.Id) && seat.Status == SeatStatus.Active),
                     };

        var rows = await listed.Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.TenantListing(
            row.Id, row.Slug.Value, row.Name, row.Status, row.ActiveSeats))];
    }

    /// <summary>
    /// <c>tenant =&gt; tenant.Slug &gt; after</c>, built from nodes: a slug is a value object, which C# does not
    /// order, while its column is text, which the database does. The slug it comes after is a parameter of the
    /// query, converted as the column is.
    /// </summary>
    private static Expression<Func<TTenant, bool>> SlugComesAfter(TenantSlug after)
    {
        Expression<Func<TenantSlug>> parameter = () => after;
        var tenant = Expression.Parameter(typeof(TTenant), "tenant");
        var slug = Expression.Property(tenant, nameof(TenantAggregate<TTenantId>.Slug));

        return Expression.Lambda<Func<TTenant, bool>>(Expression.GreaterThan(slug, parameter.Body, liftToNull: false, OrderOfSlugs), tenant);
    }

    /// <summary>The method the comparison of two slugs names, which an expression needs and no query ever calls.</summary>
    private static readonly MethodInfo OrderOfSlugs = typeof(EfTenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TContext>)
        .GetMethod(nameof(ComesAfter), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool ComesAfter(TenantSlug slug, TenantSlug other) => string.CompareOrdinal(slug.Value, other.Value) > 0;

    /// <inheritdoc />
    public Task<TOrganization?> FindOrganizationAsync(TTenantId id, CancellationToken cancellationToken)
        => context.Set<TOrganization>().Where(organization => organization.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A seat comes with its placements and their grants, two collections, in one query: a seat holds a few of
    /// each, so the rows they multiply to stay few, and the aggregate is read as one consistent whole rather than
    /// in statements that another save could come between. Saying so is also what keeps Entity Framework from
    /// warning that it had to choose.
    /// </remarks>
    public Task<TSeat?> FindSeatAsync(TSeatId id, CancellationToken cancellationToken)
        => context.Set<TSeat>().AsSingleQuery().Where(seat => seat.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// One statement over the seats' table, which is where a seat's name is. It selects the id, the name and the
    /// status and nothing else, so the identity never leaves the database, and it names the tenant next to the
    /// tenant filter: a person's seats in other tenants, which a database may let that person read, are not among
    /// the answer.
    /// </remarks>
    public async Task<IReadOnlyList<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.SeatSummary>> ListSeatsAsync(
        TTenantId tenant,
        IReadOnlyCollection<TSeatId>? only,
        CancellationToken cancellationToken)
    {
        var seats = context.Set<TSeat>().AsNoTracking().Where(seat => seat.TenantId.Equals(tenant));
        if (only is not null)
        {
            var asked = only.ToArray();
            seats = seats.Where(seat => asked.Contains(seat.Id));
        }

        return await seats
            .Select(seat => new TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.SeatSummary(seat.Id, seat.DisplayName, seat.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IdentityHasSeatAsync(TTenantId tenant, Guid identity, CancellationToken cancellationToken)
        => context.Set<TSeat>().IgnoreQueryFilters().AnyAsync(seat => seat.Identity == identity && seat.TenantId.Equals(tenant), cancellationToken);

    /// <inheritdoc />
    public Task<TRole?> FindRoleAsync(TRoleId id, CancellationToken cancellationToken)
        => context.Set<TRole>().Where(role => role.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TRole>> ListRolesAsync(TTenantId tenant, CancellationToken cancellationToken)
        => await context.Set<TRole>().Where(role => role.TenantId.Equals(tenant)).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<bool> RoleNameTakenAsync(TTenantId tenant, string name, TRoleId? except, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Against the column the unique index is on, filled in by the save: comparing in SQL would ignore case
        // only as far as the database's own lower case goes, which on SQLite is ASCII.
        var normalized = TenancyMapping.NormalizedRoleName(name);
        var roles = context.Set<TRole>()
            .IgnoreQueryFilters()
            .Where(role => role.TenantId.Equals(tenant) && EF.Property<string>(role, TenancyMapping.NormalizedNameColumn) == normalized);
        if (except is { } other)
        {
            roles = roles.Where(role => !role.Id.Equals(other));
        }

        return roles.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(TSeatId Seat, TRoleId Role)>> AdministratorsAsync(TTenantId tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (AsksTheDatabase)
        {
            // The function finds the tenant from the caller, as its seat's, and reads the database's own clock. It
            // answers a seat that may read other seats' grants, and any other seat nothing. For the use cases that
            // is the whole answer all the same: every command that could take an administrator away first asks a
            // key that makes its caller such a seat, the one for seats, for grants or for roles. Should the
            // database see the caller as someone else, its own trigger still keeps the tenant's last administrator
            // when the save commits.
            var answered = await context.Set<TenantAdministratorRow<TSeatId, TRoleId>>()
                .FromSqlRaw(TenancyFunctionSql.Select(
                    context,
                    schema: null,
                    TenancyFunctionNames.TenantAdministrators,
                    parameters: 0,
                    nameof(TenantAdministratorRow<TSeatId, TRoleId>.SeatId),
                    nameof(TenantAdministratorRow<TSeatId, TRoleId>.RoleId)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. answered.Select(row => (row.SeatId, row.RoleId))];
        }

        var administrators = from right in Reads.SeatRights
                             where right.TenantId.Equals(tenant) && right.Key == TenancyKeys.AdministratorKey
                                   && right.EndsAt == null && right.StartsAt <= now
                             join root in Reads.Units on right.UnitId equals root.Id
                             where root.TenantId.Equals(tenant) && !root.ParentId.HasValue
                             select new { right.SeatId, right.RoleId };

        var pairs = await administrators.ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. pairs.Select(pair => (pair.SeatId, pair.RoleId))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MoveReach<TUnitId>>> RightsAMoveChangesAsync(
        TTenantId tenant,
        TSeatId seat,
        TUnitId parent,
        TUnitId newParent,
        IReadOnlyCollection<string> managing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(managing);

        if (AsksTheDatabase)
        {
            // The function finds the tenant and the seat from the caller, knows which keys manage access from
            // the catalogue it was written from, and reads the database's own clock.
            var answered = await context.Set<MoveReach<TUnitId>>()
                .FromSqlRaw(
                    TenancyFunctionSql.Select(
                        context,
                        schema: null,
                        TenancyFunctionNames.RightsAMoveChanges,
                        parameters: 2,
                        nameof(MoveReach<TUnitId>.UnitId),
                        nameof(MoveReach<TUnitId>.Key),
                        nameof(MoveReach<TUnitId>.EndsAt),
                        nameof(MoveReach<TUnitId>.Parent),
                        nameof(MoveReach<TUnitId>.OfCaller)),
                    TenancyFunctionSql.Stored(context, parent),
                    TenancyFunctionSql.Stored(context, newParent))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // The function answers only a seat that manages units at both parents, and that seat's own rights for
            // that key are then among the rows. The use case found the calling seat to manage units at both
            // before it asked, so no row at all is one of two things, and never "the move changes nothing": taken
            // for that, it would let every move through, and nothing in the database checks a move's reach after it.
            if (answered.Count == 0)
            {
                // The database sees the seat, and by its own clock the seat no longer manages units at both: a
                // grant started or ended between the two reads, or inside what the two clocks differ by. That is
                // a race the caller lost, to be read again, as any other is.
                if (await DatabaseSeesTheSeatAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new ConcurrencyConflictException(
                        "The calling seat managed units at both parents when the move was checked, and by the database's clock no longer does: one of its grants started or ended in between. " +
                        "Nothing was changed. Read again, and send the command again.");
                }

                throw new InvalidOperationException(
                    "The database answered nothing about the rights a move of a unit changes. It answers a seat that manages units at both parents, which the calling seat " +
                    "was just found to do, and it does not see the seat the application acts as. The context that maps Tenancy's tables must send its commands as the " +
                    "signed-in user, in the seat's tenant: on Postgres, wire it with UseDDDToolkit once row level security is registered, or add UsePostgresRowLevelSecurity " +
                    "after UseDDDToolkitCore, and begin the user's caller around the command.");
            }

            return answered;
        }

        var marked = managing.ToArray();
        var reaching = from right in Reads.SeatRights
                       where right.TenantId.Equals(tenant) && (right.EndsAt == null || right.EndsAt > now)
                             && (right.SeatId.Equals(seat) || marked.Contains(right.Key))
                       join path in Reads.UnitPaths on right.UnitId equals path.AncestorId
                       where path.TenantId.Equals(tenant) && (path.DescendantId.Equals(parent) || path.DescendantId.Equals(newParent))
                       select new { right.SeatId, right.UnitId, right.Key, right.EndsAt, Reaches = path.DescendantId };

        var rows = await reaching.ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new MoveReach<TUnitId>(row.UnitId, row.Key, row.EndsAt, row.Reaches, row.SeatId.Equals(seat)))];
    }

    /// <summary>
    /// Whether the database finds a calling seat on this context's connection: what the function that answers
    /// the caller's seat says, which every one of Tenancy's database functions and policies asks first. It tells
    /// a seat whose rights changed between two reads from a connection that says nothing of who is calling.
    /// </summary>
    private async Task<bool> DatabaseSeesTheSeatAsync(CancellationToken cancellationToken)
    {
        var seen = await context.Database
            .SqlQueryRaw<bool>(TenancyFunctionSql.Answers(context, schema: null, TenancyFunctionSql.CallerSeat))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return seen is [true];
    }

    /// <summary>
    /// Whether what is asked about every seat's rights is asked of the database's functions: it keeps the rights,
    /// and the caller is a seat, which reads only its own. System work in a tenant reads every right of its tenant
    /// itself, and so does every caller of a database that leaves the rights to the save.
    /// </summary>
    private bool AsksTheDatabase
        => options.Value.DatabaseKeepsRights
           && context.Database.IsRelational()
           && TenancyCallers.Current<TTenantId, TSeatId>().Kind == TenancyCallerKind.Seat;

    /// <inheritdoc />
    public void Add(TTenant tenant) => context.Add(tenant);

    /// <inheritdoc />
    public void Add(TOrganization organization) => context.Add(organization);

    /// <inheritdoc />
    public void Add(TSeat seat) => context.Add(seat);

    /// <inheritdoc />
    public void Add(TRole role) => context.Add(role);

    /// <inheritdoc />
    public void AddAccessRevision(TTenantId tenant) => context.Add(new TenancyAccessRevision<TTenantId> { TenantId = tenant });

    /// <inheritdoc />
    public async Task SerializeAccessChangesAsync(TTenantId tenant, CancellationToken cancellationToken)
    {
        // Through the filtered set: the caller's tenant is the one it changes rights in. Read once per unit of
        // work; a second call finds the tracked row and bumps it again, which keeps the value first read as the
        // one the save compares.
        var revision = await context.Set<TenancyAccessRevision<TTenantId>>()
            .Where(row => row.TenantId.Equals(tenant))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The tenant " + tenant + " has no access revision the current caller can read. Every tenant gets one when it is provisioned.");

        revision.Revision++;
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        // Without Tenancy's interceptor, or with it ahead of the toolkit's, the save would write rows no check saw.
        TenancyChecks.EnsureWired(context);

        // A refusal is an answer already, a unique index's among them, and passes through unchanged. A failure
        // no translator knows is thrown on as it was.
        try
        {
            await SaveInOrderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not RefusalException && _failures.Length > 0)
        {
            if (Translate(failure) is { } refusal)
            {
                throw refusal;
            }

            throw;
        }
    }

    /// <summary>
    /// Saves the unit of work. When it makes a placement primary and demotes another, the demotions are written
    /// first and the promotions after them, in one transaction, so a unique index on the primary placements never
    /// sees a seat with two, whatever order Entity Framework would give the rows.
    /// </summary>
    private async Task SaveInOrderAsync(CancellationToken cancellationToken)
    {
        var promoted = context.ChangeTracker.Entries<Placement<TSeatId, TUnitId, TRoleId>>()
            .Where(entry => entry.State == EntityState.Modified && entry.Entity.IsPrimary && entry.Property(placement => placement.IsPrimary).IsModified)
            .ToList();
        if (promoted.Count == 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // One transaction for both writes: the caller's, or an ambient one, when there is one.
        var transaction = context.Database.CurrentTransaction is null && System.Transactions.Transaction.Current is null
            ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            try
            {
                foreach (var entry in promoted)
                {
                    var isPrimary = entry.Property(placement => placement.IsPrimary);
                    isPrimary.CurrentValue = false;
                    isPrimary.IsModified = false;
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // The aggregate says what it said, whether the first write went through or not.
                foreach (var entry in promoted)
                {
                    entry.Property(placement => placement.IsPrimary).CurrentValue = true;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The first refusal a registered translator makes of <paramref name="failure"/>, with the failure as its inner
    /// exception, or <see langword="null"/>.
    /// </summary>
    /// <exception cref="AggregateException">A translator threw: what it threw, and the failure it was asked about.</exception>
    private RefusalException? Translate(Exception failure)
    {
        foreach (var translator in _failures)
        {
            RefusalException? refusal;
            try
            {
                refusal = translator.Translate(failure, context);
            }
            catch (Exception fault)
            {
                throw new AggregateException(
                    $"{translator.GetType().Name} threw when it was asked whether a failed save of Tenancy's store is a refusal. " +
                    "The first inner exception is what it threw, the second the failure it was asked about.",
                    fault,
                    failure);
            }

            if (refusal is not null)
            {
                // Kept with the refusal, so a log of it says which rule of the database refused the save.
                return refusal.InnerException is null && refusal.GetType() == typeof(RefusalException)
                    ? new RefusalException(refusal.Code, refusal.Kind, refusal.Message, refusal.Arguments, failure)
                    : refusal;
            }
        }

        return null;
    }
}
