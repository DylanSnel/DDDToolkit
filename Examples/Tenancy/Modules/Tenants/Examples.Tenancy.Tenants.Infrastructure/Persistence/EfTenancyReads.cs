using System.Text.Json.Nodes;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Examples.Tenancy.Shared.Infrastructure.Paging;
using Examples.Tenancy.Tenants.Application.History;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using GreenDonut.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence;

/// <summary>
/// The application project's <see cref="ITenancyReads"/>: every read on storage of its own, never on the
/// request's context, which is the unit of work of its commands.
/// </summary>
/// <remarks>
/// A context runs one query at a time, and a request's queries may run side by side. So:
/// <list type="bullet">
/// <item>A reading takes a context from the factory the module registers next to its context, and disposes it
/// with the reading. The factory builds it with the options the request's own context has, so the tenant filter
/// and the caller it reads are the same.</item>
/// <item>The Tenancy package's directory and its tenant selection are the package's services. The container makes them
/// over the context of the scope they are resolved in, and nothing here can hand them another, so each question
/// to them gets a scope of its own, and with it a context of its own. The caller is not the scope's: Tenancy
/// keeps it with the flow of work, so it is the same inside.</item>
/// </list>
/// It holds no context and no state of its own, so one instance serves every query of a request, whichever run at
/// once.
/// <para>
/// Internal: the module's registration, in this project, is the only code that names it. Everything else asks for
/// the port.
/// </para>
/// </remarks>
/// <param name="contexts">Makes a context for one reading.</param>
/// <param name="scopes">Makes a scope for one question to the package.</param>
internal sealed class EfTenancyReads(IDbContextFactory<TenantsContext> contexts, IServiceScopeFactory scopes) : ITenancyReads
{
    /// <inheritdoc />
    public ITenancyReading Open() => new Reading(contexts.CreateDbContext());

    /// <inheritdoc />
    public async Task<TAnswer> AskDirectoryAsync<TAnswer>(Func<TenantsTenancy.TenancyDirectory, Task<TAnswer>> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        await using var scope = scopes.CreateAsyncScope();
        return await ask(scope.ServiceProvider.GetRequiredService<TenantsTenancy.TenancyDirectory>());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TView>> SeatsOfAsync<TView>(Caller caller, Func<SeatOfCaller<TenantId, SeatId>, Seat, TView> view, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>().SeatsOfAsync(caller, view, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TenantsTenancy.TenantDirectoryPage> TenantsAsync(string? after, int size, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenantsTenancy.TenantDirectory>().ListAsync(after, size, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Paged where the statement runs, by the index the history has on a tenant and the time a row was written:
    /// newest first, and rows written at one instant in the order of their ids, so a page starts exactly where the
    /// one before it ended. The marker is the paging library's own, made of those two values. One that is not
    /// such a marker is refused by code before the statement, by the check every module's paged read makes
    /// (<see cref="ListCursors"/>); what the database itself refuses is passed on as it is.
    /// </remarks>
    public async Task<Page<AccessHistoryEntry>> HistoryAsync(TenantId tenant, PagingArguments paging, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var page = await db.Set<EventLogEntry>().AsNoTracking()
            .Where(entry => EF.Property<TenantId>(entry, TenancyEventLogTable.TenantId) == tenant)
            .OrderByDescending(entry => entry.RecordedAt)
            .ThenByDescending(entry => entry.Id)
            .TakingOnlyItsOwnCursors(paging, () => TenancyRefusals.Of(TenancyRefusals.CursorInvalid))
            .ToPageAsync(paging, cancellationToken);

        return Page<AccessHistoryEntry>.Create(
            [.. page.Items.Select(Row)],
            page.Entries,
            page.HasNextPage,
            page.HasPreviousPage,
            (PageEntry<EventLogEntry> entry) => page.CreateCursor(entry),
            page.TotalCount);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The package lists invitations with the use cases that issue and cancel them. Asked here for the list
    /// alone, over the context of a scope of this read's own, never the request's unit of work.
    /// </remarks>
    public async Task<IReadOnlyList<TenantsTenancy.OpenInvitation<InvitationId>>> OpenInvitationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenantsTenancy.InvitationCommands<Invitation, InvitationId>>().ListOpenAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Three reads in one scope of this read's own, one after the other on its context: the seat with its
    /// placements and grants, the tenant's roles for their names, and the paths of the units it is placed in from
    /// the package's directory. Each is the caller's, under the policies: a seat of another tenant is not found.
    /// </remarks>
    public async Task<IReadOnlyList<SeatGrant>> GrantsOfAsync(SeatId seat, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TenantsTenancy.IStore>();

        if (await store.FindSeatAsync(seat, cancellationToken) is not { } found)
        {
            return [];
        }

        var roles = (await store.ListRolesAsync(found.TenantId, cancellationToken)).ToDictionary(role => role.Id, role => role.Name);
        var units = (await scope.ServiceProvider.GetRequiredService<TenantsTenancy.TenancyDirectory>()
                .UnitsByIdAsync([.. found.Placements.Select(placement => placement.UnitId).Distinct()], cancellationToken))
            .ToDictionary(unit => unit.Id, unit => unit.Path);
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        return
        [
            .. found.Placements
                .SelectMany(placement => placement.Grants.Select(grant => new SeatGrant(
                    placement.UnitId,
                    units.GetValueOrDefault(placement.UnitId, string.Empty),
                    grant.RoleId,
                    roles.GetValueOrDefault(grant.RoleId, string.Empty),
                    grant.StartsAt,
                    grant.EndsAt,
                    grant.AppliesAt(now))))
                .OrderBy(grant => grant.UnitPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Role, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// A row of the history as a query answers it. Who acted is the row's own columns, which the save wrote from
    /// its caller: a seat by its id, and an operator by the kind alone. The event says who acted as well, with an
    /// operator's identity, so that part of it is left out of what is answered.
    /// </summary>
    private static AccessHistoryEntry Row(EventLogEntry entry)
    {
        var details = entry.Payload;
        if (JsonNode.Parse(details) is JsonObject stored && stored.Remove(ByOfAnEvent))
        {
            details = stored.ToJsonString();
        }

        return new AccessHistoryEntry(
            entry.Id,
            entry.EventName,
            entry.OccurredAt,
            entry.ActedByKind,
            entry.ActedByKind is TenancyActorKinds.Seat or TenancyActorKinds.Token && Guid.TryParse(entry.ActedById, out var seat) ? new SeatId(seat) : null,
            details);
    }

    /// <summary>The member every event of Tenancy's ends with: who made the change, as the event itself carries it.</summary>
    private const string ByOfAnEvent = "By";

    /// <summary>Tenancy's rows on one context, which the reading owns.</summary>
    /// <param name="db">A context of the reading's own, from the factory.</param>
    private sealed class Reading(TenantsContext db) : ITenancyReading
    {
        /// <inheritdoc />
        public ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Rows { get; } =
            new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(db);

        /// <inheritdoc />
        /// <remarks>
        /// The reading's, not one from the container: whoever runs a query built over this context must run it
        /// with this context's provider.
        /// </remarks>
        public IQueryExecutor Queries => EfQueryExecutor.Instance;

        /// <inheritdoc />
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
