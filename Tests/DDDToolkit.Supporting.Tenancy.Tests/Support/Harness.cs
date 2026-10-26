namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>
/// The use cases over an in-memory store, with a clock a test controls, run the way an application runs
/// them: each call as one caller, in a unit of work of its own.
/// <para>
/// <see cref="OfHarbor"/> starts from the Harbor tenant of <see cref="HarborBuilder"/>, saved as if it had
/// been provisioned. The store works on copies, so the <see cref="Harbor"/> record keeps the ids to use and
/// the store has the state to assert on.
/// </para>
/// </summary>
public sealed class Harness
{
    /// <summary>An empty store, with the host's catalogue and any contributions.</summary>
    public Harness(TenancyCatalogue catalogue)
    {
        Catalogue = catalogue;
        Store = new InMemoryTenancyStore(catalogue);
        Options = new TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId> { Catalogue = HostCatalogue.Application };
    }

    /// <summary>The clock the use cases read.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The catalogue the use cases and the store work with.</summary>
    public TenancyCatalogue Catalogue { get; }

    /// <summary>The options, with the host's catalogue: new ids are the ids' own, <c>TenantId.Create()</c> and the rest.</summary>
    public TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId> Options { get; }

    /// <summary>How long an invitation stays open: the package's own lifetimes.</summary>
    public TenancyInvitationOptions<InvitationId> InvitationOptions { get; } = new();

    /// <summary>The store.</summary>
    public InMemoryTenancyStore Store { get; }

    /// <summary>The tenant the harness started from, when it started from one.</summary>
    public Harbor Harbor { get; private set; } = null!;

    /// <summary>The Harbor tenant's id.</summary>
    public TenantId Tenant => Harbor.Tenant.Id;

    /// <summary>The first administrator's seat.</summary>
    public SeatId Administrator => Harbor.Administrator.Id;

    /// <summary>The packs' texts in a tenant's language, as an application registers them; none unless a test sets them.</summary>
    public IRolePackTexts? PackTexts { get; set; }

    public HostTenancy.TenantCommands Tenants => new(Store, Catalogue, Clock, PackTexts);

    public HostTenancy.OrganizationCommands Organization => new(Store, Catalogue, Clock);

    public HostTenancy.SeatCommands Seats => new(Store, Catalogue, Clock);

    public HostTenancy.RoleCommands Roles => new(Store, Catalogue, Clock);

    public HostTenancy.TenancyDirectory Directory => new(Store, Catalogue, Clock);

    /// <summary>The invitations, which ask the toolkit's own caller who accepts: the one <c>Callers.Begin</c> made current.</summary>
    public HostTenancy.InvitationCommands<HostInvitation, InvitationId> Invitations
        => new(Store, Store, Catalogue, Options, InvitationOptions, new DDDToolkit.Access.AmbientCallerAccessor(), Clock);

    /// <summary>The tenants' directory, which asks the toolkit's own caller: the one <c>Callers.Begin</c> made current.</summary>
    public HostTenancy.TenantDirectory TenantDirectory => new(Store, Options, new DDDToolkit.Access.AmbientCallerAccessor());

    /// <summary>Harbor, saved, with the catalogue given or the host's.</summary>
    public static Harness OfHarbor(TenancyCatalogue? catalogue = null)
    {
        var harbor = new HarborBuilder().With(catalogue ?? New.Catalogue()).Build();
        var harness = new Harness(harbor.Catalogue) { Harbor = harbor };
        harness.Store.Seed(harbor.Aggregates);
        return harness;
    }

    /// <summary>Another tenant saved next to Harbor, built the same way.</summary>
    public Harbor Seed(long tenant, string slug)
    {
        var other = new HarborBuilder().With(Catalogue).Tenant(tenant, slug).Build();
        Store.Seed(other.Aggregates);
        return other;
    }

    /// <summary>The role Harbor copied from a pack.</summary>
    public RoleId RoleFromPack(string pack) => Harbor.RolesByPack[pack].Id;

    /// <summary>A seat of Harbor, as a caller.</summary>
    public HostCaller SeatCaller(SeatId seat) => HostCaller.InSeat(Tenant, seat);

    /// <summary>System work in Harbor, as a caller.</summary>
    public HostCaller SystemCaller(SeatId? actingSeat = null) => HostCaller.SystemIn(Tenant, actingSeat);

    /// <summary>Runs <paramref name="act"/> as a seat of Harbor, in a new unit of work.</summary>
    public Task<T> As<T>(SeatId seat, Func<Harness, Task<T>> act) => Run(SeatCaller(seat), act);

    /// <summary>Runs <paramref name="act"/> as a seat of Harbor, in a new unit of work.</summary>
    public Task As(SeatId seat, Func<Harness, Task> act) => Run(SeatCaller(seat), act);

    /// <summary>Runs <paramref name="act"/> as system work in Harbor, in a new unit of work.</summary>
    public Task<T> BySystemWork<T>(Func<Harness, Task<T>> act) => Run(SystemCaller(), act);

    /// <summary>Runs <paramref name="act"/> as system work in Harbor, in a new unit of work.</summary>
    public Task BySystemWork(Func<Harness, Task> act) => Run(SystemCaller(), act);

    /// <summary>Runs <paramref name="act"/> as <paramref name="caller"/>, in a new unit of work.</summary>
    public async Task<T> Run<T>(ITenancyCaller caller, Func<Harness, Task<T>> act)
    {
        using (TenancyCallers.Begin(caller))
        {
            Store.BeginUnitOfWork();
            return await act(this);
        }
    }

    /// <summary>Runs <paramref name="act"/> as <paramref name="caller"/>, in a new unit of work.</summary>
    public Task Run(ITenancyCaller caller, Func<Harness, Task> act)
        => Run(caller, async harness =>
        {
            await act(harness);
            return true;
        });

    /// <summary>
    /// Accepts an invitation as a person signed in with <paramref name="identity"/>, who names no tenant and has no
    /// Tenancy caller: the toolkit's caller is all there is, as in a request, in a new unit of work. The new seat is
    /// given <paramref name="displayName"/> in the use case's callback, as the host application names its seats.
    /// </summary>
    public async Task<HostTenancy.AcceptedInvitation> Accept(Guid identity, string token, string? displayName = "Wren", string? verifiedAddress = null)
    {
        using (DDDToolkit.Access.Callers.Begin(DDDToolkit.Abstractions.Access.Caller.User(identity)))
        {
            Store.BeginUnitOfWork();
            return await Invitations.AcceptAsync(token, verifiedAddress, CancellationToken.None, configure: seat => seat.Rename(displayName));
        }
    }

    /// <summary>
    /// A new seat of Harbor, added with <paramref name="displayName"/>, the host's own field, placed at
    /// <paramref name="unit"/> as its primary placement and granted there the role of each pack, by system work, one
    /// command at a time.
    /// </summary>
    public async Task<SeatId> SeatAt(string displayName, OrganizationUnitId unit, params string[] packs)
    {
        var seat = await BySystemWork(harness => harness.Seats.AddSeatAsync(Guid.NewGuid(), default, configure: added => added.Rename(displayName)));
        await BySystemWork(harness => harness.Seats.PlaceAsync(seat, unit, primary: true, default));
        foreach (var pack in packs)
        {
            await Grant(seat, unit, pack);
        }

        return seat;
    }

    /// <summary>Places a seat of Harbor at another unit, by system work.</summary>
    public Task Place(SeatId seat, OrganizationUnitId unit)
        => BySystemWork(harness => harness.Seats.PlaceAsync(seat, unit, primary: false, default));

    /// <summary>Grants a seat of Harbor the role of a pack at a unit where it is placed, by system work.</summary>
    public Task Grant(SeatId seat, OrganizationUnitId unit, string pack, DateTimeOffset? until = null, DateTimeOffset? from = null)
        => BySystemWork(harness => harness.Seats.GrantAsync(seat, unit, RoleFromPack(pack), until, reason: null, default, from));
}
