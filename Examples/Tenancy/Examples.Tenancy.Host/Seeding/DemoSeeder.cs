using Examples.Tenancy.Projects.Application.Access;
using Examples.Tenancy.Projects.Application.Crew.Commands;
using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using Examples.Tenancy.Projects.Application.Overview.Queries;
using Examples.Tenancy.Projects.Application.ProjectRoles.Commands;
using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Application.Roles.Queries;
using Examples.Tenancy.Tenants.Domain.Aggregates.Seats;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using Examples.Tenancy.Tenants.Application.Tenant.Commands;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Host.Seeding;

/// <summary>
/// Seeds the demonstration (<see cref="DemoData"/>) when <c>Sample:SeedDemoData</c> is on: both tenants, their
/// units, every seat, placement and grant, the state changes, the starter project roles, and the projects with
/// their crews, through the same use cases a request goes through.
/// </summary>
/// <remarks>
/// It is a plain hosted service that awaits all of the seeding inside <see cref="StartAsync"/>, so the host
/// takes no request, and a test host answers no call, before the data is there. By then the start-up checks have
/// run, in their <c>StartingAsync</c>, and found every migration applied: the host applies none, whoever owns the
/// database applies the files under <c>Examples/Tenancy/supabase/migrations</c>.
/// <para>
/// Nothing here writes a row by hand, and nothing here names a context: the host knows no module's storage.
/// Provisioning is system work outside any tenant, the only thing such work may do; everything after it is
/// system work inside the tenant, acting for its first administrator, which holds every permission there and nothing
/// anywhere else. Each command is a unit of work of its own, in a scope of its own, as it would be in a request.
/// Within a tenant the first step is setting it up, which a tenant made at any time goes through as well: the
/// starter project roles its crews begin with. Then units, seats, placements, grants, state changes, and then
/// projects and crews: a suspended seat was placed and granted like any other before it was suspended, and a
/// project's owner and crew are seats with their places in the organization when the project is opened.
/// </para>
/// <para>
/// What a request could ask for goes the way a request does: as a command or a query, sent through the mediator.
/// Tenancy's own data is seeded with the package's use cases instead, because a demonstration needs what no
/// command offers a client: ids fixed in advance, and a grant that started in the past.
/// </para>
/// <para>
/// Seeding twice seeds nothing: a first tenant whose slug is taken means the data is there, and it stops. It
/// then checks that every seat, role, project role and project of the demonstration is there too, and warns,
/// naming what is not: a seeding that stopped part-way, or a file an earlier version of the sample seeded, would
/// otherwise be taken for a complete one by every later start.
/// </para>
/// </remarks>
/// <param name="scopes">Makes a scope per command.</param>
/// <param name="configuration">Says whether to seed.</param>
/// <param name="clock">What "now" is, which the grants' periods are measured from.</param>
/// <param name="logger">Says what was seeded.</param>
public sealed class DemoSeeder(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<DemoSeeder> logger) : IHostedService
{
    /// <summary>The setting that turns seeding on.</summary>
    public const string Setting = "Sample:SeedDemoData";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>(Setting))
        {
            return;
        }

        // Who acts, and the work that is not a caller's own: the access manager, closed over the Tenants module's ids
        // like its use cases, so the host names neither of them. It keeps nothing, so one serves the whole seeding.
        await using var seeding = scopes.CreateAsyncScope();
        var access = seeding.ServiceProvider.GetRequiredService<TenancyAccessManager>();

        using (access.BeginSystem())
        {
            foreach (var tenant in DemoData.Tenants)
            {
                if (!await ProvisionAsync(tenant, cancellationToken))
                {
                    logger.LogInformation("The demonstration is seeded already: the slug {Slug} is taken.", tenant.Slug);
                    await WarnWhenIncompleteAsync(access, cancellationToken);
                    return;
                }
            }
        }

        foreach (var tenant in DemoData.Tenants)
        {
            // The organization is Tenancy's, so it is seeded as Tenancy's system work in the tenant; the projects are
            // Projects', so they are seeded as that module's, in its own scope, the one the projects' rules let
            // through. Tenancy's scope holds nothing on a project.
            using (access.BeginSystemIn(tenant.Id, tenant.Administrator.Id))
            {
                await SeedOrganizationAsync(tenant, cancellationToken);
            }

            using (access.BeginSystemIn(tenant.Id, tenant.Administrator.Id, ProjectMembership.Scope))
            {
                await SeedProjectsAsync(tenant, cancellationToken);
            }
        }

        logger.LogInformation("Seeded the demonstration: {Tenants}.", string.Join(" and ", DemoData.Tenants.Select(tenant => tenant.Slug)));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Provisions <paramref name="tenant"/> with its fixed ids: the tenant, its root, a role per starter role seeded for its
    /// shape, and its administrator's seat, placed at the root and granted there the role of the administrators'
    /// starter role among them. The root's kind and the administrator's name are the application's own fields, so the
    /// package hands the root and the first seat to callbacks that set them, in the save that provisions.
    /// <see langword="false"/> when its slug is taken.
    /// </summary>
    private async Task<bool> ProvisionAsync(DemoTenant tenant, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync<TenantManager>(tenants => tenants.ProvisionAsync(
                new TenantToProvision(
                    tenant.Slug,
                    tenant.Name,
                    tenant.Shape,
                    tenant.Name,
                    tenant.Administrator.Person.Id,
                    TenantId: tenant.Id,
                    RootId: tenant.Root,
                    AdminSeatId: tenant.Administrator.Id,
                    RoleIds: tenant.Roles,
                    ConfigureRoot: root => root.SetKind(DemoTenant.RootKind),
                    ConfigureFirstSeat: administrator => administrator.Rename(tenant.Administrator.Person.Name)),
                cancellationToken));
            return true;
        }
        catch (RefusalException refusal) when (refusal.Code == TenancyRefusals.SlugTaken)
        {
            return false;
        }
    }

    /// <summary>
    /// Warns, naming what is missing, when a seat, a role, a project role or a project of the demonstration is not
    /// there: the seeding that provisioned the tenants stopped before the end, or an earlier version of the sample
    /// seeded the file, and the demonstration's people and refusals would not all be the ones it promises.
    /// </summary>
    private async Task WarnWhenIncompleteAsync(TenancyAccessManager access, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var tenant in DemoData.Tenants)
        {
            // System work in a tenant reads all of it: Tenancy's every seat and role, Projects' every project role
            // and every project, each module's in its own scope.
            IReadOnlyList<string> lacking;
            IReadOnlyList<Seat> seats;
            IReadOnlyList<RoleListing> roles;
            using (access.BeginSystemIn(tenant.Id, tenant.Administrator.Id))
            {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                seats = await sender.Send(new TenantSeats(), cancellationToken);
                roles = await sender.Send(new TenantRoles(), cancellationToken);
            }

            using (access.BeginSystemIn(tenant.Id, tenant.Administrator.Id, ProjectMembership.Scope))
            {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var projectRoles = await sender.Send(new TenantProjectRoles(), cancellationToken);
                // The demonstration has a handful of projects in each tenant: its largest page holds them all.
                var projects = await sender.Send(new VisibleProjects(new PagingArguments(first: VisibleProjects.LargestPage)), cancellationToken);
                lacking = tenant.MissingFrom(
                    seats.Select(seat => seat.Id).ToHashSet(),
                    roles.Select(listed => listed.Role.Id).ToHashSet(),
                    projectRoles.Select(listed => listed.Id).ToHashSet(),
                    projects.Items.Select(project => project.Id).ToHashSet());
            }

            if (lacking.Count > 0)
            {
                missing.Add(tenant.Slug + ": " + string.Join(", ", lacking));
            }
        }

        if (missing.Count > 0)
        {
            logger.LogWarning(
                "The demonstration data is incomplete, so an earlier seeding stopped part-way or an earlier version of the sample seeded it. Missing: {Missing}. To seed from scratch, restart the AppHost, which starts on an empty database, or run `supabase db reset` on the Supabase CLI's stack.",
                string.Join("; ", missing));
        }
    }

    /// <summary>The organization of one tenant, its units, seats and grants, as Tenancy's system work there.</summary>
    private async Task SeedOrganizationAsync(DemoTenant tenant, CancellationToken cancellationToken)
    {
        // An application's own state on a package's aggregate: the package stores it and knows nothing of it, so
        // the module has a command of its own for it, which only system work in the tenant may send.
        await SendAsync(new MarkTenantAsDemo(), cancellationToken);

        // Each with its kind, set on the new unit in the save that adds it, as the module's command sets it.
        foreach (var unit in tenant.Units)
        {
            await RunAsync<OrganizationManager>(organization
                => organization.AddUnitAsync(unit.Parent, unit.Name, cancellationToken, unit.Id, added => added.SetKind(unit.Kind)));
        }

        // Each with the name the person is shown by in this tenant, the seat class's own field, set in the save that adds it.
        foreach (var seat in tenant.Seats)
        {
            await RunAsync<SeatManager>(seats
                => seats.AddAsync(seat.Person.Id, cancellationToken, seat.Id, added => added.Rename(seat.Person.Name)));
        }

        foreach (var seat in tenant.Seats)
        {
            await RunAsync<SeatManager>(seats => seats.PlaceAsync(seat.Id, seat.PlacedIn, primary: true, cancellationToken));
        }

        // A start in the past is system work's to choose, which this is: a grant that has expired had to begin.
        var now = clock.GetUtcNow();
        foreach (var grant in tenant.Grants)
        {
            await RunAsync<SeatManager>(seats => seats.GrantAsync(
                tenant.SeatOf(grant.Person),
                tenant.Roles[grant.StarterRole],
                grant.Unit,
                until: grant.Until is { } after ? now + after : null,
                reason: null,
                cancellationToken,
                from: grant.From is { } before ? now - before : null));
        }

        // A suspended seat keeps its placements and grants, which simply count for nothing while it is.
        foreach (var person in tenant.Suspended)
        {
            await RunAsync<SeatManager>(seats => seats.SuspendAsync(tenant.SeatOf(person), cancellationToken));
        }

    }

    /// <summary>The project roles and the projects of one tenant, as Projects' system work there.</summary>
    private async Task SeedProjectsAsync(DemoTenant tenant, CancellationToken cancellationToken)
    {
        // Setting the tenant up for projects: the starter project roles every tenant's crews begin with. System work
        // in the tenant, as for any tenant made, with the demonstration's fixed ids.
        await SendAsync(new SetUpProjectRoles(tenant.ProjectRoles), cancellationToken);

        // Then the projects. Each owner goes on the crew holding no project role, since it holds what an owner does
        // by owning the project; the rest of the crew each hold the project role made from their starter role, all
        // for good. System work names the owner, and is recorded as the administrator it acts for. The commands are
        // the ones a request sends; the project's id is the one thing only system work may say.
        var now = clock.GetUtcNow();
        foreach (var project in tenant.Projects)
        {
            await SendAsync(
                new OpenProject(
                    project.Number,
                    project.Name,
                    project.Unit,
                    tenant.SeatOf(project.Owner),
                    project.Id,
                    project.PlannedOn(DateOnly.FromDateTime(now.UtcDateTime))),
                cancellationToken);

            foreach (var member in project.Crew)
            {
                await SendAsync(
                    new AddCrewMember(project.Id, tenant.SeatOf(member.Person), tenant.ProjectRoles[member.Role], Until: null),
                    cancellationToken);
            }
        }
    }

    /// <summary>Sends one command in a scope of its own: one unit of work, as a request would be.</summary>
    private async Task SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }

    /// <summary>Runs one use case in a scope of its own: one unit of work, as a request would be.</summary>
    private async Task RunAsync<TService>(Func<TService, Task> command)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        await command(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
