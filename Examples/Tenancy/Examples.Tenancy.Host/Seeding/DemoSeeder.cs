using Examples.Tenancy.Projects.Application.Crew.Commands;
using Examples.Tenancy.Projects.Application.Lifecycle.Commands;
using Examples.Tenancy.Projects.Application.Overview.Queries;
using Examples.Tenancy.Projects.Application.ProjectRoles.Commands;
using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
using Examples.Tenancy.Tenants.Application.Roles.Queries;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using Examples.Tenancy.Tenants.Application.Tenant.Commands;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
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
/// takes no request, and a test host answers no call, before the data is there. The databases have been
/// migrated by then, in their <c>StartingAsync</c>.
/// <para>
/// Nothing here writes a row by hand, and nothing here names a context: the host knows no module's storage.
/// Provisioning is system work outside any tenant, the only thing such work may do; everything after it is
/// system work inside the tenant, acting for its first administrator, which holds every key there and nothing
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

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            foreach (var tenant in DemoData.Tenants)
            {
                if (!await ProvisionAsync(tenant, cancellationToken))
                {
                    logger.LogInformation("The demonstration is seeded already: the slug {Slug} is taken.", tenant.Slug);
                    await WarnWhenIncompleteAsync(cancellationToken);
                    return;
                }
            }
        }

        foreach (var tenant in DemoData.Tenants)
        {
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant.Id, tenant.Administrator.Id))
            {
                await SeedAsync(tenant, cancellationToken);
            }
        }

        logger.LogInformation("Seeded the demonstration: {Tenants}.", string.Join(" and ", DemoData.Tenants.Select(tenant => tenant.Slug)));
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Provisions <paramref name="tenant"/> with its fixed ids: the tenant, its root, a role per pack seeded for its
    /// shape, and its administrator's seat, placed at the root and granted there the role of the administrators'
    /// pack among them. The root's kind is the application's own field, so the package hands the root to a callback
    /// that sets it, in the save that provisions. <see langword="false"/> when its slug is taken.
    /// </summary>
    private async Task<bool> ProvisionAsync(DemoTenant tenant, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync<TenantsTenancy.TenantCommands>(tenants => tenants.ProvisionAsync(
                new TenantsTenancy.TenantToProvision(
                    tenant.Slug,
                    tenant.Name,
                    tenant.Shape,
                    tenant.Name,
                    tenant.Administrator.Person.Id,
                    tenant.Administrator.Person.Name,
                    TenantId: tenant.Id,
                    RootId: tenant.Root,
                    AdminSeatId: tenant.Administrator.Id,
                    RoleIds: tenant.Roles,
                    ConfigureRoot: root => root.SetKind(DemoTenant.RootKind)),
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
    private async Task WarnWhenIncompleteAsync(CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var tenant in DemoData.Tenants)
        {
            // System work in a tenant reads all of it: every seat, role and project role, and every project.
            IReadOnlyList<string> lacking;
            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant.Id, tenant.Administrator.Id))
            {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var seats = await sender.Send(new TenantSeats(), cancellationToken);
                var roles = await sender.Send(new TenantRoles(), cancellationToken);
                var projectRoles = await sender.Send(new TenantProjectRoles(), cancellationToken);
                // The demonstration has a handful of projects in each tenant: its largest page holds them all.
                var projects = await sender.Send(new VisibleProjects(new PagingArguments(first: VisibleProjects.LargestPage)), cancellationToken);
                lacking = tenant.MissingFrom(
                    seats.Select(seat => seat.Id).ToHashSet(),
                    roles.Select(listed => listed.Id).ToHashSet(),
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

    /// <summary>Everything inside one tenant, as system work there.</summary>
    private async Task SeedAsync(DemoTenant tenant, CancellationToken cancellationToken)
    {
        // Setting the tenant up for projects: the starter project roles every tenant's crews begin with, the crew
        // lead's among them, which every project's owner holds. System work in the tenant, as for any tenant made,
        // with the demonstration's fixed ids.
        await SendAsync(new SetUpProjectRoles(tenant.ProjectRoles), cancellationToken);

        // An application's own state on a package's aggregate: the package stores it and knows nothing of it, so
        // the module has a command of its own for it, which only system work in the tenant may send.
        await SendAsync(new MarkTenantAsDemo(), cancellationToken);

        // Each with its kind, set on the new unit in the save that adds it, as the module's command sets it.
        foreach (var unit in tenant.Units)
        {
            await RunAsync<TenantsTenancy.OrganizationCommands>(organization
                => organization.AddUnitAsync(unit.Parent, unit.Name, cancellationToken, unit.Id, added => added.SetKind(unit.Kind)));
        }

        foreach (var seat in tenant.Seats)
        {
            await RunAsync<TenantsTenancy.SeatCommands>(seats
                => seats.AddSeatAsync(seat.Person.Id, seat.Person.Name, cancellationToken, seat.Id));
        }

        foreach (var seat in tenant.Seats)
        {
            await RunAsync<TenantsTenancy.SeatCommands>(seats => seats.PlaceAsync(seat.Id, seat.PlacedIn, primary: true, cancellationToken));
        }

        // A start in the past is system work's to choose, which this is: a grant that has expired had to begin.
        var now = clock.GetUtcNow();
        foreach (var grant in tenant.Grants)
        {
            await RunAsync<TenantsTenancy.SeatCommands>(seats => seats.GrantAsync(
                tenant.SeatOf(grant.Person),
                grant.Unit,
                tenant.Roles[grant.Pack],
                until: grant.Until is { } after ? now + after : null,
                reason: null,
                cancellationToken,
                from: grant.From is { } before ? now - before : null));
        }

        // A suspended seat keeps its placements and grants, which simply count for nothing while it is.
        foreach (var person in tenant.Suspended)
        {
            await RunAsync<TenantsTenancy.SeatCommands>(seats => seats.SuspendAsync(tenant.SeatOf(person), cancellationToken));
        }

        // Last, the projects. Each owner goes on the crew holding the tenant's crew lead's project role, the rest of
        // the crew each holding the project role made from their starter role, all for good. System work names the
        // owner, and is recorded as the administrator it acts for. The commands are the ones a request sends; the
        // project's id is the one thing only system work may say.
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
