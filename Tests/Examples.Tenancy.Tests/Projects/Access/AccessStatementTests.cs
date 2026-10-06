using DDDToolkit.Exceptions;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using GreenDonut.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// What the database is sent when a module asks who may see what, through the application's read port. The
/// application asks Tenancy's set-shaped questions without knowing how anything is stored, and hands the answers
/// to the port as sets not yet read; the port must still send them with its own query as one statement, with
/// Tenancy's answer a subquery, rather than fetch the answer first and pass it back as a list.
/// </summary>
/// <remarks>
/// A read takes a context of its own from the module's factory, which a test cannot name beforehand. So what is
/// counted is what the test's own flow of work sends, on whichever context: the read under test, and nothing the
/// outbox pollers or another test sends meanwhile on flows of their own.
/// <para>
/// The statements are the ones Postgres is sent. A module reads no table of Tenancy's there: what a seat holds
/// in the organization is what Tenancy's function answers (<see cref="CallerRights"/>), named inside the
/// module's own statement like a table, and the units, seats and roles are functions of Tenancy's likewise.
/// Tenancy's own context reads its own tables. What row level security sets on a connection before a statement
/// runs is not a statement of the context's, and is not counted.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AccessStatementTests(SampleHosts hosts) : IClassFixture<SampleHosts>
{
    /// <summary>
    /// Where a module's statement reads what the caller's seat holds in the organization: the function Tenancy
    /// gives another module to ask, in place of its table of rights.
    /// </summary>
    private const string CallerRights = TenantsContext.Schema + ".caller_rights()";

    /// <summary>
    /// How a statement names a table of Tenancy's own, any of them: the schema and the quote a table's name
    /// begins with. A function of Tenancy's is the schema and a name without one.
    /// </summary>
    private const string AnyTableOfTenancy = TenantsContext.Schema + ".\"";

    /// <summary>The projects' table as a statement names it, in the module's schema.</summary>
    private const string ProjectsTable = $"{ProjectsContext.Schema}.\"{ProjectsContext.ProjectsTable}\"";

    /// <summary>The crew members' table as a statement names it.</summary>
    private const string CrewTable = $"{ProjectsContext.Schema}.\"{ProjectsContext.CrewTable}\"";

    /// <summary>The table of the roles held on a crew, as a statement names it.</summary>
    private const string CrewRolesTable = $"{ProjectsContext.Schema}.\"{ProjectsContext.CrewRolesTable}\"";

    /// <summary>The table of the tenants' project roles, as a statement names it.</summary>
    private const string ProjectRolesTable = $"{ProjectsContext.Schema}.\"{ProjectsContext.ProjectRolesTable}\"";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task The_projects_a_seat_may_see_are_one_statement_through_the_read_port()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);

        // Rhea reaches Pier 7 through her area, North, and Inland depot through its crew, which she leads.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        var listed = await rhea.VisibleProjectsAsync();
        listed.Named("Pier 7").Text("via").Should().Be("organization");
        listed.Named("Inland depot").Text("via").Should().Be("crew");

        using (AsSeatOf(DemoPeople.Rhea))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            var reads = scope.ServiceProvider.GetRequiredService<IProjectReads>();
            var access = scope.ServiceProvider.GetRequiredService<ProjectAccess>();

            // What the application does: it asks the projects' rules for the reach of the key, which says what is to
            // be asked and asks nothing yet, and hands that to the port.
            counter.WatchThisFlow();
            await using var reading = reads.Open();
            var reach = access.ReachFor(ProjectKeys.View);
            counter.Commands.Should().BeEmpty("a reach is made of questions that have not been asked of the database yet");

            var mine = await reading.PageAsync(reach, ProjectListFilter.None, new PagingArguments(first: VisibleProjects.LargestPage), Cancellation);

            // One statement: the units where she holds the key, from Tenancy's rights, and the crews she is on,
            // are subqueries of the project statement. Of a crew it asks only whether she is on it: a list reads
            // no crew until somebody asks for one.
            var statement = counter.Commands.Should().ContainSingle("both ways in are asked inside the project statement").Which;
            statement.Should().Contain(CallerRights, "UnitsWhereIHold reads Tenancy's rights in the same statement");
            statement.Should().NotContain(AnyTableOfTenancy, "and reads them from Tenancy's function: no table of Tenancy's is named");
            statement.Should().Contain(CrewTable, "whether she is on the crew is asked in the same statement");
            statement.Should().NotContain(CrewRolesTable, "seeing a project takes no role, and no crew is read with the list");
            mine.Items.Select(project => project.Name).Should().Equal(listed.Names(), "the list the API answers with is this statement's");
            mine.Items.Select(project => project.Via).Should().Equal([ProjectVia.Organization, ProjectVia.Crew], "and how each was reached came with it");
        }
    }

    [Fact]
    public async Task Listing_is_one_statement_whoever_asks()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);

        // A seat. Ada administers the tenant from its root, and owns Bay bridge and HQ refit, so she leads their
        // crews: how she reaches each project comes back with it, the crew first.
        using (AsSeatOf(DemoPeople.Ada))
        {
            var (reach, projects) = await ListedAsync(sample, counter);

            counter.Commands.Should().ContainSingle("every project she sees, and how, in the one statement");
            reach.Everything.Should().BeFalse();
            projects.ToDictionary(project => project.Id, project => project.Via).Should().BeEquivalentTo(new Dictionary<ProjectId, ProjectVia?>
            {
                [Harbor.ProjectNamed("Pier 7").Id] = ProjectVia.Organization,
                [Harbor.ProjectNamed("Inland depot").Id] = ProjectVia.Organization,
                [Harbor.ProjectNamed("Bay bridge").Id] = ProjectVia.Crew,
                [Harbor.ProjectNamed("HQ refit").Id] = ProjectVia.Crew,
            });
        }

        // Juno is a surveyor on one crew and holds nothing in the organization: the same statement, one project.
        using (AsSeatOf(DemoPeople.Juno))
        {
            var (reach, projects) = await ListedAsync(sample, counter);

            counter.Commands.Should().ContainSingle();
            projects.Should().ContainSingle().Which.Should().Match<ProjectOverview>(project => project.Name == "Pier 7" && project.Via == ProjectVia.Crew);
        }

        // System work in the tenant reaches all of it, by neither way: the filter is left out of the statement,
        // and the tenant filter keeps it to that tenant.
        using (TenantsTenancy.BeginSystemIn(Harbor.Id))
        {
            var (reach, projects) = await ListedAsync(sample, counter);

            var statement = counter.Commands.Should().ContainSingle().Which;
            statement.Should().NotContain(CallerRights, "system work is asked for no key");
            reach.Everything.Should().BeTrue();
            projects.Select(project => project.Id).Should().BeEquivalentTo(Harbor.Projects.Select(project => project.Id));
            projects.Should().OnlyContain(project => project.Via == ProjectVia.System);
        }
    }

    [Fact]
    public async Task A_key_on_a_project_is_checked_in_one_statement()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);
        var depot = Harbor.ProjectNamed("Inland depot").Id;
        var bridge = Harbor.ProjectNamed("Bay bridge").Id;

        // A key a crew gives. Rhea leads Inland depot's crew, and holds nothing at its unit that lets her edit.
        using (AsSeatOf(DemoPeople.Rhea))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            counter.WatchThisFlow();

            var hold = await scope.ServiceProvider.GetRequiredService<IMemberQuestions<ProjectId>>().RequireAsync(depot, ProjectKeys.Edit, Cancellation);

            // Whether she sees it, how she holds the key on it, and its version, in the one statement: her
            // membership, and in it a role that gives the key.
            var statement = counter.Commands.Should().ContainSingle("the check a command passes is one statement").Which;
            statement.Should().Contain(CallerRights).And.Contain(CrewTable).And.Contain(CrewRolesTable);
            hold.Should().Match<MemberHold<ProjectId>>(held => held.Via == MemberVia.Members && held.Version > 0);

            // And the role's own period is compared where the statement runs, as the membership's is: the columns of
            // the alias the roles' table has there, not rows filtered after they were read.
            statement.Should().MatchRegex(
                $$"""FROM {{ProjectsContext.Schema}}\."{{ProjectsContext.CrewRolesTable}}" AS (?<role>\w+)[\s\S]*?\k<role>\."StartsAt" <= @\w+ AND \(\k<role>\."EndsAt" IS NULL OR \k<role>\."EndsAt" > @\w+\)""");
        }

        // A key no crew gives. Ada leads Bay bridge's crew, and still holds the owner's key through the organization alone.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            counter.WatchThisFlow();

            var hold = await scope.ServiceProvider.GetRequiredService<IMemberQuestions<ProjectId>>().RequireAsync(bridge, ProjectKeys.ChangeOwner, Cancellation);

            counter.Commands.Should().ContainSingle();
            hold.Via.Should().Be(MemberVia.Above, "naming an owner never comes through a crew");
        }

        // The gate another module asks is the same check: one statement, a project out of reach included.
        using (AsSeatOf(DemoPeople.Juno))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            var gate = scope.ServiceProvider.GetRequiredService<IProjectGate>();

            // Seeing a project asks whether she is on its crew, not what she holds there: no role is read.
            counter.WatchThisFlow();
            (await gate.AskAsync(bridge, ProjectKeys.View, Cancellation)).Visible.Should().BeFalse();
            counter.Commands.Should().ContainSingle()
                .Which.Should().Contain(CrewTable).And.NotContain(CrewRolesTable);

            counter.WatchThisFlow();
            (await gate.AskAsync(Harbor.ProjectNamed("Pier 7").Id, ProjectKeys.Edit, Cancellation)).Should().Be(new ProjectAnswer(Visible: true, Allowed: false, Closed: false));
            counter.Commands.Should().ContainSingle();
        }

        // System work in the tenant: one statement too, and it holds every key by neither way.
        using (TenantsTenancy.BeginSystemIn(Harbor.Id))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            counter.WatchThisFlow();

            var hold = await scope.ServiceProvider.GetRequiredService<IMemberQuestions<ProjectId>>().RequireAsync(depot, ProjectKeys.ChangeOwner, Cancellation);

            counter.Commands.Should().ContainSingle().Which.Should().NotContain(CallerRights);
            hold.Via.Should().Be(MemberVia.System);
        }
    }

    [Fact]
    public async Task A_command_asks_tenancy_on_one_reading_and_saves_on_the_request_s_context()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);

        // Ada opens a project at the root and names Leo as its owner, which is the most a command of Projects asks
        // before it changes anything: the owner's key at the unit, the unit, the seat and the lead role, which is a
        // project role, a row of the module's own.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            var ofTheScope = scope.ServiceProvider.GetRequiredService<ProjectsContext>();

            counter.WatchThisFlow();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new OpenProject("P-900", "Quay wall", Harbor.Root, Harbor.SeatOf(DemoPeople.Leo)),
                Cancellation);

            // Three contexts: the access check's reading, one reading for everything the handler asks of Tenancy,
            // and the request's own, the unit of work, for the number and the save. Each is taken from the pool, so
            // they are counted by rental: the handler's reading may well be handed the instance the check gave back.
            counter.Rentals.Should().HaveCount(3, "the check reads on one context, the handler asks on one more, and the command saves on the request's");
            counter.Rentals[^1].Should().Be(ofTheScope.ContextId, "what changes is saved on the request's context, last");
            counter.Rentals.Take(2).Should().NotContain(ofTheScope.ContextId, "and nothing of Tenancy is read on it");
            counter.Commands.Take(4).Should().AllSatisfy(
                statement => statement.Should().StartWith("SELECT").And.Contain($"FROM {TenantsContext.Schema}.").And.NotContain(AnyTableOfTenancy).And.NotContain($"FROM {ProjectsTable}"),
                "the four questions of Tenancy's come first, each asked of a function of Tenancy's: the key to open, the owner's key, the unit and the seat");
            counter.Commands[4].Should().Contain($"FROM {ProjectRolesTable}").And.NotContain($"{TenantsContext.Schema}.", "then the lead role, of the module's own table");
            counter.Commands.Skip(5).Should().AllSatisfy(
                statement => statement.Should().Contain(ProjectsTable).And.NotContain($"{TenantsContext.Schema}."),
                "and then the module's own: whether the number is free, and the save");
        }
    }

    [Fact]
    public async Task Giving_a_crew_role_asks_the_tenancy_module_nothing_and_reads_the_role_on_the_request_s_context()
    {
        var projects = new CommandCounter();
        var tenancy = new CommandCounter();
        await using var sample = await hosts.StartAsync(services => services
            .ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(projects))
            .ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(tenancy)));

        // Leo gives Vic the surveyor's project role on Pier 7. The role is a row of this module's own, so whether it
        // is one a crew member may hold is asked of this module's table, and nothing of the Tenants module's.
        using (AsSeatOf(DemoPeople.Leo))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            var ofTheScope = scope.ServiceProvider.GetRequiredService<ProjectsContext>();

            projects.WatchThisFlow();
            tenancy.WatchThisFlow();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new GiveCrewRole(Harbor.ProjectNamed("Pier 7").Id, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[SampleCatalogue.Surveyor], Until: null),
                Cancellation);

            tenancy.Commands.Should().BeEmpty("a crew's roles are the module's own");

            // Projects' own: the access check on a reading, then the load, the role and the save on the request's
            // context. Both are taken from the pool, so they are counted by rental: a second reading would be handed
            // the instance the first gave back, and would not show as another context.
            projects.Rentals.Should().HaveCount(2, "the check reads on one context, and the command loads, asks about the role and saves on the request's");
            projects.Rentals[^1].Should().Be(ofTheScope.ContextId);
            projects.Commands.Should().ContainSingle(
                    statement => statement.Contains($"FROM {ProjectRolesTable}", StringComparison.Ordinal) && !statement.Contains(CrewTable, StringComparison.Ordinal),
                    "the role is asked about once, in the module's own table, beside the check that read the keys of the crew's roles")
                .Which.Should().NotContain(AnyTableOfTenancy, "and nothing of Tenancy's: no table of the Tenants module is named, whichever context asks");
            projects.Commands.Should().ContainSingle(statement => statement.StartsWith($"INSERT INTO {CrewRolesTable}", StringComparison.Ordinal), "the role is one row of its own");
        }
    }

    [Fact]
    public async Task Sending_VisibleProjects_costs_one_statement_however_many_projects_there_are()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);

        // The page with how each project is reached, and nothing after it: the crews and what the caller may do
        // are queries of their own, sent by whoever shows them. The declared key is the statement's filter, so no
        // check reads before the handler either.
        foreach (var (person, sees) in new[] { (DemoPeople.Ada, Harbor.Projects.Count), (DemoPeople.Juno, 1) })
        {
            using (AsSeatOf(person))
            {
                await using var scope = sample.Services.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                counter.WatchThisFlow();
                var projects = await sender.Send(new VisibleProjects(), Cancellation);

                projects.Items.Should().HaveCount(sees);
                counter.Commands.Should().ContainSingle("{0} sees {1}, and the number of statements does not follow the number of projects", person.Name, sees);
            }
        }
    }

    [Fact]
    public async Task The_crews_and_the_abilities_of_a_page_cost_one_statement_each()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);
        ProjectId[] all = [.. Harbor.Projects.Select(project => project.Id)];
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        // Leo leads Pier 7's crew and sees nothing else of harbor; Ada administers the tenant. Asked about every
        // project of harbor, each is answered about the ones it sees, in one statement, whatever their number.
        foreach (var (person, sees) in new[] { (DemoPeople.Leo, 1), (DemoPeople.Ada, all.Length) })
        {
            using (AsSeatOf(person))
            {
                await using var scope = sample.Services.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                counter.WatchThisFlow();
                var crews = await sender.Send(new CrewsOfProjects(all), Cancellation);

                var statement = counter.Commands.Should().ContainSingle("{0} is answered the crews by the statement that decides which projects she sees", person.Name).Which;
                statement.Should().Contain(CallerRights).And.Contain(CrewTable).And.Contain(CrewRolesTable);
                crews.Should().HaveCount(sees).And.ContainKey(pier);
                crews[pier].Members.Select(member => member.SeatId).Should().Equal(
                    [Harbor.SeatOf(DemoPeople.Leo), Harbor.SeatOf(DemoPeople.Juno), Harbor.SeatOf(DemoPeople.Vic)],
                    "the owner first, then by when each was put on the crew");
                crews[pier].Members.Should().OnlyContain(member => member.ProjectId == pier, "a member says whose crew it is on");
                crews[pier].MyRoleIds.Should().Equal(person == DemoPeople.Leo ? [Harbor.ProjectRoles[SampleCatalogue.CrewLead]] : [], "the caller's own roles on the crew come with it");

                // Every key an ability stands for, each project's state and whether there is anywhere to move one
                // to: one statement. Leo may not open a project anywhere, so he may not move one.
                counter.WatchThisFlow();
                var abilities = await sender.Send(new AbilitiesOnProjects(all), Cancellation);

                counter.Commands.Should().ContainSingle("{0} is answered what she may do by one statement", person.Name);
                abilities.Should().HaveCount(sees);
                abilities[pier].Should().Be(person == DemoPeople.Leo
                    ? new ProjectAbilities(Rename: true, Plan: true, Move: false, Close: true, Reopen: false, ManageCrew: true, ChangeOwner: false)
                    : new ProjectAbilities(Rename: true, Plan: true, Move: true, Close: true, Reopen: false, ManageCrew: true, ChangeOwner: true));

                // And no projects at all cost nothing.
                counter.WatchThisFlow();
                (await sender.Send(new CrewsOfProjects([]), Cancellation)).Should().BeEmpty();
                (await sender.Send(new AbilitiesOnProjects([]), Cancellation)).Should().BeEmpty();
                (await sender.Send(new ProjectsById([]), Cancellation)).Should().BeEmpty();
                counter.Commands.Should().BeEmpty();
            }
        }

        // Vic only looks at Pier 7: he holds no key an ability stands for, and is answered about it all the same.
        using (AsSeatOf(DemoPeople.Vic))
        {
            await using var scope = sample.Services.CreateAsyncScope();

            (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AbilitiesOnProjects(all), Cancellation))
                .Should().ContainSingle().Which.Should().Be(KeyValuePair.Create(pier, new ProjectAbilities(false, false, false, false, false, false, false)));
        }

        // More than a question may be about is refused, for each of the three alike.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            ProjectId[] tooMany = [.. Enumerable.Range(0, ProjectsById.MostProjects + 1).Select(_ => ProjectId.CreateSequential())];

            foreach (var asking in new Func<Task>[]
            {
                async () => await sender.Send(new ProjectsById(tooMany), Cancellation),
                async () => await sender.Send(new CrewsOfProjects(tooMany), Cancellation),
                async () => await sender.Send(new AbilitiesOnProjects(tooMany), Cancellation),
            })
            {
                (await asking.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ProjectRefusals.TooManyIds);
            }
        }
    }

    [Fact]
    public async Task Sending_AllCrewMembers_costs_one_statement_whoever_asks()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);
        var pier = Harbor.ProjectNamed("Pier 7");

        // The query declares the key it is seen with, so nothing is read before its handler: the one statement is
        // the project within the caller's reach, with its crew and each member's roles. Juno is on the crew, Rhea
        // reaches it through her area, and Hana reaches no project at all.
        foreach (var person in new[] { DemoPeople.Juno, DemoPeople.Rhea })
        {
            using (AsSeatOf(person))
            {
                await using var scope = sample.Services.CreateAsyncScope();

                counter.WatchThisFlow();
                var crew = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AllCrewMembers(pier.Id), Cancellation);

                var statement = counter.Commands.Should().ContainSingle("{0} is answered the crew by the statement that decides whether she sees the project", person.Name).Which;
                statement.Should().Contain(CallerRights).And.Contain(CrewTable).And.Contain(CrewRolesTable);
                counter.Rentals.Should().ContainSingle("the query reads on one context, and no check read before it");
                crew.Select(member => member.SeatId).Should().Equal(
                    [Harbor.SeatOf(DemoPeople.Leo), Harbor.SeatOf(DemoPeople.Juno), Harbor.SeatOf(DemoPeople.Vic)],
                    "the owner first, then by when each was put on the crew");
            }
        }

        using (AsSeatOf(DemoPeople.Hana))
        {
            await using var scope = sample.Services.CreateAsyncScope();

            counter.WatchThisFlow();
            var asking = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(new AllCrewMembers(pier.Id), Cancellation);

            (await asking.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ProjectRefusals.NotFound);
            counter.Commands.Should().ContainSingle("that the project is out of reach is that same statement's answer");
        }
    }

    [Fact]
    public async Task Sending_ProjectDetail_costs_two_statements_whatever_the_project()
    {
        var counter = new CommandCounter();
        await using var sample = await CountedAsync(counter);

        // The access check, and the project with how it is reached. No name is read: the answer carries ids.
        using (AsSeatOf(DemoPeople.Ada))
        {
            foreach (var project in Harbor.Projects)
            {
                await using var scope = sample.Services.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                counter.WatchThisFlow();
                var detail = await sender.Send(new ProjectDetail(project.Id), Cancellation);

                detail.Should().Match<ProjectOverview>(answered => answered.Id == project.Id && answered.Name == project.Name && answered.Via != null);
                counter.Commands.Should().HaveCount(2, "{0} costs what every project costs", project.Name);
            }
        }
    }

    [Fact]
    public async Task Sending_ProjectInspections_costs_three_statements_however_many_inspections_there_are()
    {
        var counter = new CommandCounter();
        await using var sample = await hosts.StartAsync(services =>
        {
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter));
            services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(counter));
        });
        var pier = Harbor.ProjectNamed("Pier 7").Id;

        // Juno is Pier 7's surveyor: she sees it and records on it through the crew.
        using (AsSeatOf(DemoPeople.Juno))
        {
            for (var recorded = 0; recorded <= 2; recorded++)
            {
                await using var scope = sample.Services.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                counter.WatchThisFlow();
                var list = await sender.Send(new ProjectInspections(pier), Cancellation);
                var statements = counter.Commands;

                list.Items.Should().HaveCount(recorded);
                list.CanRecord.Should().BeTrue();
                statements.Should().HaveCount(3, "the access check, whether she may record, and the list, with {0} recorded", recorded);

                // Projects' gate answers each question in one statement of Projects' own, with Tenancy's answer
                // inside it: once for the access check, which asks whether she sees the project, and once for the
                // button, which asks whether she may record on it.
                statements.Take(2).Should().AllSatisfy(statement => statement.Should()
                    .Contain(ProjectsTable).And.Contain(CallerRights).And.Contain(CrewTable));

                // The list is one statement that reads inspections and nothing of Tenancy's or Projects'.
                statements[2].Should().Contain($"{InspectionsContext.Schema}.\"{InspectionsContext.InspectionsTable}\"")
                    .And.NotContain($"{TenantsContext.Schema}.").And.NotContain($"{ProjectsContext.Schema}.");

                await sender.Send(new RecordInspection(pier, $"Finding {recorded + 1}"), Cancellation);
            }
        }
    }

    [Fact]
    public async Task A_directory_question_costs_the_same_statements_however_many_ids()
    {
        var counter = new CommandCounter();
        await using var sample = await hosts.StartAsync(services => services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(counter)));

        // Juno holds no role in the organization: whoever works in the tenant reads its names.
        using (AsSeatOf(DemoPeople.Juno))
        {
            foreach (var padding in new[] { 0, TenantsTenancy.TenancyDirectory.MostIds - Harbor.Seats.Count - 1 })
            {
                await using var scope = sample.Services.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                // The seats: one statement, with the ids in it, however many are asked about.
                SeatId[] seats = [.. Harbor.Seats.Select(seat => seat.Id), Harbor.Administrator.Id, .. Enumerable.Range(0, padding).Select(_ => SeatId.CreateSequential())];
                counter.WatchThisFlow();
                (await sender.Send(new SeatsById(seats), Cancellation)).Should().HaveCount(Harbor.Seats.Count + 1);
                counter.Commands.Should().ContainSingle("{0} seats asked about", seats.Length)
                    .Which.Should().Contain($"{AnyTableOfTenancy}Seats\"").And.NotContain("\"Identity\"", "the names are read from the seats' own table, and the identity is not selected");

                // The roles: two statements, the tenant's roles, from which the ones asked for are picked, and
                // whether the caller holds the key a role's keys are answered to.
                RoleId[] roles = [.. Harbor.Roles.Values, .. Enumerable.Range(0, padding).Select(_ => RoleId.CreateSequential())];
                counter.WatchThisFlow();
                (await sender.Send(new RolesById(roles), Cancellation)).Should().HaveCount(Harbor.Roles.Count);
                counter.Commands.Should().HaveCount(2, "{0} roles asked about", roles.Length);

                // The units: two statements, the organization with its units and the closure that orders a path. The
                // kind, the application's own field, comes with the units the directory read: no statement more.
                OrganizationUnitId[] units = [Harbor.Root, .. Harbor.Units.Select(unit => unit.Id), .. Enumerable.Range(0, padding).Select(_ => OrganizationUnitId.CreateSequential())];
                counter.WatchThisFlow();
                (await sender.Send(new OrganizationUnitsById(units), Cancellation)).Should().HaveCount(Harbor.Units.Count + 1)
                    .And.OnlyContain(unit => unit.Kind != null, "every unit of the demonstration tenant is seeded with its kind");
                counter.Commands.Should().HaveCount(2, "{0} units asked about", units.Length);

                // And no ids at all cost nothing.
                counter.WatchThisFlow();
                (await sender.Send(new SeatsById([]), Cancellation)).Should().BeEmpty();
                (await sender.Send(new RolesById([]), Cancellation)).Should().BeEmpty();
                (await sender.Send(new OrganizationUnitsById([]), Cancellation)).Should().BeEmpty();
                counter.Commands.Should().BeEmpty();
            }
        }
    }

    /// <summary>The projects the current caller reaches with <see cref="ProjectKeys.View"/>, read through the port, counted.</summary>
    private static async Task<(MemberReach<ProjectId> Reach, IReadOnlyList<ProjectOverview> Projects)> ListedAsync(SampleFactory sample, CommandCounter counter)
    {
        await using var scope = sample.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<ProjectAccess>();

        counter.WatchThisFlow();
        await using var reading = scope.ServiceProvider.GetRequiredService<IProjectReads>().Open();
        var reach = access.ReachFor(ProjectKeys.View);
        return (reach, [.. (await reading.PageAsync(reach, ProjectListFilter.None, new PagingArguments(first: VisibleProjects.LargestPage), Cancellation)).Items]);
    }

    [Fact]
    public async Task Where_a_seat_holds_a_key_is_one_statement_through_the_read_port()
    {
        var counter = new CommandCounter();
        await using var sample = await hosts.StartAsync(services => services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(counter)));

        // Rhea manages the units of her area, North, and so of the two units below it; not the whole tenant.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        var answered = await rhea.GetFromJsonAsync<JsonElement>("/access/units?key=" + TenancyKeys.UnitsManage, Cancellation);

        using (AsSeatOf(DemoPeople.Rhea))
        {
            await using var scope = sample.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            counter.WatchThisFlow();
            var held = await sender.Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation);

            // One statement, on a context the query took from the factory: the units where she holds the key, from
            // Tenancy's rights, are a subquery of the query that reads the tenant's units. This is Tenancy's own
            // context, so both are its own tables.
            var statement = counter.Commands.Should().ContainSingle("where the key is held is asked inside the query that reads the units").Which;
            statement.Should().Contain($"{AnyTableOfTenancy}SeatRights\"", "UnitsWhereIHold reads Tenancy's rights in the same statement");
            statement.Should().Contain($"{AnyTableOfTenancy}OrganizationUnits\"", "the units are read in the same statement");

            held.WholeTenant.Should().BeFalse();
            statement.Should().NotContain("\"Name\"", "no name is read: the answer is ids");
            held.Units.Should().BeEquivalentTo([Harbor.UnitNamed("North"), Harbor.UnitNamed("North Coast"), Harbor.UnitNamed("North Inland")]);
            held.Units.Select(unit => unit.Value).Should().BeInAscendingOrder("the ids come ordered by their value, so the answer is the same every time");
            held.Units.Select(unit => unit.Value).Should().Equal(
                answered.GetProperty("unitsWhereIHold").EnumerateArray().Select(unit => unit.GetGuid()),
                "the route answers with this query's answer");
            answered.GetProperty("wholeTenant").GetBoolean().Should().BeFalse();
        }

        // Ada administers the tenant from its root: every unit, and the whole tenant, in the one statement too.
        using (AsSeatOf(DemoPeople.Ada))
        {
            await using var scope = sample.Services.CreateAsyncScope();

            counter.WatchThisFlow();
            var held = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new UnitsWhereIHold(TenancyKeys.UnitsManage), Cancellation);

            counter.Commands.Should().ContainSingle();
            held.WholeTenant.Should().BeTrue("she holds the key at the root");
            held.Units.Should().HaveCount(Harbor.Units.Count + 1, "the root and every unit below it");
        }
    }

    /// <summary>A host of the test's own, started on the demonstration, with <paramref name="counter"/> on every <see cref="ProjectsContext"/> it makes.</summary>
    private Task<SampleFactory> CountedAsync(CommandCounter counter)
        => hosts.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter)));

    /// <summary>Begins the callers of a request of <paramref name="person"/> in harbor: the signed-in user, and the seat.</summary>
    private static IDisposable AsSeatOf(DemoPerson person) => SampleCallers.BeginSeatOf(person, Harbor);
}
