using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;
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
                    DemoTenant.RootKind,
                    Harbor.Administrator.Person.Id,
                    Harbor.Administrator.Person.Name,
                    TenantId: Harbor.Id,
                    AdminSeatId: Harbor.Administrator.Id),
                Cancellation);
        }

        // Its administrator holds every key that manages access, and may not: no key gives this.
        using (AsSeatOf(DemoPeople.Ada))
        {
            var refused = await RefusalOfAsync(host, new MarkTenantAsDemo());
            refused.Code.Should().Be(TenancyRefusals.SystemOnly);
            refused.Kind.Should().Be(RefusalKind.NotPermitted);
        }

        (await RefusalOfAsync(host, new MarkTenantAsDemo())).Code.Should().Be(TenancyRefusals.NotSeated, "nobody is refused as nobody");

        // No use case of the package stands between this command and the tenant, so its handler asks the same
        // again: handed the command directly, past the check, it still refuses a seat.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var handler = new MarkTenantAsDemoHandler(
                scope.ServiceProvider.GetRequiredService<SampleTenancy.IStore>(),
                scope.ServiceProvider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>());

            var handle = async () => await handler.Handle(new MarkTenantAsDemo(), Cancellation);
            (await handle.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(TenancyRefusals.SystemOnly);
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
    public async Task A_project_changed_since_the_check_is_a_lost_race()
    {
        // A step registered after everything else runs after the access check and right before the handler: there
        // it changes the project, in a scope of its own, as another request arriving in between would.
        await using var host = await sample.StartAsync(
            services => services.AddScoped<IPipelineBehavior<ChangeProjectName, Unit>, RenamedMeanwhile>());
        var pier = Harbor.ProjectNamed("Pier 7");
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        using var renamed = await ada.PutAsJsonAsync($"/projects/{pier.Id.Value}/name", new { name = "Pier 7, east" }, Cancellation);

        // The handler loads the project at the version the check saw. It is no longer that version, so what was
        // checked is not what would be changed: the same answer as a save that came second.
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
    public async Task A_crew_role_taken_since_the_check_is_a_lost_race_too()
    {
        // The same step, taking away the crew role the caller was checked with. A role is a row two tables below
        // the project, and still a change to the project: its version moves on, so a caller who was a lead when
        // checked and is none when the handler runs changes nothing.
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

        await renamed.ShouldBeRefusedAsync(HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict);
        var after = await vic.ProjectDetailAsync(pier);
        after.GetProperty("name").GetString().Should().Be("Pier 7", "the request that lost the race changed nothing");
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
    public async Task A_handler_reached_past_the_mediator_has_no_project_to_load()
    {
        await using var host = await sample.StartAsync();
        var pier = Harbor.ProjectNamed("Pier 7");

        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var handler = new CloseProjectHandler(
                scope.ServiceProvider.GetRequiredService<IProjectStore>(),
                scope.ServiceProvider.GetRequiredService<Checked<MemberHold<ProjectId>>>());

            // Called directly, the command passed no access check, so there is no checked project to load: though
            // Ada may close this project, and though nothing else stands in the way.
            var handle = async () => await handler.Handle(new CloseProject(pier.Id), Cancellation);
            (await handle.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No MemberHold<ProjectId> was kept*");

            // A check counts for the request that passed it, not for one that looks the same. Renaming is sent, and
            // checked; an equal command handed to the handler directly was not.
            var sent = new ChangeProjectName(pier.Id, "Pier 7, east");
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(sent, Cancellation);
            var rename = new ChangeProjectNameHandler(
                scope.ServiceProvider.GetRequiredService<IProjectStore>(),
                scope.ServiceProvider.GetRequiredService<Checked<MemberHold<ProjectId>>>());
            var again = async () => await rename.Handle(sent with { }, Cancellation);
            (await again.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No MemberHold<ProjectId> was kept*");

            // Nor does it count twice. The command that was sent was handled, once; handed to the handler again,
            // the very same command has no checked project left to load.
            var twice = async () => await rename.Handle(sent, Cancellation);
            (await twice.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No MemberHold<ProjectId> was kept*");
        }

        // Opening a project is asked at a unit, with no project to check yet, and is held the same way. Vic is an
        // observer on Pier 7's crew and holds no key at any unit: handed to the handler directly, his command passed
        // no check, so there is no unit to open a project at, though the unit is there and the number is free.
        using (AsSeatOf(DemoPeople.Vic))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var open = new OpenProjectHandler(
                scope.ServiceProvider.GetRequiredService<IProjectStore>(),
                scope.ServiceProvider.GetRequiredService<IProjectReads>(),
                scope.ServiceProvider.GetRequiredService<ProjectAccess>(),
                scope.ServiceProvider.GetRequiredService<ProjectTenancy>(),
                scope.ServiceProvider.GetRequiredService<MemberAdmission<ProjectId, SeatId, ProjectRoleId>>(),
                scope.ServiceProvider.GetRequiredService<Checked<OrganizationUnitId>>(),
                scope.ServiceProvider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            var opening = async () => await open.Handle(new OpenProject("P-901", "Past the check", Harbor.Root), Cancellation);
            (await opening.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No OrganizationUnitId was kept*");

            // Sent, the same command is checked, and refused for the key he does not hold there.
            (await RefusalOfAsync(host, new OpenProject("P-901", "Past the check", Harbor.Root))).Code.Should().Be(ProjectRefusals.NotPermitted);
        }

        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var detail = await ada.ProjectDetailAsync(pier);
        detail.GetProperty("state").GetString().Should().Be("open", "the handler that was called directly closed nothing");
        detail.GetProperty("name").GetString().Should().Be("Pier 7, east");
        (await ada.VisibleProjectsAsync()).Names().Should().NotContain("Past the check", "and the one that was called directly opened nothing");
    }

    [Fact]
    public async Task A_handler_of_inspections_reached_past_the_mediator_has_no_project_to_act_on()
    {
        var host = await sample.SharedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        // Juno is Pier 7's surveyor: she sees it, and may record on it.
        using (AsSeatOf(DemoPeople.Juno))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var gated = scope.ServiceProvider.GetRequiredService<Checked<GatedProject>>();

            // Called directly, the command asked Projects' gate nothing, so there is no project to record on:
            // though she may record on this one, and though the title is fine.
            var record = new RecordInspectionHandler(
                scope.ServiceProvider.GetRequiredService<IInspectionStore>(),
                gated,
                scope.ServiceProvider.GetRequiredService<ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>());
            var recording = async () => await record.Handle(new RecordInspection(pier, "Loose railing"), Cancellation);
            (await recording.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No GatedProject was kept*");

            // The same for the query: nothing is listed for a request the gate was not asked about.
            var list = new ProjectInspectionsHandler(
                scope.ServiceProvider.GetRequiredService<IInspectionReads>(),
                gated,
                scope.ServiceProvider.GetRequiredService<IProjectGate>());
            var listing = async () => await list.Handle(new ProjectInspections(pier), Cancellation);
            (await listing.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No GatedProject was kept*");

            // A check counts for the request that passed it, once. The query that was sent was answered; an equal
            // one, and the very same one a second time, are not.
            var sent = new ProjectInspections(pier);
            (await sender.Send(sent, Cancellation)).Items.Should().BeEmpty("the handler that was called directly recorded nothing");
            var equal = async () => await list.Handle(sent with { }, Cancellation);
            (await equal.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No GatedProject was kept*");
            var twice = async () => await list.Handle(sent, Cancellation);
            (await twice.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No GatedProject was kept*");

            // Sent twice at once, the one query passes the check twice and is answered twice; and then nothing is
            // left of it for a handler called directly.
            var atOnce = await Task.WhenAll(sender.Send(sent, Cancellation).AsTask(), sender.Send(sent, Cancellation).AsTask());
            atOnce.Should().HaveCount(2).And.OnlyContain(answered => answered.Items.Count == 0);
            (await twice.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No GatedProject was kept*");
        }
    }

    [Fact]
    public async Task A_host_that_leaves_a_module_s_access_check_out_runs_no_handler_unchecked()
    {
        // What a program gets that registers a module's handlers and leaves its access behavior out of the pipeline.
        // Only the behaviors are taken out here: the checks, and what a check keeps for the handlers, are still
        // registered, so every handler can be made, and the host starts. Nothing is seeded, since the seeder's own
        // commands would be stopped the same way, and a handler that acted unchecked would need no data to show it.
        Type[] checks = [typeof(ProjectsAccessBehavior<,>), typeof(InspectionsAccessBehavior<,>), typeof(TenantsAccessBehavior<,>)];
        await using var host = await sample.StartAsync(
            seeded: false,
            settings: new Dictionary<string, string> { [DemoSeeder.Setting] = "false" },
            services: services =>
            {
                foreach (var check in checks)
                {
                    services.Remove(services.Where(descriptor => descriptor.ImplementationType == check).Should().ContainSingle("{0} is in the pipeline once", check.Name).Subject);
                }
            });

        var project = ProjectId.CreateSequential();
        var seat = SeatId.CreateSequential();
        var role = ProjectRoleId.CreateSequential();
        var unit = OrganizationUnitId.CreateSequential();

        // Every request of Projects and of Inspections that acts on a project, or at a unit: its handler takes what
        // the check kept for the request, finds nothing, and stops. A seat that could do all of it changes nothing.
        (IMessage Request, string Stopped)[] kept =
        [
            (new OpenProject("P-900", "Quay wall", unit), "No OrganizationUnitId was kept"),
            (new ChangeProjectName(project, "Quay wall, east"), "No MemberHold<ProjectId> was kept"),
            (new PlanProject(project, Planned: null), "No MemberHold<ProjectId> was kept"),
            (new MoveProjectToUnit(project, unit), "No MemberHold<ProjectId> was kept"),
            (new CloseProject(project), "No MemberHold<ProjectId> was kept"),
            (new ReopenProject(project), "No MemberHold<ProjectId> was kept"),
            (new AddCrewMember(project, seat, role, Until: null), "No MemberHold<ProjectId> was kept"),
            (new GiveCrewRole(project, seat, role, Until: null), "No MemberHold<ProjectId> was kept"),
            (new TakeCrewRole(project, seat, role), "No MemberHold<ProjectId> was kept"),
            (new RemoveCrewMember(project, seat), "No MemberHold<ProjectId> was kept"),
            (new ChangeProjectOwner(project, seat), "No MemberHold<ProjectId> was kept"),
            (new ProjectInspections(project), "No GatedProject was kept"),
            (new RecordInspection(project, "Loose railing"), "No GatedProject was kept"),
        ];

        using (AsSeatOf(DemoPeople.Ada))
        {
            foreach (var (request, stopped) in kept)
            {
                await using var scope = host.Services.CreateAsyncScope();
                var send = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Cancellation);
                (await send.Should().ThrowAsync<InvalidOperationException>(request.GetType().Name)).WithMessage($"*{stopped}*");
            }

            // Tenancy's check keeps nothing for its handlers, and needs to keep nothing. Every command of the
            // package's goes to a use case of the package, which asks who is calling whoever calls it; the commands
            // that are the modules' own, marking a tenant as a demonstration and those of the project roles, each
            // ask again what they declared. So the seat is refused before the handler looks for the role, which is
            // not there.
            (await RefusalOfAsync(host, new MarkTenantAsDemo())).Code.Should().Be(TenancyRefusals.SystemOnly);
            (await RefusalOfAsync(host, new SetUpProjectRoles())).Code.Should().Be(TenancyRefusals.SystemOnly);
            (await RefusalOfAsync(host, new MakeProjectRole("Rigger", null, []))).Code.Should().Be(TenancyRefusals.NotPermitted);
            (await RefusalOfAsync(host, new RenameProjectRole(role, "Rigger", null))).Code.Should().Be(TenancyRefusals.NotPermitted);
            (await RefusalOfAsync(host, new SetProjectRoleKeys(role, []))).Code.Should().Be(TenancyRefusals.NotPermitted);
            (await RefusalOfAsync(host, new ArchiveProjectRole(role))).Code.Should().Be(TenancyRefusals.NotPermitted);
        }

        // As nobody, a command of the package's is refused by the package, as it is behind the check.
        (await RefusalOfAsync(host, new SuspendTenantSeat(seat))).Code.Should().Be(TenancyRefusals.NotSeated);
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
