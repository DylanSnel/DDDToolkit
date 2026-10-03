using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Tenancy's part of every save, in Tenancy's own context and in every context with an entity kept to a tenant:
/// the save check. Every row the save adds, changes or deletes that belongs to a tenant must belong to the
/// current Tenancy caller's, so a use case that loaded or made the wrong row cannot write it.
/// <list type="table">
/// <listheader><term>Caller</term><description>What the check does</description></listheader>
/// <item><term>A seat, or system work in a tenant</term><description>Refuses a row of another tenant with <c>tenancy.other-tenant</c>.</description></item>
/// <item><term>Nobody</term><description>Refuses every such row with <c>tenancy.not-seated</c>.</description></item>
/// <item><term>System work outside any tenant</term><description>Refuses every such row with <c>tenancy.other-tenant</c>: it acts in no tenant, and provisioning saves as system work in the tenant it makes.</description></item>
/// </list>
/// Rows of owned types (units, placements, grants) are checked through the aggregate that owns them, and rows
/// of a derived type as rows of the type at the root of its hierarchy, which is the one kept to a tenant.
/// The check also refuses, on the first save, a model in which an entity type kept to a tenant has lost the
/// tenant filter.
/// <para>
/// Once the check has let the save through, it fills in who changed each row of an entity type that keeps it
/// (<see cref="TenancyAttribution.RecordsWhoChanged"/>): the actor of the Tenancy caller the save runs as, on a
/// new row as who wrote it first and who changed it last, on a changed row as who changed it last.
/// </para>
/// <para>
/// In Tenancy's own context it then writes, in the same save, what the access questions read: the closure of
/// every organization whose tree changed and the rights of every seat the save reaches, a role's holders
/// included. The rights the use cases check are therefore never behind the aggregates they were saved with.
/// Where the database keeps the rights itself (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), it writes
/// them, in the same transaction, and the save writes the closure alone.
/// </para>
/// <para>
/// <c>AddTenancy</c> on the service collection registers the one instance; <c>UseTenancy</c> adds it to a
/// context. It must come after <c>UseDDDToolkit</c>, so it sees what the domain event handlers changed and only
/// aggregates that passed their invariants; <see cref="TenancyChecks.EnsureWired"/> checks both.
/// </para>
/// </summary>
public abstract class TenancySaveInterceptor : SaveChangesInterceptor
{
    private const int MaxOwnershipDepth = 32;

    /// <summary>Only Tenancy derives from it, closed over the application's classes.</summary>
    private protected TenancySaveInterceptor()
    {
    }

    /// <summary>
    /// What is wrong with the accessor the application's services answer who acted with, for a context that keeps
    /// Tenancy's access history, or <see langword="null"/> when it is Tenancy's own: see
    /// <see cref="TenancyChecks.EnsureWired"/>.
    /// </summary>
    internal abstract string? ProblemWithWhoActed { get; }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            CheckTenants(context);
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            CheckTenants(context);
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Checks every row of <paramref name="context"/> the save would write against the current Tenancy caller.
    /// </summary>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.other-tenant</c> or <c>tenancy.not-seated</c>.</exception>
    /// <exception cref="InvalidOperationException">An entity type kept to a tenant has lost the tenant filter.</exception>
    private static void CheckTenants(DbContext context)
    {
        var facts = TenancyModelFacts.Of(context.Model);
        facts.RequireAttributedTypesAreScoped();
        if (!facts.HasScopedTypes)
        {
            return;
        }

        facts.RequireTenantFilters();

        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (ScopedEntryOf(context, entry) is not { } scoped)
            {
                continue;
            }

            var tenant = scoped.Property(TenantPropertyOf(scoped.Metadata)!);
            TenancySaveCheck.Check(tenant.CurrentValue
                ?? throw new InvalidOperationException("The " + scoped.Metadata.DisplayName() + " being saved has no tenant."));

            // A row moved from one tenant to another would otherwise pass as its new tenant's.
            if (scoped.State != EntityState.Added && tenant.IsModified && tenant.OriginalValue is { } original)
            {
                TenancySaveCheck.Check(original);
            }
        }

        // After the check, so a refused save changes nothing: every row left is of the caller's tenant.
        if (facts.RecordsWhoChanged)
        {
            RecordWhoChanged(context);
        }
    }

    /// <summary>
    /// Fills in who changed each row the save writes, of the entity types that keep it: on a new row both who
    /// wrote it first and who changed it last, on a changed row who changed it last. A part of a row that is
    /// tracked on its own, an owned type stored in its owner's table, changes its owner's row, so its owner says
    /// who did.
    /// </summary>
    /// <exception cref="InvalidOperationException">The columns were mapped for another seat id than the caller's.</exception>
    private static void RecordWhoChanged(DbContext context)
    {
        // The check let the save through, so the caller acts in a tenant, and has an actor.
        if (TenancyCallers.Ambient?.Actor is not { } actor)
        {
            return;
        }

        var kind = TenancyActorKinds.Of(actor.Kind);
        var seat = actor.Kind == TenancyActorKind.Seat ? actor.SeatId : null;
        var identity = actor.Kind == TenancyActorKind.Operator ? actor.Operator : null;

        // Listed first: filling a row in may change the state of its entry.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (RowOf(context, entry) is not { } row || row.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            // A new row says who wrote it first as well. Any row the save writes, for its own columns or for a
            // part's, says who changed it last.
            if (row.State == EntityState.Added)
            {
                Fill(row, TenancyAttribution.Created, seat, kind, identity);
            }

            Fill(row, TenancyAttribution.Changed, seat, kind, identity);
        }
    }

    /// <summary>
    /// The entry of the row <paramref name="entry"/> is stored in, when that row keeps who changed it: the entry
    /// itself, or, for an owned type stored in its owner's table, the owner that does. <see langword="null"/> for
    /// a row that does not keep it.
    /// </summary>
    private static EntityEntry? RowOf(DbContext context, EntityEntry entry)
    {
        var current = entry;
        for (var depth = 0; depth <= MaxOwnershipDepth; depth++)
        {
            if (TenancyModel.RecordsWhoChanged(current.Metadata))
            {
                return current;
            }

            if (current.Metadata.FindOwnership() is not { } ownership || !SharesItsOwnersTable(current.Metadata, ownership))
            {
                return null;
            }

            if (OwnerOf(context, current, ownership) is not { } owner)
            {
                return null;
            }

            current = owner;
        }

        return null;
    }

    /// <summary>Whether an owned type is stored in the table of its owner, as a part of the owner's row.</summary>
    private static bool SharesItsOwnersTable(IEntityType owned, IForeignKey ownership)
        => owned.IsMappedToJson()
           || (string.Equals(owned.GetTableName(), ownership.PrincipalEntityType.GetTableName(), StringComparison.Ordinal)
               && string.Equals(owned.GetSchema(), ownership.PrincipalEntityType.GetSchema(), StringComparison.Ordinal));

    /// <summary>Sets a seat, a kind and an identity on <paramref name="row"/>, in the three columns of <paramref name="columns"/>.</summary>
    private static void Fill(EntityEntry row, string[] columns, object? seat, string kind, Guid? identity)
    {
        var seatColumn = row.Property(columns[0]);
        var mapped = Nullable.GetUnderlyingType(seatColumn.Metadata.ClrType) ?? seatColumn.Metadata.ClrType;
        if (seat is not null && seat.GetType() != mapped)
        {
            throw new InvalidOperationException(
                "The " + row.Metadata.DisplayName() + " being saved keeps who changed it as a " + mapped.Name + ", and the seat that saves it is a " + seat.GetType().Name
                + ". Call RecordsWhoChanged with the seat id Tenancy is registered with.");
        }

        seatColumn.CurrentValue = seat;
        row.Property(columns[1]).CurrentValue = kind;
        row.Property(columns[2]).CurrentValue = identity;
    }

    /// <summary>
    /// The entry that says which tenant <paramref name="entry"/> belongs to: itself when its type is kept to a
    /// tenant, the aggregate that owns it when it is owned, or <see langword="null"/> when it is neither.
    /// </summary>
    private static EntityEntry? ScopedEntryOf(DbContext context, EntityEntry entry)
    {
        var current = entry;
        for (var depth = 0; depth <= MaxOwnershipDepth; depth++)
        {
            if (TenantPropertyOf(current.Metadata) is not null)
            {
                return current;
            }

            if (current.Metadata.FindOwnership() is not { } ownership)
            {
                return null;
            }

            current = OwnerOf(context, current, ownership)
                ?? throw new InvalidOperationException(
                    "The " + current.Metadata.DisplayName() + " being saved is owned, but its owner is not tracked, so its tenant cannot be checked.");
        }

        return null;
    }

    /// <summary>
    /// The property that holds the tenant of an entity of <paramref name="entityType"/>, or <see langword="null"/>
    /// when the type is not kept to a tenant. It is read from the root of the type's hierarchy: <c>ScopeToTenant</c>
    /// marks the type it is called on, a query filter can only sit on a root, and an annotation is not inherited,
    /// so a derived type would otherwise pass unchecked while its rows are filtered as the root's.
    /// </summary>
    private static string? TenantPropertyOf(IEntityType entityType)
        => entityType.GetRootType().FindAnnotation(TenancyMapping.TenantPropertyAnnotation)?.Value as string;

    /// <summary>
    /// The tracked owner of an owned entry. Owned entities have no navigation back to their owner, only the
    /// foreign key, so the state manager finds the owner from the key's values without touching the database.
    /// This is Entity Framework's internal API (EF1001), the same lookup the toolkit's version interceptor makes.
    /// </summary>
    private static EntityEntry? OwnerOf(DbContext context, EntityEntry owned, IForeignKey ownership)
    {
#pragma warning disable EF1001 // Internal EF Core API usage.
        var stateManager = context.GetService<IStateManager>();
        var owner = stateManager.FindPrincipal(owned.GetInfrastructure(), ownership);
        return owner is null ? null : new EntityEntry(owner);
#pragma warning restore EF1001
    }
}

/// <summary>
/// Tenancy's save interceptor, closed over the application's classes and ids: the one <c>AddTenancy</c> registers.
/// After the save check it writes, in Tenancy's own context, the closure and the rights the save's changes call
/// for (<see cref="TenancyProjectionWriter{TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId}"/>).
/// It comes after the check, so a refused save reads and writes nothing more.
/// </summary>
/// <param name="catalogue">Which keys are live, for the rights.</param>
/// <param name="store">What the store leaves to the database: the rights, where it keeps them itself.</param>
/// <param name="services">The application's services, asked once which accessor says who acted.</param>
internal sealed class TenancySaveInterceptor<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(
    TenancyCatalogue catalogue,
    IOptions<TenancyStoreOptions> store,
    IServiceProvider services)
    : TenancySaveInterceptor
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly TenancyProjectionWriter<TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> _writer = new(catalogue, store.Value);

    /// <summary>Asked the first time a context that keeps the history is checked: what is registered does not change afterwards.</summary>
    private readonly Lazy<string?> _actedByProblem = new(() => ProblemWith(services));

    /// <inheritdoc />
    internal override string? ProblemWithWhoActed => _actedByProblem.Value;

    /// <summary>
    /// The accessor is the last one registered, so one the application registers after <c>AddTenancy</c> takes
    /// the place of Tenancy's instead of being wrapped by it, without a word from the container.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The application's services cannot make the accessor, in a scope or outside one: the container's own
    /// reason, a service the accessor takes that nobody registered say, which is not this check's to rephrase.
    /// </exception>
    private static string? ProblemWith(IServiceProvider services)
    {
        const string fix =
            " Register the application's own accessor before AddTenancy, as a singleton: Tenancy then answers for its callers and asks that one for everybody else.";

        IActedByAccessor? accessor;
        var scoped = false;
        try
        {
            accessor = (IActedByAccessor?)services.GetService(typeof(IActedByAccessor));
        }
        catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
        {
            // The application's own services refuse to make a scoped service outside a scope, and a scope of
            // them says which accessor that is. Tenancy's is a singleton, so one a scope can make and they cannot
            // is the application's. Any other reason comes back from this second asking, as the container says it.
            using var scope = services.CreateScope();
            accessor = scope.ServiceProvider.GetService<IActedByAccessor>();
            scoped = true;
        }

        return accessor switch
        {
            TenancyActedByAccessor => null,
            null => $"{nameof(IActedByAccessor)} is not registered any more: {nameof(TenancyActedByAccessor)} was taken away after AddTenancy." + fix,
            _ => $"{nameof(IActedByAccessor)} is {accessor.GetType().Name}, registered after AddTenancy{(scoped ? " as a scoped service" : string.Empty)} in place of {nameof(TenancyActedByAccessor)}." + fix,
        };
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        result = base.SavingChanges(eventData, result);
        if (eventData.Context is { } context && !result.HasResult)
        {
            // Every read runs synchronously on this path, so the task has completed when it returns.
            _writer.WriteAsync(context, async: false, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        result = await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
        if (eventData.Context is { } context && !result.HasResult)
        {
            await _writer.WriteAsync(context, async: true, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
