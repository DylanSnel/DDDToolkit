using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>
/// Tenancy's storage over lists, doing what the Entity Framework store does, so the use cases are tested
/// with no database:
/// <list type="bullet">
/// <item>It keeps what was saved apart from a unit of work: a command gets its own copies of what it loads,
/// and a refused command leaves the saved state as it was.</item>
/// <item>It emulates the tenant filter: every lookup and every read row keeps to the current Tenancy caller's
/// tenant, evaluated when the query runs; only <see cref="SlugTakenAsync"/> and <see cref="ListTenantsAsync"/>
/// look across tenants.</item>
/// <item>It emulates the save: <see cref="TenancySaveCheck"/> on every changed or added aggregate first, then
/// the invariants, then the closure and the rights recomputed with <see cref="TenancyProjection"/>, the same
/// functions the Entity Framework writer uses.</item>
/// <item>It records the order of the calls made to it, so a test sees what a command read first, and who was
/// calling at each, Tenancy's caller and the toolkit's, so a test sees what a command ran as.</item>
/// <item>With <see cref="KeepsOtherSeatsRights"/> it shows a seat only its own rights, as a database that keeps the
/// rights itself does, while it still answers what the use cases ask about every seat's: the administrators, and
/// the rights a move changes.</item>
/// </list>
/// </summary>
public sealed class InMemoryTenancyStore
    : HostTenancy.IStore,
      HostTenancy.IInvitationStore<HostInvitation, InvitationId>,
      ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>,
      IQueryExecutor
{
    private readonly TenancyCatalogue _catalogue;

    // What was saved.
    private readonly Dictionary<TenantId, HostTenant> _tenants = [];
    private readonly Dictionary<TenantId, HostOrganization> _organizations = [];
    private readonly Dictionary<SeatId, HostSeat> _seats = [];
    private readonly Dictionary<RoleId, HostRole> _roles = [];
    private readonly Dictionary<TenantId, long> _revisions = [];
    private readonly Dictionary<InvitationId, HostInvitation> _invitations = [];

    // The digests of the invitations' tokens, apart from the invitations: by the digest, as hexadecimal text.
    private readonly Dictionary<string, InvitationId> _digests = new(StringComparer.Ordinal);
    private List<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>> _rights = [];
    private List<OrganizationUnitPath<TenantId, OrganizationUnitId>> _paths = [];

    // The unit of work: the copies it loaded, by the saved instance, and what it added.
    private readonly Dictionary<object, object> _loaded = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _added = [];
    private readonly List<(InvitationId Invitation, byte[] Digest)> _addedDigests = [];
    private readonly HashSet<TenantId> _newRevisions = [];
    private readonly HashSet<TenantId> _takenRevisions = [];

    private readonly List<string> _calls = [];
    private readonly List<StoreCall> _observed = [];
    private readonly List<IDomainEvent> _saved = [];

    // The read rows of the caller's tenant, every seat's rights included: what the store itself answers from.
    private readonly ReadRows _whole;

    /// <summary>A store whose rights are worked out with <paramref name="catalogue"/>.</summary>
    public InMemoryTenancyStore(TenancyCatalogue catalogue)
    {
        _catalogue = catalogue;
        Filtered = new ReadRows(this, filtered: true, mayKeepRights: true);
        Unfiltered = new ReadRows(this, filtered: false, mayKeepRights: false);
        _whole = new ReadRows(this, filtered: true, mayKeepRights: false);
    }

    /// <summary>
    /// Whether the read rows show a seat only its own rights, and system work every right of its tenant: what a
    /// database that keeps the rights itself lets each read. What the store answers about every seat's rights is
    /// not held to it.
    /// </summary>
    public bool KeepsOtherSeatsRights { get; set; }

    /// <summary>The calls made since the unit of work began, in order.</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>Every call made to the store since it was made, whatever the unit of work, with who was calling.</summary>
    public IReadOnlyList<StoreCall> Observed => _observed;

    /// <summary>Every domain event saved so far, in order: what an outbox would hold.</summary>
    public IReadOnlyList<IDomainEvent> SavedEvents => _saved;

    /// <summary>How many times a unit of work was saved.</summary>
    public int SaveCount { get; private set; }

    /// <summary>The read rows as the tenant filter shows them to the current caller.</summary>
    public ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Filtered { get; }

    /// <summary>The read rows of every tenant, with no filter: to see that the questions keep to the caller's tenant themselves.</summary>
    public ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Unfiltered { get; }

    /// <summary>The saved rights, of every tenant.</summary>
    public IReadOnlyList<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>> SavedRights => _rights;

    /// <summary>The saved closure, of every tenant.</summary>
    public IReadOnlyList<OrganizationUnitPath<TenantId, OrganizationUnitId>> SavedPaths => _paths;

    // ---------------------------------------------------------------- for tests

    /// <summary>Starts a new unit of work: forgets what the last one loaded, added and called.</summary>
    public void BeginUnitOfWork()
    {
        _loaded.Clear();
        _added.Clear();
        _addedDigests.Clear();
        _newRevisions.Clear();
        _takenRevisions.Clear();
        _calls.Clear();
    }

    /// <summary>
    /// Puts aggregates in the store as if they had been saved, with no caller and no check, and an access
    /// revision for every tenant among them. Their pending events are dropped.
    /// </summary>
    public void Seed(params object[] aggregates)
    {
        foreach (var aggregate in aggregates)
        {
            ((IHasDomainEvents)aggregate).ClearDomainEvents();
            Commit(aggregate);
            _revisions.TryAdd(TenantOf(aggregate), 0);
        }

        Project();
    }

    /// <summary>The saved tenant, to assert on.</summary>
    public HostTenant Tenant(TenantId id) => _tenants[id];

    /// <summary>The saved organization, to assert on.</summary>
    public HostOrganization Organization(TenantId id) => _organizations[id];

    /// <summary>The saved seat, to assert on.</summary>
    public HostSeat Seat(SeatId id) => _seats[id];

    /// <summary>The saved role, to assert on.</summary>
    public HostRole Role(RoleId id) => _roles[id];

    /// <summary>The saved invitation, to assert on.</summary>
    public HostInvitation Invitation(InvitationId id) => _invitations[id];

    /// <summary>The saved invitations of a tenant.</summary>
    public IReadOnlyList<HostInvitation> InvitationsOf(TenantId tenant) => [.. _invitations.Values.Where(invitation => invitation.TenantId == tenant)];

    /// <summary>The digests kept for the saved invitations, each as hexadecimal text: all the store has of a token.</summary>
    public IReadOnlyCollection<string> KeptDigests => _digests.Keys;

    /// <summary>The saved roles of a tenant.</summary>
    public IReadOnlyList<HostRole> RolesOf(TenantId tenant) => [.. _roles.Values.Where(role => role.TenantId == tenant)];

    /// <summary>The saved seats of a tenant.</summary>
    public IReadOnlyList<HostSeat> SeatsIn(TenantId tenant) => [.. _seats.Values.Where(seat => seat.TenantId == tenant)];

    /// <summary>The tenant's access revision as saved.</summary>
    public long RevisionOf(TenantId tenant) => _revisions[tenant];

    /// <summary>Whether a tenant with this id was saved.</summary>
    public bool HasTenant(TenantId id) => _tenants.ContainsKey(id);

    // ---------------------------------------------------------------- IStore

    ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> HostTenancy.IStore.Reads
    {
        get
        {
            Record("Reads");
            return Filtered;
        }
    }

    IQueryExecutor HostTenancy.IStore.Queries
    {
        get
        {
            Record("Queries");
            return this;
        }
    }

    public Task<HostTenant?> FindTenantAsync(TenantId id, CancellationToken cancellationToken)
    {
        Record(nameof(FindTenantAsync));
        return Task.FromResult(_tenants.TryGetValue(id, out var tenant) && Visible(tenant.Id) ? LoadedCopy(tenant) : null);
    }

    public Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken)
    {
        Record(nameof(SlugTakenAsync));
        return Task.FromResult(_tenants.Values.Any(tenant => tenant.Slug.Value == slug));
    }

    public Task<HostOrganization?> FindOrganizationAsync(TenantId id, CancellationToken cancellationToken)
    {
        Record(nameof(FindOrganizationAsync));
        return Task.FromResult(_organizations.TryGetValue(id, out var organization) && Visible(organization.Id) ? LoadedCopy(organization) : null);
    }

    public Task<HostSeat?> FindSeatAsync(SeatId id, CancellationToken cancellationToken)
    {
        Record(nameof(FindSeatAsync));
        return Task.FromResult(_seats.TryGetValue(id, out var seat) && Visible(seat.TenantId) ? LoadedCopy(seat) : null);
    }

    public Task<IReadOnlyList<HostTenancy.TenantListing>> ListTenantsAsync(string? afterSlug, int take, CancellationToken cancellationToken)
    {
        // Across tenants, as the store's own does: who may ask is the use case's to say.
        Record(nameof(ListTenantsAsync));
        IReadOnlyList<HostTenancy.TenantListing> tenants =
        [
            .. _tenants.Values
                .Where(tenant => afterSlug is null || string.CompareOrdinal(tenant.Slug.Value, afterSlug) > 0)
                .OrderBy(tenant => tenant.Slug.Value, StringComparer.Ordinal)
                .Take(take)
                .Select(tenant => new HostTenancy.TenantListing(
                    tenant.Id,
                    tenant.Slug.Value,
                    _organizations[tenant.Id].Name,
                    tenant.Status,
                    _seats.Values.Count(seat => seat.TenantId == tenant.Id && seat.Status == SeatStatus.Active))),
        ];
        return Task.FromResult(tenants);
    }

    public Task<IReadOnlyList<HostSeat>> ListSeatsAsync(TenantId tenant, IReadOnlyCollection<SeatId>? only, CancellationToken cancellationToken)
    {
        Record(nameof(ListSeatsAsync));

        // Copies the unit of work does not track, as a database's read for a view is: what a view does to one is
        // never saved.
        IReadOnlyList<HostSeat> seats =
        [
            .. _seats.Values
                .Where(seat => Visible(seat.TenantId) && seat.TenantId == tenant && (only is null || only.Contains(seat.Id)))
                .Select(Copies.Of),
        ];
        return Task.FromResult(seats);
    }

    public Task<bool> IdentityHasSeatAsync(TenantId tenant, Guid identity, CancellationToken cancellationToken)
    {
        Record(nameof(IdentityHasSeatAsync));
        return Task.FromResult(_seats.Values.Any(seat => Visible(seat.TenantId) && seat.TenantId == tenant && seat.Identity == identity));
    }

    public Task<HostRole?> FindRoleAsync(RoleId id, CancellationToken cancellationToken)
    {
        Record(nameof(FindRoleAsync));
        return Task.FromResult(_roles.TryGetValue(id, out var role) && Visible(role.TenantId) ? LoadedCopy(role) : null);
    }

    public Task<IReadOnlyList<HostRole>> ListRolesAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        Record(nameof(ListRolesAsync));
        IReadOnlyList<HostRole> roles = [.. _roles.Values.Where(role => Visible(role.TenantId) && role.TenantId == tenant).Select(LoadedCopy)];
        return Task.FromResult(roles);
    }

    public Task<bool> RoleNameTakenAsync(TenantId tenant, string name, RoleId? except, CancellationToken cancellationToken)
    {
        Record(nameof(RoleNameTakenAsync));
        return Task.FromResult(_roles.Values.Any(role => Visible(role.TenantId) && role.TenantId == tenant && role.Id != except
                                                         && string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<IReadOnlyList<(SeatId Seat, RoleId Role)>> AdministratorsAsync(TenantId tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Record(nameof(AdministratorsAsync));
        var roots = _whole.Units.AsEnumerable().Where(unit => unit.TenantId == tenant && unit.ParentId is null).Select(unit => unit.Id).ToHashSet();
        IReadOnlyList<(SeatId Seat, RoleId Role)> pairs =
        [
            .. _whole.SeatRights.AsEnumerable()
                .Where(right => right.TenantId == tenant && roots.Contains(right.UnitId) && right.Key == TenancyKeys.AdministratorKey
                                && right.EndsAt is null && right.StartsAt <= now)
                .Select(right => (right.SeatId, right.RoleId)),
        ];
        return Task.FromResult(pairs);
    }

    public Task<IReadOnlyList<MoveReach<OrganizationUnitId>>> RightsAMoveChangesAsync(
        TenantId tenant,
        SeatId seat,
        OrganizationUnitId parent,
        OrganizationUnitId newParent,
        IReadOnlyCollection<string> managing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Record(nameof(RightsAMoveChangesAsync));
        var paths = _whole.UnitPaths.AsEnumerable().Where(path => path.TenantId == tenant && (path.DescendantId == parent || path.DescendantId == newParent)).ToList();
        bool ReachesBoth(OrganizationUnitId unit)
            => paths.Any(path => path.AncestorId == unit && path.DescendantId == parent) && paths.Any(path => path.AncestorId == unit && path.DescendantId == newParent);

        // Another seat's right only where it reaches one parent and not the other, as the stores answer it.
        IReadOnlyList<MoveReach<OrganizationUnitId>> reaches =
        [
            .. from right in _whole.SeatRights.AsEnumerable()
               where right.TenantId == tenant && (right.EndsAt is null || right.EndsAt > now)
                     && (right.SeatId == seat || (managing.Contains(right.Key) && !ReachesBoth(right.UnitId)))
               from path in paths
               where path.AncestorId == right.UnitId
               select new MoveReach<OrganizationUnitId>(right.UnitId, right.Key, right.EndsAt, path.DescendantId, right.SeatId == seat),
        ];
        return Task.FromResult(reaches);
    }

    // ---------------------------------------------------------------- IInvitationStore

    public Task<HostInvitation?> FindAsync(InvitationId id, CancellationToken cancellationToken)
    {
        Record(nameof(FindAsync));
        return Task.FromResult(_invitations.TryGetValue(id, out var invitation) && Visible(invitation.TenantId) ? LoadedCopy(invitation) : null);
    }

    public Task<IReadOnlyList<HostInvitation>> ListOpenAsync(TenantId tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Copies of their own, and no part of the unit of work: a list is read, and nothing of it is saved.
        Record(nameof(ListOpenAsync));
        IReadOnlyList<HostInvitation> open =
        [
            .. _invitations.Values
                .Where(invitation => Visible(invitation.TenantId) && invitation.TenantId == tenant && invitation.IsOpenAt(now))
                .Select(Copies.Of),
        ];
        return Task.FromResult(open);
    }

    public Task<HostTenancy.InvitationOfToken<InvitationId>?> FindByDigestAsync(byte[] digest, CancellationToken cancellationToken)
    {
        // Across tenants, as the store's own does: the token is what names the tenant. Ids alone are answered.
        Record(nameof(FindByDigestAsync));
        return Task.FromResult(
            _digests.TryGetValue(Convert.ToHexString(digest), out var id) && _invitations.TryGetValue(id, out var invitation)
                ? new HostTenancy.InvitationOfToken<InvitationId>(invitation.TenantId, invitation.Id, invitation.IssuedBy)
                : null);
    }

    public void Add(HostInvitation invitation, byte[] digest)
    {
        AddToUnitOfWork(invitation);
        _addedDigests.Add((invitation.Id, digest));
    }

    public void Add(HostTenant tenant) => AddToUnitOfWork(tenant);

    public void Add(HostOrganization organization) => AddToUnitOfWork(organization);

    public void Add(HostSeat seat) => AddToUnitOfWork(seat);

    public void Add(HostRole role) => AddToUnitOfWork(role);

    public void AddAccessRevision(TenantId tenant)
    {
        Record(nameof(AddAccessRevision));
        _newRevisions.Add(tenant);
    }

    public Task SerializeAccessChangesAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        Record(nameof(SerializeAccessChangesAsync));
        if (!Visible(tenant) || !_revisions.ContainsKey(tenant))
        {
            throw new InvalidOperationException("No access revision of " + tenant + " can be read by the current caller.");
        }

        _takenRevisions.Add(tenant);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        Record(nameof(SaveAsync));

        var changed = _loaded.Values.Concat(_added).ToArray();
        foreach (var aggregate in changed)
        {
            TenancySaveCheck.Check(TenantOf(aggregate));
        }

        foreach (var tenant in _newRevisions.Concat(_takenRevisions))
        {
            TenancySaveCheck.Check(tenant);
        }

        foreach (var aggregate in changed)
        {
            ((IHasInvariants)aggregate).EnsureInvariants();
        }

        foreach (var aggregate in _added)
        {
            if (Saved(aggregate))
            {
                throw new InvalidOperationException("A " + aggregate.GetType().Name + " with the same key is already saved.");
            }
        }

        foreach (var aggregate in changed)
        {
            _saved.AddRange(((IHasDomainEvents)aggregate).DequeueDomainEvents());
            Commit(aggregate);
        }

        foreach (var (invitation, digest) in _addedDigests)
        {
            _digests.Add(Convert.ToHexString(digest), invitation);
        }

        foreach (var tenant in _newRevisions)
        {
            _revisions.Add(tenant, 0);
        }

        foreach (var tenant in _takenRevisions)
        {
            _revisions[tenant]++;
        }

        Project();
        SaveCount++;

        _loaded.Clear();
        _added.Clear();
        _addedDigests.Clear();
        _newRevisions.Clear();
        _takenRevisions.Clear();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- IQueryExecutor

    public Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Record(nameof(AnyAsync));
        return Task.FromResult(query.Any());
    }

    public Task<IReadOnlyList<T>> ListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Record(nameof(ListAsync));
        IReadOnlyList<T> rows = [.. query];
        return Task.FromResult(rows);
    }

    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        Record(nameof(FirstOrDefaultAsync));
        return Task.FromResult(query.FirstOrDefault());
    }

    // ---------------------------------------------------------------- ITenancyReadSource, filtered

    IQueryable<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>> ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>.SeatRights => Filtered.SeatRights;

    IQueryable<OrganizationUnitPath<TenantId, OrganizationUnitId>> ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>.UnitPaths => Filtered.UnitPaths;

    IQueryable<OrganizationUnitRow<TenantId, OrganizationUnitId>> ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>.Units => Filtered.Units;

    IQueryable<RoleRow<TenantId, RoleId>> ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>.Roles => Filtered.Roles;

    IQueryable<PlacementRow<SeatId, OrganizationUnitId>> ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>.Placements => Filtered.Placements;

    IQueryable<SeatRow<TenantId, SeatId>> ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>.Seats => Filtered.Seats;

    // ---------------------------------------------------------------- the rest

    /// <summary>Whether the tenant filter lets the current caller see a row of <paramref name="tenant"/>.</summary>
    private static bool Visible(TenantId tenant) => TenancyCallers.CurrentTenantOrNull() is TenantId current && current == tenant;

    private static TenantId TenantOf(object aggregate) => aggregate switch
    {
        HostTenant tenant => tenant.Id,
        HostOrganization organization => organization.Id,
        HostSeat seat => seat.TenantId,
        HostRole role => role.TenantId,
        HostInvitation invitation => invitation.TenantId,
        _ => throw new ArgumentException("Not a Tenancy aggregate: " + aggregate.GetType().Name, nameof(aggregate)),
    };

    /// <summary>This unit of work's copy of a saved aggregate: the same copy every time it is loaded again.</summary>
    private T LoadedCopy<T>(T saved)
        where T : class
    {
        if (!_loaded.TryGetValue(saved, out var copy))
        {
            _loaded[saved] = copy = Copies.Of(saved);
        }

        return (T)copy;
    }

    /// <summary>Records a call, and who made it.</summary>
    private void Record(string call)
    {
        _calls.Add(call);
        _observed.Add(new StoreCall(call, TenancyCallers.Ambient, Callers.Ambient));
    }

    private void AddToUnitOfWork(object aggregate)
    {
        Record(nameof(Add));
        _added.Add(aggregate);
    }

    private bool Saved(object aggregate) => aggregate switch
    {
        HostTenant tenant => _tenants.ContainsKey(tenant.Id),
        HostOrganization organization => _organizations.ContainsKey(organization.Id),
        HostSeat seat => _seats.ContainsKey(seat.Id),
        HostRole role => _roles.ContainsKey(role.Id),
        HostInvitation invitation => _invitations.ContainsKey(invitation.Id),
        _ => false,
    };

    private void Commit(object aggregate)
    {
        switch (aggregate)
        {
            case HostTenant tenant:
                _tenants[tenant.Id] = tenant;
                break;
            case HostOrganization organization:
                _organizations[organization.Id] = organization;
                break;
            case HostSeat seat:
                _seats[seat.Id] = seat;
                break;
            case HostRole role:
                _roles[role.Id] = role;
                break;
            case HostInvitation invitation:
                _invitations[invitation.Id] = invitation;
                break;
            default:
                throw new ArgumentException("Not a Tenancy aggregate: " + aggregate.GetType().Name, nameof(aggregate));
        }
    }

    /// <summary>The closure and the rights, worked out again from everything saved.</summary>
    private void Project()
    {
        _paths = [.. _organizations.Values.SelectMany(organization => TenancyProjection.ClosureOf(organization))];
        _rights =
        [
            .. _seats.Values.SelectMany(seat => TenancyProjection.RightsOf(
                seat.TenantId,
                seat.Id,
                seat.Status,
                TenancyProjection.GrantsOf(seat),
                role => _roles.TryGetValue(role, out var found) && found.TenantId == seat.TenantId ? found.Facts : null,
                _catalogue)),
        ];
    }

    /// <summary>The read rows of what was saved, with the tenant filter or without it, evaluated when a query runs.</summary>
    private sealed class ReadRows(InMemoryTenancyStore store, bool filtered, bool mayKeepRights) : ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>
    {
        public IQueryable<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>> SeatRights
            => KeptToTenant(() => store._rights.Where(Readable), right => right.TenantId).AsQueryable();

        /// <summary>Whether the current caller reads the right: every one, or with the rights kept, a seat its own alone.</summary>
        private bool Readable(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId> right)
            => !mayKeepRights
               || !store.KeepsOtherSeatsRights
               || TenancyCallers.Ambient is not HostCaller { Kind: TenancyCallerKind.Seat } seat
               || right.SeatId == seat.Seat;

        public IQueryable<OrganizationUnitPath<TenantId, OrganizationUnitId>> UnitPaths
            => KeptToTenant(() => store._paths, path => path.TenantId).AsQueryable();

        public IQueryable<OrganizationUnitRow<TenantId, OrganizationUnitId>> Units
            => KeptToTenant(
                () => store._organizations.Values.SelectMany(organization => organization.Units.Select(unit => new OrganizationUnitRow<TenantId, OrganizationUnitId>
                {
                    Id = unit.Id,
                    TenantId = organization.Id,
                    ParentId = unit.ParentId,
                    Status = unit.Status,
                })),
                row => row.TenantId).AsQueryable();

        public IQueryable<RoleRow<TenantId, RoleId>> Roles
            => KeptToTenant(
                () => store._roles.Values.Select(role => new RoleRow<TenantId, RoleId>
                {
                    Id = role.Id,
                    TenantId = role.TenantId,
                    FromPack = role.FromPack,
                    Status = role.Status,
                    Keys = role.Keys,
                }),
                row => row.TenantId).AsQueryable();

        public IQueryable<PlacementRow<SeatId, OrganizationUnitId>> Placements
            => KeptToTenant(
                    () => store._seats.Values.SelectMany(seat => seat.Placements.Select(placement => (seat.TenantId, Row: new PlacementRow<SeatId, OrganizationUnitId>
                    {
                        SeatId = seat.Id,
                        UnitId = placement.UnitId,
                        IsPrimary = placement.IsPrimary,
                    }))),
                    row => row.TenantId)
                .Select(row => row.Row)
                .AsQueryable();

        public IQueryable<SeatRow<TenantId, SeatId>> Seats
            => KeptToTenant(
                () => store._seats.Values.Select(seat => new SeatRow<TenantId, SeatId>
                {
                    Id = seat.Id,
                    TenantId = seat.TenantId,
                    Status = seat.Status,
                }),
                row => row.TenantId).AsQueryable();

        /// <summary>The rows, read when enumerated, kept to the current caller's tenant when filtered.</summary>
        private IEnumerable<T> KeptToTenant<T>(Func<IEnumerable<T>> rows, Func<T, TenantId> tenantOf)
        {
            foreach (var row in rows())
            {
                if (!filtered || Visible(tenantOf(row)))
                {
                    yield return row;
                }
            }
        }
    }
}

/// <summary>One call made to the store, and who was calling when it was made.</summary>
/// <param name="Name">The call.</param>
/// <param name="Tenancy">The Tenancy caller then.</param>
/// <param name="Core">The toolkit's caller then.</param>
public sealed record StoreCall(string Name, ITenancyCaller? Tenancy, Caller? Core);
