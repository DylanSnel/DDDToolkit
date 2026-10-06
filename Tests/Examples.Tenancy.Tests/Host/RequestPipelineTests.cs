using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.Exceptions;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.UseCases;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// How a command or a query travels from whoever sends it to its handler: through tracing, then its module's
/// access check, then the handler; and what holds when a request's queries are sent side by side, as the
/// resolvers of one GraphQL request send them.
/// </summary>
/// <remarks>
/// The requests are sent the way anything but a route sends them: from a scope of the host's services, as the
/// callers the test begins, which are the two a request has (<see cref="SampleCallers"/>). What the routes answer
/// is the scenario tests' to prove.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class RequestPipelineTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task The_pipeline_runs_tracing_then_access_then_the_handler()
    {
        var host = await sample.SharedAsync();

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == host.Services.GetRequiredService<IHostEnvironment>().ApplicationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        // The other tests' hosts trace on the same source, so the test's own requests are told apart by their trace.
        using var mine = new Activity("the test's own requests").Start();
        var notAKey = new UnitsWhereIHold("projects.nothing");

        // Nobody: the access check refuses before the handler sees the key, which it would have refused.
        var asNobody = await RefusalOfAsync(host, notAKey);
        asNobody.Code.Should().Be(TenancyRefusals.NotSeated, "the access check comes before the handler");

        // A seat: the access check lets it through, and the handler refuses the key.
        RefusalException asRhea;
        using (AsSeatOf(DemoPeople.Rhea))
        {
            asRhea = await RefusalOfAsync(host, notAKey);
        }

        asRhea.Code.Should().Be(TenancyRefusals.UnknownPermission, "the handler runs once the access check has passed");

        // Tracing is outside both: each refused request has its activity, tagged with the code it was refused with.
        mine.Stop();
        List<Activity> traced;
        lock (stopped)
        {
            traced = [.. stopped.Where(activity => activity.TraceId == mine.TraceId && activity.OperationName == nameof(UnitsWhereIHold))];
        }

        traced.Select(activity => activity.GetTagItem(RequestTracing.RefusalTag)).Should().Equal(TenancyRefusals.NotSeated, TenancyRefusals.UnknownPermission);
        traced.Should().OnlyContain(activity => activity.Status != ActivityStatusCode.Error, "a refusal is an answer, not a fault");
    }

    [Fact]
    public async Task A_request_that_declares_nothing_is_refused_before_its_handler()
    {
        // The behavior the toolkit writes for a module is closed to everything its module's checks do not let
        // through. A request that declares nothing at all is stopped, not let through.
        var host = await sample.SharedAsync();
        var reached = false;
        ValueTask<Unit> Reached()
        {
            reached = true;
            return ValueTask.FromResult(Unit.Value);
        }

        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var ofTenants = new TenantsAccessBehavior<DeclaresNothing, Unit>(scope.ServiceProvider.GetRequiredService<AccessChecks<ITenantsRequest>>());

            var handle = async () => await ofTenants.Handle(new DeclaresNothing(), (_, _) => Reached(), Cancellation);
            (await handle.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*DeclaresNothing declares no access requirement*");
        }

        reached.Should().BeFalse("the handler is not reached");

        // Projects' behavior is written from the same template, and stops the same request the same way.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var ofProjects = new ProjectsAccessBehavior<DeclaresNothingOfProjects, Unit>(scope.ServiceProvider.GetRequiredService<AccessChecks<IProjectsRequest>>());

            var pass = async () => await ofProjects.Handle(new DeclaresNothingOfProjects(), (_, _) => Reached(), Cancellation);
            (await pass.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*DeclaresNothingOfProjects declares no access requirement*");
        }

        reached.Should().BeFalse("nor is it here");

        // And Inspections', whose own check asks Projects' gate: the request is stopped before anything is asked.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var ofInspections = new InspectionsAccessBehavior<DeclaresNothingOfInspections, Unit>(scope.ServiceProvider.GetRequiredService<AccessChecks<IInspectionsRequest>>());

            var pass = async () => await ofInspections.Handle(new DeclaresNothingOfInspections(), (_, _) => Reached(), Cancellation);
            (await pass.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*DeclaresNothingOfInspections declares no access requirement*");
        }

        reached.Should().BeFalse("nor here");
    }

    [Fact]
    public async Task A_requirement_no_check_of_the_module_decides_is_refused_before_its_handler()
    {
        // "No branch for this case" is "no check answered" now: a request that declares a case of another module,
        // which its own module registered no check for, is stopped where it is sent, whoever sends it. Ada
        // administers the tenant, and Projects' own check would let her through.
        var host = await sample.SharedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id;
        var reached = false;

        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var ofTenants = new TenantsAccessBehavior<DeclaresAnothersCase, Unit>(scope.ServiceProvider.GetRequiredService<AccessChecks<ITenantsRequest>>());

            var handle = async () => await ofTenants.Handle(
                new DeclaresAnothersCase(MemberAccess.On(ProjectKeys.View, pier)),
                (_, _) =>
                {
                    reached = true;
                    return ValueTask.FromResult(Unit.Value);
                },
                Cancellation);

            (await handle.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*DeclaresAnothersCase declares 'MemberAccess<ProjectId>.On', which none of the access checks registered for ITenantsRequest decides*");
        }

        reached.Should().BeFalse("the handler is not reached");
    }

    /// <summary>A request of Tenancy's that says nothing of what it requires.</summary>
    private sealed record DeclaresNothing : ICommand, ITenantsRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => null!;
    }

    /// <summary>A request of Tenancy's that declares a case its module has no check for.</summary>
    private sealed record DeclaresAnothersCase(AccessRequirement Requires) : ICommand, ITenantsRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>A request of Projects' that says nothing of what it requires.</summary>
    private sealed record DeclaresNothingOfProjects : ICommand, IProjectsRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => null!;
    }

    /// <summary>A request of Inspections' that says nothing of what it requires.</summary>
    private sealed record DeclaresNothingOfInspections : ICommand, IInspectionsRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => null!;
    }

    [Fact]
    public async Task A_sender_is_never_resolved_from_the_root()
    {
        // Production, where a host does not check its scopes unless it is told to. The sender, the handlers and the
        // stores live as long as a request: taken from the root they would live as long as the host, and every
        // request would share one unit of work.
        await using var host = await sample.StartAsync(
            settings: new Dictionary<string, string>
            {
                ["Supabase:Url"] = "http://127.0.0.1:54321",
                ["Supabase:JwtSecret"] = "super-secret-jwt-token-with-at-least-32-characters-long",
            },
            environment: Environments.Production);

        var fromTheRoot = () => host.Services.GetRequiredService<ISender>();
        fromTheRoot.Should().Throw<InvalidOperationException>().WithMessage("*scoped*root*");

        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISender>().Should().NotBeNull();
    }

    [Fact]
    public async Task Only_system_work_marks_a_tenant_as_a_demonstration()
    {
        // A host without the demonstration: one tenant, provisioned here, which nobody has marked.
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string> { [DemoSeeder.Setting] = "false" }, seeded: false);

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SampleTenancy.TenantCommands>().ProvisionAsync(
                new SampleTenancy.TenantToProvision(
                    Harbor.Slug,
                    Harbor.Name,
                    Harbor.Shape,
                    Harbor.Name,
                    Harbor.Administrator.Person.Id,
                    Harbor.Administrator.Person.Name,
                    TenantId: Harbor.Id,
                    AdminSeatId: Harbor.Administrator.Id,
                    ConfigureRoot: root => root.SetKind(DemoTenant.RootKind)),
                Cancellation);
        }

        // Its administrator holds every key that manages access, and may not: the request requires system work,
        // and no key gives that.
        using (AsSeatOf(DemoPeople.Ada))
        {
            var refused = await RefusalOfAsync(host, new MarkTenantAsDemo());
            refused.Code.Should().Be(ToolkitRefusals.SystemOnly);
            refused.Kind.Should().Be(RefusalKind.NotPermitted);
        }

        using (Callers.Begin(Caller.Anonymous))
        {
            (await RefusalOfAsync(host, new MarkTenantAsDemo())).Code.Should().Be(ToolkitRefusals.SystemOnly, "a caller who did not sign in is no system work either");
        }

        // Work nobody began a caller for fails in a host that requires explicit callers, before it is refused or
        // let through: it is a mistake in the calling code, not a caller.
        await using (var unasked = host.Services.CreateAsyncScope())
        {
            var send = async () => await unasked.ServiceProvider.GetRequiredService<ISender>().Send(new MarkTenantAsDemo(), Cancellation);
            await send.Should().ThrowAsync<NoCallerException>();
        }

        // No use case of the package stands between this command and the tenant, so its handler asks again, and
        // narrower: handed the command directly, past the check, it still refuses a seat.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var handler = new MarkTenantAsDemoHandler(
                scope.ServiceProvider.GetRequiredService<SampleTenancy.IStore>(),
                scope.ServiceProvider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>());

            var handle = async () => await handler.Handle(new MarkTenantAsDemo(), Cancellation);
            (await handle.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.SystemOnly);
        }

        (await IsDemoAsync(host)).Should().BeFalse("a refused command changes nothing");

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new MarkTenantAsDemo(), Cancellation);
        }

        (await IsDemoAsync(host)).Should().BeTrue();
    }

    [Fact]
    public async Task A_command_of_the_package_s_is_refused_at_the_door_as_its_use_case_refuses_it_past_the_door()
    {
        // A request handed to a use case of Tenancy's says the first thing that use case asks. Juno, a surveyor,
        // holds none of the keys that manage the organization: each command is refused before its handler runs,
        // and handed to its handler directly, past the door, the use case refuses her the same way, with the same
        // key, and at the same unit. Nothing is changed either way.
        await using var host = await sample.StartAsync();
        var unit = Harbor.Root;
        var seat = Harbor.Administrator.Id;
        var role = Harbor.AdministratorsRole;
        IMessage[] commands =
        [
            new AddOrganizationUnit(unit, "Annex", UnitKind.Region),
            new MakePlacement(seat, unit, Primary: false),
            new WithdrawPlacement(seat, unit),
            new MakeGrant(seat, unit, role, Until: null, Reason: null),
            new RevokeGrant(seat, unit, role),
            new SuspendTenantSeat(seat),
            new ReactivateTenantSeat(seat),
            new DeactivateTenantSeat(seat),
            new CreateTenantRole("Storekeeper", "Keeps the stores", []),
            new SetRoleKeys(role, []),
            new ArchiveTenantRole(role),
            new ChangeTenantShape(TenantShape.Hierarchical),
            new InvitePerson("wren@example.test", unit, role, Until: null, DisplayName: null),
        ];

        using (AsSeatOf(DemoPeople.Juno))
        {
            foreach (var command in commands)
            {
                var name = command.GetType().Name;
                var declared = ((IRequireAccess)command).RequiredAccess;
                var atTheDoor = await RefusalAtTheDoorAsync(host, command);
                var pastTheDoor = await RefusalPastTheDoorAsync(host, command);

                atTheDoor.Code.Should().Be(TenancyRefusals.NotPermitted, "{0} requires a key juno does not hold", name);
                pastTheDoor.Code.Should().Be(atTheDoor.Code, "the use case of {0} asks first what its request requires", name);
                atTheDoor.Arguments["Key"].Should().Be(declared is TenancyRequirement.AtUnit<OrganizationUnitId> at ? at.Key : ((TenancyRequirement.ForTheWholeTenant)declared).Key, name)
                    .And.Be(pastTheDoor.Arguments["Key"], "the door and the use case name the same key for {0}", name);
                if (declared is TenancyRequirement.AtUnit<OrganizationUnitId> atUnit)
                {
                    atTheDoor.Arguments["Unit"].Should().Be(atUnit.Unit, name).And.Be(pastTheDoor.Arguments["Unit"], "and the same unit for {0}", name);
                }
            }
        }
    }

    [Fact]
    public async Task A_project_role_is_changed_only_by_who_manages_roles_past_the_check_too()
    {
        await using var host = await sample.StartAsync();
        var observer = Harbor.ProjectRoles[SampleCatalogue.Observer];
        var archive = new ArchiveProjectRole(observer);

        // Nothing of a package's stands between this command and the role, so its handler asks again what the
        // command declared. Leo leads a crew and manages no roles: handed the command directly, past the check, the
        // handler refuses him as the check does, naming the key.
        using (AsSeatOf(DemoPeople.Leo))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var handle = async () => await ArchiveHandlerOf(scope).Handle(archive, Cancellation);
            var refused = (await handle.Should().ThrowAsync<RefusalException>()).Which;
            refused.Code.Should().Be(TenancyRefusals.NotPermitted);
            refused.Kind.Should().Be(RefusalKind.NotPermitted);
            refused.Arguments["Key"].Should().Be(TenancyKeys.RolesManage);
        }

        // Nobody is refused as nobody, before anything is read.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var handle = async () => await ArchiveHandlerOf(scope).Handle(archive, Cancellation);
            (await handle.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.NotSeated);
        }

        (await StatusOfAsync(host, observer)).Should().Be(KeptRoleStatus.Active, "a refused command changes nothing");

        // Maud holds the key for the whole tenant, so the handler goes on for her wherever it runs.
        using (AsSeatOf(DemoPeople.Maud))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await ArchiveHandlerOf(scope).Handle(archive, Cancellation);
        }

        (await StatusOfAsync(host, observer)).Should().Be(KeptRoleStatus.Archived);
    }

    [Fact]
    public async Task Queries_of_one_request_run_side_by_side_each_on_a_context_of_its_own()
    {
        var sideBySide = new SideBySide();
        await using var host = await sample.StartAsync(
            services => services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(sideBySide)));

        using (AsSeatOf(DemoPeople.Ada))
        {
            // One scope, as one request has, and one sender.
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var ofTheScope = scope.ServiceProvider.GetRequiredService<TenantsContext>();

            // What each answers alone, to compare with.
            var seats = await sender.Send(new TenantSeats(), Cancellation);
            var roles = await sender.Send(new TenantRoles(), Cancellation);
            var held = await sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation);

            // Two queries the Tenancy package answers, each in a scope of its own.
            var packageQueries = sideBySide.WatchThisFlow();
            var seatsAtOnce = sender.Send(new TenantSeats(), Cancellation).AsTask();
            var rolesAtOnce = sender.Send(new TenantRoles(), Cancellation).AsTask();
            await Task.WhenAll(seatsAtOnce, rolesAtOnce);
            (await seatsAtOnce).Should().BeEquivalentTo(seats);
            (await rolesAtOnce).Should().BeEquivalentTo(roles);
            packageQueries.Met.Should().BeTrue("the two queries ran at the same time, or this proves nothing");
            packageQueries.Contexts.Should().HaveCountGreaterThanOrEqualTo(2).And.NotContain(ofTheScope);

            // The same query twice, so one handler answers both at once, each on a context from the factory.
            var sameQuery = sideBySide.WatchThisFlow();
            var twice = await Task.WhenAll(
                sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation).AsTask(),
                sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation).AsTask());
            twice.Should().AllBeEquivalentTo(held);
            sameQuery.Met.Should().BeTrue("the two queries ran at the same time, or this proves nothing");
            sameQuery.Contexts.Should().HaveCount(2).And.NotContain(ofTheScope);

            // A query of each kind, and one that reads the caller itself.
            var mixed = sideBySide.WatchThisFlow();
            var overview = sender.Send(new OverviewOfMine(), Cancellation).AsTask();
            var units = sender.Send(new OrganizationUnits(), Cancellation).AsTask();
            var heldAgain = sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation).AsTask();
            await Task.WhenAll(overview, units, heldAgain);
            (await overview).Seat.Id.Should().Be(Harbor.SeatOf(DemoPeople.Ada));
            (await heldAgain).Should().BeEquivalentTo(held);
            mixed.Met.Should().BeTrue();

            // Three reads, three contexts taken from the pool. Counted by rental, since a read that starts when
            // another is done may be handed the very instance that one gave back.
            mixed.Rentals.Should().HaveCountGreaterThanOrEqualTo(3);
            mixed.Contexts.Should().NotContain(ofTheScope);

            // The request's own context, the unit of work of its commands, was never read on, and tracks nothing.
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty("a query reads on a context of its own, and tracks nothing on the request's");
        }
    }

    [Fact]
    public async Task Queries_sent_side_by_side_each_take_a_context_from_the_pool()
    {
        // One watcher on every module's contexts, so queries of all three meet.
        var sideBySide = new SideBySide();
        await using var host = await sample.StartAsync(
            services =>
            {
                services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(sideBySide));
                services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(sideBySide));
                services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(sideBySide));
            });
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        using (AsSeatOf(DemoPeople.Ada))
        {
            // One scope, as one request has, and one sender. The request's own contexts are taken from the pools
            // too, when the scope asks for them, and are the scope's until it ends.
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            DbContext[] ofTheScope =
            [
                scope.ServiceProvider.GetRequiredService<TenantsContext>(),
                scope.ServiceProvider.GetRequiredService<ProjectsContext>(),
                scope.ServiceProvider.GetRequiredService<InspectionsContext>(),
            ];
            ofTheScope.Should().OnlyContain(context => context.IsPooled());

            // What each answers alone, to compare with.
            var held = await sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation);
            var seats = await sender.Send(new TenantSeats(), Cancellation);
            var list = await sender.Send(new VisibleProjects(), Cancellation);
            var detail = await sender.Send(new ProjectDetail(pier), Cancellation);
            var inspections = await sender.Send(new ProjectInspections(pier), Cancellation);

            // A query of every module and of every kind, at once, four times over: one that asks a key, one the
            // Tenancy package answers, a list, a detail behind its access check, and one that asks another module's gate.
            var instances = new HashSet<DbContext>();
            var rentals = new HashSet<DbContextId>();
            for (var round = 0; round < 4; round++)
            {
                var encounter = sideBySide.WatchThisFlow();
                var heldAtOnce = sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation).AsTask();
                var seatsAtOnce = sender.Send(new TenantSeats(), Cancellation).AsTask();
                var listAtOnce = sender.Send(new VisibleProjects(), Cancellation).AsTask();
                var detailAtOnce = sender.Send(new ProjectDetail(pier), Cancellation).AsTask();
                var inspectionsAtOnce = sender.Send(new ProjectInspections(pier), Cancellation).AsTask();
                await Task.WhenAll(heldAtOnce, seatsAtOnce, listAtOnce, detailAtOnce, inspectionsAtOnce);

                (await heldAtOnce).Should().BeEquivalentTo(held);
                (await seatsAtOnce).Should().BeEquivalentTo(seats);
                (await listAtOnce).Should().BeEquivalentTo(list, options => options.WithStrictOrdering());
                (await detailAtOnce).Should().BeEquivalentTo(detail);
                (await inspectionsAtOnce).Should().BeEquivalentTo(inspections, options => options.WithStrictOrdering());

                encounter.Met.Should().BeTrue("the queries ran at the same time, or this proves nothing");
                encounter.AllPooled.Should().BeTrue("every read takes its context from a pool");
                encounter.Contexts.Should().NotContain(ofTheScope, "and none reads on the request's own");
                instances.UnionWith(encounter.Contexts);
                rentals.UnionWith(encounter.Rentals);
            }

            // A context a read gave back is the next read's: far fewer instances than rentals.
            rentals.Count.Should().BeGreaterThan(instances.Count, "the pools handed their contexts out again");
            ofTheScope.Should().OnlyContain(context => !context.ChangeTracker.Entries().Any(), "a query tracks nothing on the request's contexts");
        }
    }

    [Fact]
    public async Task A_query_rents_no_context_of_the_request_s()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(
            services =>
            {
                services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(counter));
                services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter));
                services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(counter));
            });
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            // One query, one statement, on one context: taken from the pool for it, and given back.
            counter.WatchThisFlow();
            await sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation);
            var read = counter.Rentals.ToList();
            read.Should().ContainSingle("the query reads on one context");

            // A query of every module and of every kind: one the Tenancy package answers, in a scope of its own; a
            // list; a detail behind its access check; and one that asks another module's gate first.
            foreach (var send in new Func<Task>[]
            {
                async () => await sender.Send(new TenantSeats(), Cancellation),
                async () => await sender.Send(new VisibleProjects(), Cancellation),
                async () => await sender.Send(new ProjectDetail(pier), Cancellation),
                async () => await sender.Send(new ProjectInspections(pier), Cancellation),
            })
            {
                counter.WatchThisFlow();
                await send();
                counter.Rentals.Should().NotBeEmpty("the query read something");
                read.AddRange(counter.Rentals);
            }

            // Only now does the scope ask for its own contexts. Had a query read on the request's context, the scope
            // would be holding that context still, and this would be the very rental the query read on. It is not:
            // even where the pool hands the scope an instance a query used, it is another rental of it.
            foreach (var ofTheScope in new DbContext[]
            {
                scope.ServiceProvider.GetRequiredService<TenantsContext>(),
                scope.ServiceProvider.GetRequiredService<ProjectsContext>(),
                scope.ServiceProvider.GetRequiredService<InspectionsContext>(),
            })
            {
                read.Should().NotContain(ofTheScope.ContextId, "no query took the request's {0}", ofTheScope.GetType().Name);
                ofTheScope.ChangeTracker.Entries().Should().BeEmpty();
            }

            // The control: a command does save on the request's context, and that rental is counted.
            counter.WatchThisFlow();
            await sender.Send(new ChangeProjectName(pier, "Pier 7, east"), Cancellation);
            counter.Rentals.Should().Contain(scope.ServiceProvider.GetRequiredService<ProjectsContext>().ContextId, "what changes is saved on the request's context");
        }
    }

    [Fact]
    public async Task Two_reads_at_once_on_one_context_are_what_entity_framework_refuses()
    {
        // The control for the test above: what would happen had the queries shared the request's context. Held the
        // same way, the second read is refused the moment it starts.
        var sideBySide = new SideBySide();
        await using var host = await sample.StartAsync(
            services => services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(sideBySide)));

        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var shared = scope.ServiceProvider.GetRequiredService<TenantsContext>();

            var encounter = sideBySide.WatchThisFlow();
            var first = shared.Seats.AsNoTracking().ToListAsync(Cancellation);
            var second = () => shared.Roles.AsNoTracking().ToListAsync(Cancellation);

            (await second.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*second operation*");
            encounter.Release();
            (await first).Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task Queries_on_projects_run_side_by_side_through_their_access_checks()
    {
        var sideBySide = new SideBySide();
        await using var host = await sample.StartAsync(
            services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(sideBySide)));
        var pier = Harbor.ProjectNamed("Pier 7").Id;
        var bridge = Harbor.ProjectNamed("Bay bridge").Id;

        using (AsSeatOf(DemoPeople.Ada))
        {
            // One scope, as one request has, and one sender: so one access check, and one handler of each kind.
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var ofTheScope = scope.ServiceProvider.GetRequiredService<ProjectsContext>();

            // What each answers alone, to compare with.
            var list = await sender.Send(new VisibleProjects(), Cancellation);
            var ofPier = await sender.Send(new ProjectDetail(pier), Cancellation);
            var ofBridge = await sender.Send(new ProjectDetail(bridge), Cancellation);
            var key = await sender.Send(new KeyOnProject(pier, ProjectKeys.Edit), Cancellation);
            ofPier.Id.Should().Be(pier);
            ofBridge.Id.Should().Be(bridge);

            // The same query for two projects, so the one check and the one handler serve both at once. Each is
            // checked on a context of its own, and what the check keeps, it keeps for its own request.
            var twoDetails = sideBySide.WatchThisFlow();
            var details = await Task.WhenAll(
                sender.Send(new ProjectDetail(pier), Cancellation).AsTask(),
                sender.Send(new ProjectDetail(bridge), Cancellation).AsTask());
            details[0].Should().BeEquivalentTo(ofPier);
            details[1].Should().BeEquivalentTo(ofBridge);
            twoDetails.Met.Should().BeTrue("the two queries ran at the same time, or this proves nothing");
            twoDetails.Contexts.Should().HaveCountGreaterThanOrEqualTo(2).And.NotContain(ofTheScope);

            // A query of each kind: one filtered by what the caller sees, one checked on a project, one that asks a key.
            var mixed = sideBySide.WatchThisFlow();
            var listAtOnce = sender.Send(new VisibleProjects(), Cancellation).AsTask();
            var detailAtOnce = sender.Send(new ProjectDetail(bridge), Cancellation).AsTask();
            var keyAtOnce = sender.Send(new KeyOnProject(pier, ProjectKeys.Edit), Cancellation).AsTask();
            await Task.WhenAll(listAtOnce, detailAtOnce, keyAtOnce);
            (await listAtOnce).Should().BeEquivalentTo(list, options => options.WithStrictOrdering());
            (await detailAtOnce).Should().BeEquivalentTo(ofBridge);
            (await keyAtOnce).Should().Be(key);
            mixed.Met.Should().BeTrue();

            // Three reads, each checked and answered on contexts taken from the pool for it. Counted by rental,
            // since a read that starts when another is done may be handed the very instance that one gave back.
            mixed.Rentals.Should().HaveCountGreaterThanOrEqualTo(3);
            mixed.Contexts.Should().NotContain(ofTheScope);

            // The request's own context, the unit of work of its commands, was never read on, and tracks nothing.
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty("a query reads on a context of its own, and tracks nothing on the request's");
        }
    }

    [Fact]
    public async Task Queries_on_inspections_run_side_by_side_each_asking_the_gate()
    {
        // Two watchers, because a query of Inspections reads in two places: its access check asks Projects' gate,
        // which reads Projects' storage, and its handler reads Inspections' own. Each is held until a second arrives.
        var gateAsks = new SideBySide();
        var lists = new SideBySide();
        await using var host = await sample.StartAsync(
            services =>
            {
                services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(gateAsks));
                services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(lists));
            });
        var pier = Harbor.ProjectNamed("Pier 7").Id;
        var bridge = Harbor.ProjectNamed("Bay bridge").Id;

        using (AsSeatOf(DemoPeople.Ada))
        {
            // Something to list on each project, recorded as a request of its own would.
            foreach (var (project, title) in new[] { (pier, "Loose railing"), (bridge, "Cracked pillar") })
            {
                await using var recording = host.Services.CreateAsyncScope();
                await recording.ServiceProvider.GetRequiredService<ISender>().Send(new RecordInspection(project, title), Cancellation);
            }

            // One scope, as one request has, and one sender: so one access check, and one handler.
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var projectsOfTheScope = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
            var inspectionsOfTheScope = scope.ServiceProvider.GetRequiredService<InspectionsContext>();

            // What each answers alone, to compare with.
            var ofPier = await sender.Send(new ProjectInspections(pier), Cancellation);
            var ofBridge = await sender.Send(new ProjectInspections(bridge), Cancellation);
            ofPier.Items.Select(item => item.Title).Should().Equal("Loose railing");
            ofBridge.Items.Select(item => item.Title).Should().Equal("Cracked pillar");

            // The same query for two projects, so the one check and the one handler serve both at once. Each asks
            // the gate on a context of the gate's own, and each list is read on a context of its own.
            var checks = gateAsks.WatchThisFlow();
            var reads = lists.WatchThisFlow();
            var both = await Task.WhenAll(
                sender.Send(new ProjectInspections(pier), Cancellation).AsTask(),
                sender.Send(new ProjectInspections(bridge), Cancellation).AsTask());
            both[0].Should().BeEquivalentTo(ofPier, options => options.WithStrictOrdering());
            both[1].Should().BeEquivalentTo(ofBridge, options => options.WithStrictOrdering());
            checks.Met.Should().BeTrue("the two access checks ran at the same time, or this proves nothing");
            checks.Contexts.Should().HaveCountGreaterThanOrEqualTo(2).And.NotContain(projectsOfTheScope);
            reads.Met.Should().BeTrue("the two lists were read at the same time, or this proves nothing");
            reads.Contexts.Should().HaveCount(2).And.NotContain(inspectionsOfTheScope);

            // Next to queries of the other modules, as the resolvers of one request would ask them.
            checks = gateAsks.WatchThisFlow();
            reads = lists.WatchThisFlow();
            var pierAtOnce = sender.Send(new ProjectInspections(pier), Cancellation).AsTask();
            var bridgeAtOnce = sender.Send(new ProjectInspections(bridge), Cancellation).AsTask();
            var detailAtOnce = sender.Send(new ProjectDetail(pier), Cancellation).AsTask();
            var seatsAtOnce = sender.Send(new TenantSeats(), Cancellation).AsTask();
            await Task.WhenAll(pierAtOnce, bridgeAtOnce, detailAtOnce, seatsAtOnce);
            (await pierAtOnce).Should().BeEquivalentTo(ofPier, options => options.WithStrictOrdering());
            (await bridgeAtOnce).Should().BeEquivalentTo(ofBridge, options => options.WithStrictOrdering());
            (await detailAtOnce).Id.Should().Be(pier);
            (await seatsAtOnce).Should().NotBeEmpty();
            checks.Met.Should().BeTrue();
            reads.Met.Should().BeTrue();
            reads.Contexts.Should().HaveCount(2).And.NotContain(inspectionsOfTheScope);

            // The request's own contexts, the units of work of its commands, were never read on, and track nothing.
            projectsOfTheScope.ChangeTracker.Entries().Should().BeEmpty("the gate answers on a context of its own");
            inspectionsOfTheScope.ChangeTracker.Entries().Should().BeEmpty("a query reads on a context of its own, and tracks nothing on the request's");
        }
    }

    [Fact]
    public async Task Recording_an_inspection_is_refused_in_the_order_it_always_was()
    {
        // What a caller is refused with says what was looked at first: the caller, then the seat it records as,
        // then the project (out of sight, closed, not permitted), and the title last. The first three are the
        // access check's, which the request passes before its handler; the title is the inspection's own.
        await using var host = await sample.StartAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id;
        var outOfSight = DemoData.Meadow.ProjectNamed("Garden shed").Id;
        const string Blank = "   ";

        // Nobody, whatever the project and the title.
        (await RefusalOfAsync(host, new RecordInspection(outOfSight, Blank))).Code.Should().Be(TenancyRefusals.NotSeated);

        // System work that acts for no seat is a mistake in the calling code, found before the project is asked about.
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordInspection(outOfSight, Blank), Cancellation);
            (await send.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*the seat it acts for*");
        }

        // Vic is Pier 7's observer: he sees it and may not record on it. A project of another tenant he does not see.
        using (AsSeatOf(DemoPeople.Vic))
        {
            (await RefusalOfAsync(host, new RecordInspection(outOfSight, Blank))).Code.Should().Be(InspectionRefusals.ProjectNotFound);

            var notPermitted = await RefusalOfAsync(host, new RecordInspection(pier, Blank));
            notPermitted.Code.Should().Be(InspectionRefusals.ProjectNotPermitted, "the key is asked about before the title is read");
            notPermitted.Arguments["Key"].Should().Be(RecordInspection.RequiredKey);
        }

        // Juno may record, so hers is the first request to reach the handler, and the inspection refuses its title.
        using (AsSeatOf(DemoPeople.Juno))
        {
            (await RefusalOfAsync(host, new RecordInspection(pier, Blank))).Code.Should().Be(InspectionRefusals.TitleInvalid);
        }

        using (AsSeatOf(DemoPeople.Rhea))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CloseProject(pier), Cancellation);
        }

        // A closed project refuses every addition, whoever asks: closed comes before not permitted.
        using (AsSeatOf(DemoPeople.Vic))
        {
            (await RefusalOfAsync(host, new RecordInspection(pier, Blank))).Code.Should().Be(InspectionRefusals.ProjectClosed);

            // Listing asks for the project, not for an open one: he still sees a closed project's inspections.
            await using var scope = host.Services.CreateAsyncScope();
            var list = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ProjectInspections(pier), Cancellation);
            list.CanRecord.Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_query_tracks_nothing()
    {
        await using var host = await sample.StartAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        using (AsSeatOf(DemoPeople.Ada))
        {
            // Something for Inspections' query to read, recorded as a request of its own would.
            await using (var recording = host.Services.CreateAsyncScope())
            {
                await recording.ServiceProvider.GetRequiredService<ISender>().Send(new RecordInspection(pier, "Loose railing"), Cancellation);
            }

            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var ofTheScope = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
            var inspectionsOfTheScope = scope.ServiceProvider.GetRequiredService<InspectionsContext>();

            // Inspections' query, with its access check before it, which asks Projects' gate: neither leaves
            // anything in either module's unit of work.
            (await sender.Send(new ProjectInspections(pier), Cancellation)).Items.Should().ContainSingle();
            inspectionsOfTheScope.ChangeTracker.Entries().Should().BeEmpty();
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();

            // Its command does track: the inspection it made, and saved. The gate it passed still tracked nothing.
            await sender.Send(new RecordInspection(pier, "Loose plank on the gangway"), Cancellation);
            inspectionsOfTheScope.ChangeTracker.Entries<Inspection>().Should().ContainSingle();
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();

            // Each query of Projects, with its access check before it. What a query read is data: nothing of it is
            // left in the unit of work a command of the same request would save.
            (await sender.Send(new VisibleProjects(), Cancellation)).Items.Should().NotBeEmpty();
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();

            (await sender.Send(new ProjectDetail(pier), Cancellation)).Id.Should().Be(pier);
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();

            (await sender.Send(new CrewsOfProjects([pier]), Cancellation))[pier].Members.Should().NotBeEmpty();
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();

            (await sender.Send(new KeyOnProject(pier, ProjectKeys.Edit), Cancellation)).Allowed.Should().BeTrue();
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty();

            // A command does track: the project it loaded to change, and saved.
            await sender.Send(new ChangeProjectName(pier, "Pier 7, east"), Cancellation);
            ofTheScope.ChangeTracker.Entries<Project>().Should().ContainSingle();
        }
    }

    [Fact]
    public async Task A_project_changed_between_the_check_and_the_load_is_a_lost_race_for_a_caller_that_named_its_version()
    {
        // A step registered after everything else runs after the access check and right before the handler: there
        // it changes the project, in a scope of its own, as another request arriving in between would.
        await using var host = await sample.StartAsync(
            services => services.AddScoped<IPipelineBehavior<ChangeProjectName, Unit>, RenamedMeanwhile>());
        var pier = Harbor.ProjectNamed("Pier 7");
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var read = (await ada.ProjectDetailAsync(pier)).GetProperty("version").GetInt64();

        // Ada names the version she read, which the check finds current. The handler loads the project as it is now,
        // and holds it to her version there: what she decided was decided about another project, the same answer as
        // a save that came second.
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/projects/{pier.Id.Value}/name") { Content = JsonContent.Create(new { name = "Pier 7, east" }) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{read}\"");
        using var renamed = await ada.SendAsync(request, Cancellation);

        await renamed.ShouldBeRefusedAsync(HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict);
        (await ada.ProjectDetailAsync(pier)).GetProperty("name").GetString().Should().Be(RenamedMeanwhile.Name, "the request that lost the race changed nothing");
    }

    /// <summary>Renames the project a <see cref="ChangeProjectName"/> is about before its handler runs, in a scope of its own.</summary>
    private sealed class RenamedMeanwhile(IServiceScopeFactory scopes) : IPipelineBehavior<ChangeProjectName, Unit>
    {
        public const string Name = "Renamed meanwhile";

        public async ValueTask<Unit> Handle(ChangeProjectName message, MessageHandlerDelegate<ChangeProjectName, Unit> next, CancellationToken cancellationToken)
        {
            await using (var scope = scopes.CreateAsyncScope())
            {
                var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
                var project = await projects.Projects.AsTracking().SingleAsync(candidate => candidate.Id == message.Id, cancellationToken);
                project.Rename(Name);
                await projects.SaveChangesAsync(cancellationToken);
            }

            return await next(message, cancellationToken);
        }
    }

    [Fact]
    public async Task A_crew_role_taken_between_the_check_and_the_save_leaves_the_write_to_the_database_which_refuses_it()
    {
        // The same step, taking away the crew role the caller was checked with. Who may rename was decided before the
        // handler, about a lead; the database checks the write as the caller he is by then, who holds no key that
        // writes the project, and refuses it, so a caller who was a lead when checked and is none when the handler
        // runs changes nothing.
        await using var host = await sample.StartAsync(
            services => services.AddScoped<IPipelineBehavior<ChangeProjectName, Unit>, LeadRoleTakenMeanwhile>());
        var pier = Harbor.ProjectNamed("Pier 7");
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        using (var made = await leo.GiveCrewRoleAsync(pier, LeadRoleTakenMeanwhile.From, LeadRoleTakenMeanwhile.Role))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var renamed = await vic.PutAsJsonAsync($"/projects/{pier.Id.Value}/name", new { name = "Pier 7, east" }, Cancellation);

        await renamed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ToolkitRefusals.Refused);
        var after = await vic.ProjectDetailAsync(pier);
        after.GetProperty("name").GetString().Should().Be("Pier 7", "the request the database refused changed nothing");
        after.GetProperty("can").GetProperty("rename").GetBoolean().Should().BeFalse("and he is no lead any more");
    }

    /// <summary>
    /// Takes the crew lead's role from Vic on the project a <see cref="ChangeProjectName"/> is about before its
    /// handler runs: in a scope of its own and as Leo, the project's owner, as his request arriving in between
    /// would. Not as Vic, whose request this step runs in: a seat gives up a role of its own through the
    /// application's own work, and a save of his own that took his lead's role is one the row rules refuse.
    /// </summary>
    private sealed class LeadRoleTakenMeanwhile(IServiceScopeFactory scopes) : IPipelineBehavior<ChangeProjectName, Unit>
    {
        public static SeatId From => DemoData.Harbor.SeatOf(DemoPeople.Vic);

        public static ProjectRoleId Role => DemoData.Harbor.ProjectRoles[SampleCatalogue.CrewLead];

        public async ValueTask<Unit> Handle(ChangeProjectName message, MessageHandlerDelegate<ChangeProjectName, Unit> next, CancellationToken cancellationToken)
        {
            using (SampleCallers.BeginSeatOf(DemoPeople.Leo, DemoData.Harbor))
            {
                await using var scope = scopes.CreateAsyncScope();
                var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
                var project = await projects.Projects.AsTracking().SingleAsync(candidate => candidate.Id == message.Id, cancellationToken);
                project.TakeCrewRole(From, Role, leadRole: null);
                await projects.SaveChangesAsync(cancellationToken);
            }

            return await next(message, cancellationToken);
        }
    }

    [Fact]
    public async Task A_key_taken_from_a_crew_role_since_the_check_is_refused_by_the_database_and_logged_as_a_change_of_rights()
    {
        // The same step, taking the key away from the role instead of the role from the caller. A project role is a
        // row of its own, kept for every crew it is given on, so the project's version stays where it was and this is
        // no lost race over the project: the handler loads what was checked, renames it and saves, and the policies,
        // which read the role's keys as they are now, leave the statement no row to change. The caller is refused;
        // and the toolkit asks the request's access check again, which refuses as well, so it logs that the caller's
        // rights changed in between, not a warning that C# and the policies disagree: they agreed.
        var logs = new RecordingLoggerProvider();
        await using var host = await sample.StartAsync(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.AddScoped<IPipelineBehavior<ChangeProjectName, Unit>, LeadRoleRekeyedMeanwhile>();
        });
        var pier = Harbor.ProjectNamed("Pier 7");
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        using (var made = await leo.GiveCrewRoleAsync(pier, LeadRoleRekeyedMeanwhile.Lead, LeadRoleRekeyedMeanwhile.Role))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var renamed = await vic.PutAsJsonAsync($"/projects/{pier.Id.Value}/name", new { name = "Pier 7, east" }, Cancellation);

        await renamed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ToolkitRefusals.Refused);
        var after = await vic.ProjectDetailAsync(pier);
        after.GetProperty("name").GetString().Should().Be("Pier 7", "the refused save changed nothing");
        after.GetProperty("can").GetProperty("rename").GetBoolean().Should().BeFalse("his role no longer gives the key");

        var said = logs.Entries.Where(entry => entry.Category == typeof(DatabaseRefusalInterceptor).FullName).ToList();
        said.Should().ContainSingle("the refusal is said once").Which.Level.Should().Be(LogLevel.Information, "a change of rights is no disagreement");
        said[0].Message.Should().Be(
            "The database refused a save to projects.Projects after the access check of ChangeProjectName (MemberAccess<ProjectId>.On) let the caller through. Asked again, the check refuses as well: "
            + "the caller's rights changed between the check and the save, and the caller is refused.");
    }

    /// <summary>
    /// Leaves the crew lead's role giving nothing but the view of a project before the handler of a
    /// <see cref="ChangeProjectName"/> runs: in a scope of its own and as Ada, who manages the tenant's roles, as her
    /// request arriving in between would.
    /// </summary>
    private sealed class LeadRoleRekeyedMeanwhile(IServiceScopeFactory scopes) : IPipelineBehavior<ChangeProjectName, Unit>
    {
        public static SeatId Lead => DemoData.Harbor.SeatOf(DemoPeople.Vic);

        public static ProjectRoleId Role => DemoData.Harbor.ProjectRoles[SampleCatalogue.CrewLead];

        public async ValueTask<Unit> Handle(ChangeProjectName message, MessageHandlerDelegate<ChangeProjectName, Unit> next, CancellationToken cancellationToken)
        {
            using (SampleCallers.BeginSeatOf(DemoPeople.Ada, DemoData.Harbor))
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SetProjectRoleKeys(Role, [ProjectKeys.View]), cancellationToken);
            }

            return await next(message, cancellationToken);
        }
    }

    [Fact]
    public async Task A_handler_reached_past_the_mediator_is_held_by_the_database_to_what_its_caller_may()
    {
        await using var host = await sample.StartAsync();
        var pier = Harbor.ProjectNamed("Pier 7");

        // Called directly, a command passes no check, and what holds it is the database, which checks every write as
        // the caller. Vic is an observer on Pier 7's crew: he sees the project and holds no key that writes it, nor one
        // at any unit, so he closes nothing, and opens nothing, though the unit is there and the number is free.
        using (AsSeatOf(DemoPeople.Vic))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var close = ActivatorUtilities.CreateInstance<CloseProjectHandler>(scope.ServiceProvider);
            var closing = async () => await close.Handle(new CloseProject(pier.Id), Cancellation);
            (await closing.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.Refused);
        }

        using (AsSeatOf(DemoPeople.Vic))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var open = ActivatorUtilities.CreateInstance<OpenProjectHandler>(scope.ServiceProvider);
            var opening = async () => await open.Handle(new OpenProject("P-901", "Past the check", Harbor.Root), Cancellation);
            (await opening.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.Refused);

            // Sent, the same command is checked, and refused at the door for the key he does not hold there.
            (await RefusalOfAsync(host, new OpenProject("P-901", "Past the check", Harbor.Root))).Code.Should().Be(ProjectRefusals.NotPermitted);
        }

        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var detail = await ada.ProjectDetailAsync(pier);
        detail.GetProperty("state").GetString().Should().Be("open", "the handler that was called directly closed nothing");
        (await ada.VisibleProjectsAsync()).Names().Should().NotContain("Past the check", "and the one that was called directly opened nothing");
    }

    [Fact]
    public async Task A_handler_of_inspections_reached_past_the_mediator_records_and_shows_nothing_its_caller_may_not_see()
    {
        var host = await sample.SharedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        // Hana does not see Pier 7. Called directly, the command asked Projects' gate nothing before its handler, and
        // the handler asks it for the project's planned range: there is no project for her to record on.
        using (AsSeatOf(DemoPeople.Hana))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var record = ActivatorUtilities.CreateInstance<RecordInspectionHandler>(scope.ServiceProvider);
            var recording = async () => await record.Handle(new RecordInspection(pier, "Loose railing"), Cancellation);
            (await recording.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ProjectRefusals.NotFound);

            // And the query shows her nothing: the database answers its statement as her, and she may record nowhere.
            var listed = await ActivatorUtilities.CreateInstance<ProjectInspectionsHandler>(scope.ServiceProvider).Handle(new ProjectInspections(pier), Cancellation);
            listed.Items.Items.Should().BeEmpty();
            listed.CanRecord.Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_host_that_leaves_a_module_s_access_behavior_out_does_not_start_and_says_the_line_that_adds_it()
    {
        // The module's checks are registered and the behavior that asks them is not, so its requests would reach
        // their handlers with nothing asking what they require. Registering the checks brought the start-up check
        // that says so, which reads the module's requests from their handlers, and the host gives up before it
        // serves anything.
        await using var host = await sample.NotStartedAsync(services: services =>
            services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(ProjectsAccessBehavior<,>))));

        var refused = host.RefusedStart();

        refused.Where(failure => failure.Data[StartupChecks.FailedCheckKey] as string == AccessBehaviorChecks.BehaviorsRegisteredCheck)
            .Select(failure => failure.Message)
            .Should().Contain(
                message => message.StartsWith("ProjectsAccessBehavior<TMessage, TResponse>, the pipeline behavior the toolkit wrote to ask the access checks of IProjectsRequest, is not in the pipeline:", StringComparison.Ordinal)
                    && message.EndsWith("Add services.AddProjectsAccessBehavior() where the module registers its checks.", StringComparison.Ordinal),
                "the host gave up with {0}",
                string.Join(" / ", refused.Select(failure => failure.Message)));
    }

    [Fact]
    public async Task A_host_that_leaves_a_module_s_access_check_out_still_has_tenancy_s_commands_ask_again()
    {
        // What a program gets that registers a module's handlers and leaves its access behavior out of the pipeline.
        // Only the behaviors are taken out here: the checks are still registered, so every handler can be made. The
        // start-up check of the behaviors would stop the host, as the test above shows, so it is turned off, which is
        // what it takes to get here. Nothing is seeded, since the seeder's own commands would run unchecked the same
        // way. What stands then is what asks again past the behavior.
        Type[] checks = [typeof(ProjectsAccessBehavior<,>), typeof(InspectionsAccessBehavior<,>), typeof(TenantsAccessBehavior<,>)];
        await using var host = await sample.StartAsync(
            seeded: false,
            settings: new Dictionary<string, string> { [DemoSeeder.Setting] = "false" },
            services: services => RemoveAll(services, checks));

        var seat = SeatId.CreateSequential();
        var role = ProjectRoleId.CreateSequential();

        using (AsSeatOf(DemoPeople.Ada))
        {
            // Tenancy's check keeps nothing for its handlers, and needs to keep nothing. Every command of the
            // package's goes to a use case of the package, which asks again for what its request requires, whoever
            // calls it; the commands that are the modules' own, marking a tenant as a demonstration and those of the
            // project roles, each ask again what they declared. So the seat is refused before the handler looks for
            // the role, which is not there.
            (await RefusalOfAsync(host, new MarkTenantAsDemo())).Code.Should().Be(ToolkitRefusals.SystemOnly);
            (await RefusalOfAsync(host, new SetUpProjectRoles())).Code.Should().Be(ToolkitRefusals.SystemOnly);
            (await RefusalOfAsync(host, new MakeProjectRole("Rigger", null, []))).Code.Should().Be(TenancyRefusals.NotPermitted);
            (await RefusalOfAsync(host, new RenameProjectRole(role, "Rigger", null))).Code.Should().Be(TenancyRefusals.NotPermitted);
            (await RefusalOfAsync(host, new SetProjectRoleKeys(role, []))).Code.Should().Be(TenancyRefusals.NotPermitted);
            (await RefusalOfAsync(host, new ArchiveProjectRole(role))).Code.Should().Be(TenancyRefusals.NotPermitted);
        }

        // As nobody, a command of the package's is refused by the package, as it is behind the check.
        (await RefusalOfAsync(host, new SuspendTenantSeat(seat))).Code.Should().Be(TenancyRefusals.NotSeated);
    }

    [Fact]
    public async Task A_host_that_leaves_a_module_s_access_check_out_changes_no_project_under_the_hold()
    {
        // The same host, on the demonstration, with the expert hold on Projects' context. No request's checks were
        // asked, so no request is in hand, and no project is saved, whoever sends what: here Ada, who may send all of
        // it, and Vic, who manages no crew, giving up his own place on one, which is saved as the application's work
        // only for a command its check let through.
        Type[] checks = [typeof(ProjectsAccessBehavior<,>), typeof(InspectionsAccessBehavior<,>), typeof(TenantsAccessBehavior<,>)];
        await using var host = await sample.StartAsync(services =>
        {
            RemoveAll(services, checks);
            services.HoldProjectSaves();
        });

        var pier = Harbor.ProjectNamed("Pier 7");
        var vic = Harbor.SeatOf(DemoPeople.Vic);
        var observer = Harbor.ProjectRoles[SampleCatalogue.Observer];
        (DemoPerson Sender, IMessage Request)[] changes =
        [
            (DemoPeople.Ada, new ChangeProjectName(pier.Id, "Quay wall, east")),
            (DemoPeople.Ada, new MoveProjectToUnit(pier.Id, Harbor.UnitNamed("North Inland"))),
            (DemoPeople.Ada, new CloseProject(pier.Id)),
            (DemoPeople.Ada, new AddCrewMember(pier.Id, Harbor.SeatOf(DemoPeople.Tove), Role: null, Until: null)),
            (DemoPeople.Ada, new ChangeProjectOwner(pier.Id, Harbor.SeatOf(DemoPeople.Juno))),
            (DemoPeople.Vic, new TakeCrewRole(pier.Id, vic, observer)),
            (DemoPeople.Vic, new RemoveCrewMember(pier.Id, vic)),
        ];

        foreach (var (sender, request) in changes)
        {
            using (AsSeatOf(sender))
            {
                await using var scope = host.Services.CreateAsyncScope();
                var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Cancellation);
                (await send.Should().ThrowAsync<InvalidOperationException>(request.GetType().Name)).WithMessage("*was changed with no request in hand*");
            }
        }

        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var detail = await ada.ProjectDetailAsync(pier);
        detail.GetProperty("name").GetString().Should().Be("Pier 7", "no command changed the project");
        detail.GetProperty("state").GetString().Should().Be("open");
        (await ada.CrewAsync(pier)).EnumerateArray().Single(member => member.GetProperty("seatId").GetGuid() == vic.Value)
            .GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("roleId").GetGuid())
            .Should().Equal([observer.Value], "Vic is on the crew still, with his role");
    }

    /// <summary>
    /// Takes <paramref name="behaviors"/> out of the pipeline, each of which is in it once, and turns off the start-up
    /// check that would stop a host without them, so the host starts and shows what stands past the behaviors.
    /// </summary>
    private static void RemoveAll(IServiceCollection services, Type[] behaviors)
    {
        foreach (var behavior in behaviors)
        {
            services.Remove(services.Where(descriptor => descriptor.ImplementationType == behavior).Should().ContainSingle("{0} is in the pipeline once", behavior.Name).Subject);
        }

        services.SkipStartupCheck(AccessBehaviorChecks.BehaviorsRegisteredCheck, reason: "the test shows what a handler does when nothing asks its request's checks");
    }

    [Fact]
    public async Task What_the_host_answers_is_traced_as_an_answer_and_only_a_fault_as_a_fault()
    {
        var host = await sample.SharedAsync();

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == host.Services.GetRequiredService<IHostEnvironment>().ApplicationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        // Each is what a handler, or the save under it, may throw. The first four the host answers with a status of
        // its own (RefusalProblems); a caller that went away gets no answer; the last is a bug.
        (Exception Thrown, string? Tag, ActivityStatusCode Status)[] thrown =
        [
            (ProjectRefusals.Of(ProjectRefusals.Closed), ProjectRefusals.Closed, ActivityStatusCode.Unset),
            (new ConcurrencyConflictException(typeof(Project), ProjectId.CreateSequential()), RefusalProblems.ConcurrencyConflict, ActivityStatusCode.Unset),
            (new InvalidValueObjectException(typeof(ProjectId)), RefusalProblems.InvalidValue, ActivityStatusCode.Unset),
            (new InvariantViolationException(typeof(Project), null, "A project keeps its owner on its crew."), DDDToolkit.Invariants.InvariantViolation.SeamCode, ActivityStatusCode.Unset),
            (new OperationCanceledException(), null, ActivityStatusCode.Unset),
            (new InvalidOperationException("A bug."), null, ActivityStatusCode.Error),
        ];

        using var mine = new Activity("the test's own requests").Start();
        var tracing = new RequestTracingBehavior<DeclaresNothing, Unit>(host.Services.GetRequiredService<RequestTracing>());
        foreach (var (failure, _, _) in thrown)
        {
            var handle = async () => await tracing.Handle(new DeclaresNothing(), (_, _) => throw failure, Cancellation);
            (await handle.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure, "tracing changes nothing of what is thrown");
        }

        mine.Stop();
        List<Activity> traced;
        lock (stopped)
        {
            traced = [.. stopped.Where(activity => activity.TraceId == mine.TraceId && activity.OperationName == nameof(DeclaresNothing))];
        }

        traced.Select(activity => (activity.GetTagItem(RequestTracing.RefusalTag) as string, activity.Status))
            .Should().Equal(thrown.Select(expected => (expected.Tag, expected.Status)));
    }

    [Fact]
    public async Task A_persons_own_seats_are_answered_only_to_a_token_role_that_holds_seats()
    {
        var host = await sample.SharedAsync();

        async Task<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>> OwnSeatsAsync(string claims)
        {
            using (Callers.Begin(Callers.FromClaims(claims)))
            {
                await using var scope = host.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SeatsOfMine(), Cancellation);
            }
        }

        // Rhea, signed in: her seats, found by the identity of her token.
        var rhea = DemoPeople.Rhea.Id;
        (await OwnSeatsAsync($$"""{"sub":"{{rhea}}","role":"authenticated"}""")).Should().NotBeEmpty();
        (await OwnSeatsAsync($$"""{"sub":"{{rhea}}"}""")).Should().NotBeEmpty("a token that names no role is a signed-in user's");

        // A token of hers with a role the host seats nobody under, an analyst's say: the tenant selection would
        // seat her nowhere, and the list of her seats says as little.
        (await OwnSeatsAsync($$"""{"sub":"{{rhea}}","role":"analyst"}""")).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_system_work_chooses_a_project_s_id()
    {
        await using var host = await sample.StartAsync();
        var chosen = ProjectId.CreateSequential();
        var withAnId = new OpenProject("P-900", "Quay wall", Harbor.Root, Harbor.SeatOf(DemoPeople.Ada), chosen);

        // Ada administers the tenant, and may open a project at its root. Choosing its id is still not hers: no
        // route has a field for it, so a seat that sends one is a mistake in the calling code, not a refusal.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(withAnId, Cancellation);
            (await send.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Only system work chooses a project's id*");
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id, Harbor.Administrator.Id))
        {
            await using (var before = host.Services.CreateAsyncScope())
            {
                (await before.ServiceProvider.GetRequiredService<ISender>().Send(new VisibleProjects(), Cancellation)).Items
                    .Should().NotContain(project => project.Id == chosen, "the seat's command opened nothing");
            }

            // System work, as the seeder and an import are, says which id the project gets.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                (await scope.ServiceProvider.GetRequiredService<ISender>().Send(withAnId, Cancellation)).Should().Be(chosen);
            }

            await using var after = host.Services.CreateAsyncScope();
            (await after.ServiceProvider.GetRequiredService<ISender>().Send(new VisibleProjects(), Cancellation)).Items
                .Should().Contain(project => project.Id == chosen && project.Number == "P-900");
        }
    }

    /// <summary>The refusal <paramref name="request"/> is answered with when sent from a scope of its own, as the current caller.</summary>
    private static async Task<RefusalException> RefusalOfAsync<TResponse>(SampleFactory host, IQuery<TResponse> request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Cancellation);
        return (await send.Should().ThrowAsync<RefusalException>()).Which;
    }

    /// <inheritdoc cref="RefusalOfAsync{TResponse}(SampleFactory, IQuery{TResponse})"/>
    private static async Task<RefusalException> RefusalOfAsync<TResponse>(SampleFactory host, ICommand<TResponse> request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Cancellation);
        return (await send.Should().ThrowAsync<RefusalException>()).Which;
    }

    /// <inheritdoc cref="RefusalOfAsync{TResponse}(SampleFactory, IQuery{TResponse})"/>
    private static async Task<RefusalException> RefusalOfAsync(SampleFactory host, ICommand request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Cancellation);
        return (await send.Should().ThrowAsync<RefusalException>()).Which;
    }

    /// <summary>The refusal <paramref name="request"/> is answered with when sent through the pipeline, from a scope of its own, as the current caller.</summary>
    private static async Task<RefusalException> RefusalAtTheDoorAsync(SampleFactory host, IMessage request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Cancellation);
        return (await send.Should().ThrowAsync<RefusalException>(request.GetType().Name)).Which;
    }

    /// <summary>
    /// The refusal <paramref name="request"/> is answered with when its handler is handed it directly, past the
    /// pipeline and its access behavior, as the mediator would make the handler, from a scope of its own.
    /// </summary>
    private static async Task<RefusalException> RefusalPastTheDoorAsync(SampleFactory host, IMessage request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handled = HostRegistrations.Requests.Single(each => each.Type == request.GetType());
        var handler = ActivatorUtilities.CreateInstance(scope.ServiceProvider, handled.Handler);
        var pending = handled.Handler.GetMethod("Handle", [request.GetType(), typeof(CancellationToken)])!.Invoke(handler, [request, Cancellation])!;
        var handling = (Task)pending.GetType().GetMethod(nameof(ValueTask.AsTask))!.Invoke(pending, null)!;
        var handle = async () => await handling;
        return (await handle.Should().ThrowAsync<RefusalException>(request.GetType().Name)).Which;
    }

    /// <summary>Whether harbor is marked as a demonstration, read as system work there.</summary>
    private static async Task<bool> IsDemoAsync(SampleFactory host)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TenantsContext>().Tenants
                .Where(tenant => tenant.Id == Harbor.Id)
                .Select(tenant => tenant.IsDemo)
                .SingleAsync(Cancellation);
        }
    }

    /// <summary>The handler of <see cref="ArchiveProjectRole"/> over the services of <paramref name="scope"/>, as the mediator makes it, to be called past the mediator.</summary>
    private static ArchiveProjectRoleHandler ArchiveHandlerOf(AsyncServiceScope scope)
        => new(
            scope.ServiceProvider.GetRequiredService<IProjectStore>(),
            scope.ServiceProvider.GetRequiredService<ProjectMembership>(),
            scope.ServiceProvider.GetRequiredService<AccessChecks<IProjectsRequest>>());

    /// <summary>Whether harbor's project role <paramref name="role"/> is in use, read as system work there.</summary>
    private static async Task<KeptRoleStatus> StatusOfAsync(SampleFactory host, ProjectRoleId role)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ProjectsContext>().ProjectRoles
                .Where(candidate => candidate.Id == role)
                .Select(candidate => candidate.Status)
                .SingleAsync(Cancellation);
        }
    }

    /// <summary>Begins the callers of a request of <paramref name="person"/> in harbor: the signed-in user, and their seat there.</summary>
    private static IDisposable AsSeatOf(DemoPerson person) => SampleCallers.BeginSeatOf(person, Harbor);
}
