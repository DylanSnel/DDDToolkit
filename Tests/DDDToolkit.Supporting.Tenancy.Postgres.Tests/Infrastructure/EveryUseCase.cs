using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// Every command of the use cases, run once on the seeded data, each as a seat the use case lets do it, or as system
/// work where only that may: what proves that no policy is stricter than the use cases, and what the database is
/// left with to compare against what the use cases say it should hold.
/// </summary>
public static class EveryUseCase
{
    /// <summary>The identity of Pat, a seats manager the commands add.</summary>
    public static readonly Guid Pat = Guid.Parse("d0000000-0000-4000-8000-000000000098");

    /// <summary>The identity of Wren, who has no seat anywhere until they accept an invitation.</summary>
    public static readonly Guid Wren = Guid.Parse("d0000000-0000-4000-8000-000000000097");

    /// <summary>The tenant the commands provision, change the shape of and close.</summary>
    public static readonly TenantId Estuary = new(40);

    /// <summary>Runs the commands over <paramref name="services"/>, under whatever stands between them and the database.</summary>
    /// <returns>Pat's seat.</returns>
    public static async Task<SeatId> RunAsync(TenancyServices services, CancellationToken cancellation)
    {
        Task As(Person person, Func<IServiceProvider, Task> act) => services.BySeat(person.Identity, Harbor, person.Seat, act);

        // Roles given and taken away: by a grants manager, a role whose keys he does not hold, to someone else and to
        // himself; by a holder of a role's keys that manage access, that role.
        await As(Hiro, scoped => scoped.Seats().GrantAsync(Oli.Seat, NorthPier, HarborRoles.Watcher, until: null, reason: "Standing in", cancellation));
        await As(Hiro, scoped => scoped.Seats().RevokeAsync(Oli.Seat, NorthPier, HarborRoles.Watcher, cancellation));
        await As(Hiro, scoped => scoped.Seats().GrantAsync(Hiro.Seat, North, HarborRoles.Operator, until: null, reason: null, cancellation));
        await As(Seth, scoped => scoped.Seats().GrantAsync(Oli.Seat, NorthPier, HarborRoles.Supervisor, until: null, reason: null, cancellation));
        await As(Seth, scoped => scoped.Seats().RevokeAsync(Oli.Seat, NorthPier, HarborRoles.Supervisor, cancellation));

        // Invitations, by the administrator, who alone manages seats for the whole tenant: one cancelled by whoever
        // manages seats at its unit, who lists it first, and one accepted by a person who has no seat, which makes it.
        var withdrawn = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped =>
            scoped.Invitations().IssueAsync("lark@example.test", NorthPier, HarborRoles.Watcher, grantUntil: null, "Lark", lifetime: null, cancellation));
        await As(Seth, async scoped =>
        {
            (await scoped.Invitations().ListOpenAsync(cancellation)).Should().ContainSingle(invitation => invitation.Id == withdrawn.Id);
            await scoped.Invitations().CancelAsync(withdrawn.Id, cancellation);
        });
        var invited = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped =>
            scoped.Invitations().IssueAsync("wren@example.test", North, HarborRoles.Operator, grantUntil: null, "Wren", lifetime: null, cancellation));
        await services.BySignedInUser(Wren, scoped => scoped.Invitations().AcceptAsync(invited.Token, displayName: null, verifiedAddress: "wren@example.test", cancellation));

        // Placements, at a unit where the seats key is held; one withdrawn with its grants, a role that manages no access
        // and one that does, by a holder of its keys.
        await As(Seth, scoped => scoped.Seats().PlaceAsync(Eve.Seat, North, primary: false, cancellation));
        await As(Seth, scoped => scoped.Seats().MakePrimaryAsync(Eve.Seat, North, cancellation));
        await As(Seth, scoped => scoped.Seats().PlaceAsync(Oli.Seat, North, primary: false, cancellation));
        await As(Hiro, scoped => scoped.Seats().GrantAsync(Oli.Seat, North, HarborRoles.Watcher, until: null, reason: null, cancellation));
        await As(Seth, scoped => scoped.Seats().GrantAsync(Oli.Seat, North, HarborRoles.Supervisor, until: null, reason: null, cancellation));
        await As(Seth, scoped => scoped.Seats().WithdrawAsync(Oli.Seat, North, cancellation));

        // A seat takes away its own role that manages access, which leaves it without the key the next row's policy
        // would ask.
        await As(Hiro, scoped => scoped.Seats().RevokeAsync(Hiro.Seat, North, GrantsDesk, cancellation));

        // Seats: added, renamed, by a manager and by the seat itself, and stopped and started again, by a manager and
        // by a seats manager for the whole tenant itself, which suspends and deactivates itself.
        var pat = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Seats().AddSeatAsync(Pat, "Pat", cancellation));
        var seatsDesk = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Roles().CreateAsync("Seats desk", "Keeps the seats", [TenancyKeys.SeatsManage], cancellation));
        await As(Ada, scoped => scoped.Seats().PlaceAsync(pat, HarborRoot, primary: true, cancellation));
        await As(Ada, scoped => scoped.Seats().GrantAsync(pat, HarborRoot, seatsDesk, until: null, reason: null, cancellation));
        await As(Ada, scoped => scoped.Seats().RenameAsync(Hiro.Seat, "Hiro B.", cancellation));
        await As(Oli, scoped => scoped.Seats().RenameAsync(Oli.Seat, "Oliver", cancellation));
        await As(Ada, scoped => scoped.Seats().SuspendAsync(Oli.Seat, cancellation));
        await As(Ada, scoped => scoped.Seats().ReactivateAsync(Oli.Seat, cancellation));
        await services.BySeat(Pat, Harbor, pat, scoped => scoped.Seats().SuspendAsync(pat, cancellation));
        await As(Ada, scoped => scoped.Seats().ReactivateAsync(pat, cancellation));
        await services.BySeat(Pat, Harbor, pat, scoped => scoped.Seats().DeactivateAsync(pat, cancellation));

        // The tree, by a units manager at North, who renames North itself without the key at its parent.
        var dock = OrganizationUnitId.CreateSequential();
        await As(Seth, scoped => scoped.Organization().AddUnitAsync(North, "North Dock", cancellation, dock, unit => unit.SetCostCentre("ND-001")));
        await As(Seth, scoped => scoped.Organization().RenameUnitAsync(dock, "North Docks", cancellation));
        await As(Seth, scoped => scoped.Organization().MoveUnitAsync(dock, NorthPier, cancellation));
        await As(Seth, scoped => scoped.Organization().ArchiveUnitAsync(dock, cancellation));
        await As(Seth, scoped => scoped.Organization().RenameUnitAsync(North, "North Coast", cancellation));
        await As(Ada, scoped => scoped.Organization().MoveUnitAsync(NorthPier, South, cancellation));

        // A seat withdraws itself from the unit of its own role that manages access.
        await As(Seth, scoped => scoped.Seats().WithdrawAsync(Seth.Seat, North, cancellation));

        // Roles, by the administrator: made, renamed, their keys changed, those of a role that manages access too, and archived.
        var clerk = await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Roles().CreateAsync("Clerk", "Keeps records", [HostCatalogue.WidgetRead], cancellation));
        await As(Ada, scoped => scoped.Roles().RenameAsync(clerk, "Clerks", "Keeps the records", cancellation));
        await As(Ada, scoped => scoped.Roles().SetKeysAsync(HarborRoles.Operator, [HostCatalogue.WidgetRead], cancellation));
        await As(Ada, scoped => scoped.Roles().SetKeysAsync(HarborRoles.Supervisor, [TenancyKeys.UnitsManage, TenancyKeys.SeatsManage, HostCatalogue.WidgetChange], cancellation));
        await As(Ada, scoped => scoped.Roles().ArchiveAsync(HarborRoles.Watcher, cancellation));

        // The roles follow their packs, by system work in the tenant, once the application ships a catalogue whose
        // supervisor's pack also reads the history: Harbor's Supervisor gains that key, and keeps out what Ada took out.
        var later = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack.Key == HostCatalogue.SupervisorPack ? pack with { Keys = [.. pack.Keys, TenancyKeys.HistoryView] } : pack)],
            },
            []);
        await services.BySystemIn(Harbor, scoped => new HostTenancy.RoleCommands(
                scoped.GetRequiredService<HostTenancy.IStore>(),
                later,
                scoped.GetRequiredService<TimeProvider>())
            .FollowPacksAsync(cancellation));

        // The tenant: renamed by the administrator; suspended and started again by system work, which alone may.
        await As(Ada, scoped => scoped.Tenants().RenameOrganizationAsync("Harbor Group", cancellation));
        await services.BySystemIn(Harbor, scoped => scoped.Tenants().SuspendAsync("A pause", cancellation));
        await services.BySystemIn(Harbor, scoped => scoped.Tenants().ReactivateAsync(cancellation));

        // A flat tenant, provisioned, made hierarchical by its administrator, and closed.
        var dan = Guid.NewGuid();
        HostTenancy.ProvisionedTenant provisioned;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            provisioned = await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("estuary", "Estuary Works", TenantShape.Flat, "Estuary", dan, "Dan", TenantId: Estuary),
                cancellation));
        }

        await services.BySeat(dan, Estuary, provisioned.AdminSeat, scoped => scoped.Tenants().ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, cancellation));
        await services.BySystemIn(Estuary, scoped => scoped.Tenants().CloseAsync("Wound up", cancellation));

        return pat;
    }
}
