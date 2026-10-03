using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.UseCases;
using FluentAssertions;
using GreenDonut.Data;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// What every command and query of the sample's modules requires of its caller, and how a request is shaped. A use
/// case that forgot to say what it requires would reach its handler unchecked, so the list of requests is not
/// kept here: it is read from the host's container, and every request in it is held to these rules.
/// </summary>
/// <remarks>
/// A request declares what it requires through its module's own interface, and its module's pipeline behavior,
/// which the toolkit's generator writes for that interface, asks the module's checks before the handler runs. The
/// rules here keep that from being gone round: every request declares, what each declares is written out once
/// more below, every requirement that is declared has a check in its module's set, every request passes its
/// module's behavior, nothing sends a request from inside another, a query reads and answers with data, and what
/// the Tenancy package decides is handed to the package and to nothing else.
/// </remarks>
public sealed class AccessDeclarationTests(SampleWithoutDatabase sample) : IClassFixture<SampleWithoutDatabase>
{
    private static readonly TenancyRequirement ThePackage = new TenancyRequirement.DecidedByThePackage();

    private static readonly ProjectId TheProject = ProjectId.CreateSequential();

    private static readonly OrganizationUnitId TheUnit = OrganizationUnitId.CreateSequential();

    private static readonly TenantId TheTenant = TenantId.CreateSequential();

    /// <summary>The projects of a request about several: one list, so the requirement that names it equals the one expected.</summary>
    private static readonly IReadOnlyList<ProjectId> TheProjects = [TheProject, ProjectId.CreateSequential()];

    /// <summary>
    /// Every command and query, with what it declares. A real request each, with ids of its own, so a declaration
    /// that took the wrong field of its request would not match what is written here.
    /// </summary>
    private static readonly (IMessage Request, AccessRequirement Declares)[] Declared =
    [
        (new VisibleProjects(), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new ProjectDetail(TheProject), MemberAccess.On(ProjectKeys.View, TheProject)),
        (new ProjectsById([TheProject]), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new CrewsOfProjects([TheProject]), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new AbilitiesOnProjects([TheProject]), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new KeyOnProject(TheProject, ProjectKeys.Edit), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new KeysOnProjects([TheProject], [ProjectKeys.Edit]), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new KeysHeldAtRoot([ProjectKeys.Open]), new TenancyRequirement.InTenant()),
        (new OpenProject("P-900", "Quay wall", TheUnit, SeatId.CreateSequential(), ProjectId.CreateSequential()), new ProjectsRequirement.AtUnit(ProjectKeys.Open, TheUnit)),
        (new ChangeProjectName(TheProject, "Quay wall, east", ExpectedVersion: 7), MemberAccess.On(ProjectKeys.Edit, TheProject, 7)),
        (new PlanProject(TheProject, DateRange.Of(new DateOnly(2026, 10, 5)), ExpectedVersion: 7), MemberAccess.On(ProjectKeys.Edit, TheProject, 7)),
        (new MoveProjectToUnit(TheProject, OrganizationUnitId.CreateSequential()), MemberAccess.On(ProjectKeys.Edit, TheProject)),
        (new CloseProject(TheProject), MemberAccess.On(ProjectKeys.Close, TheProject)),
        (new ReopenProject(TheProject), MemberAccess.On(ProjectKeys.Close, TheProject)),
        (new AddCrewMember(TheProject, SeatId.CreateSequential(), ProjectRoleId.CreateSequential(), Until: null), MemberAccess.On(ProjectKeys.ManageCrew, TheProject)),
        (new GiveCrewRole(TheProject, SeatId.CreateSequential(), ProjectRoleId.CreateSequential(), Until: null), MemberAccess.On(ProjectKeys.ManageCrew, TheProject)),
        (new TakeCrewRole(TheProject, SeatId.CreateSequential(), ProjectRoleId.CreateSequential()), MemberAccess.On(ProjectKeys.ManageCrew, TheProject)),
        (new RemoveCrewMember(TheProject, SeatId.CreateSequential()), MemberAccess.On(ProjectKeys.ManageCrew, TheProject)),
        (new AllCrewMembers(TheProject), MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)),
        (new ChangeProjectOwner(TheProject, SeatId.CreateSequential()), MemberAccess.On(ProjectKeys.ChangeOwner, TheProject)),
        (new TenantProjects(TheTenant), new TenancyRequirement.OperatorsOnly()),
        (new TenantProjectRoles(), new TenancyRequirement.InTenant()),
        (new ProjectRolesById([ProjectRoleId.CreateSequential()]), new TenancyRequirement.InTenant()),
        (new MakeProjectRole("Rigger", "Rigs the hoists", [ProjectKeys.View]), new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage)),
        (new RenameProjectRole(ProjectRoleId.CreateSequential(), "Rigger", null), new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage)),
        (new SetProjectRoleKeys(ProjectRoleId.CreateSequential(), []), new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage)),
        (new ArchiveProjectRole(ProjectRoleId.CreateSequential()), new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage)),
        (new SetUpProjectRoles(), new TenancyRequirement.SystemWorkInTenant()),
        (new SeatsOfMine(), new AccessRequirement.Open(SeatsOfMine.OpenBecause)),
        (new OverviewOfMine(), ThePackage),
        (new OrganizationUnits(), ThePackage),
        (new TenantSeats(), ThePackage),
        (new TenantRoles(), ThePackage),
        (new SeatsById([SeatId.CreateSequential()]), ThePackage),
        (new OrganizationUnitsById([OrganizationUnitId.CreateSequential()]), ThePackage),
        (new RolesById([RoleId.CreateSequential()]), ThePackage),
        (new CatalogueContents(), new TenancyRequirement.InTenant()),
        (new UnitsWhereIHold(TenancyKeys.UnitsManage), new TenancyRequirement.InTenant()),
        (new AddOrganizationUnit(OrganizationUnitId.CreateSequential(), "North", "region"), ThePackage),
        (new MoveOrganizationUnit(OrganizationUnitId.CreateSequential(), OrganizationUnitId.CreateSequential()), ThePackage),
        (new ArchiveOrganizationUnit(OrganizationUnitId.CreateSequential()), ThePackage),
        (new ChangeTenantShape(TenantShape.Hierarchical), ThePackage),
        (new MakePlacement(SeatId.CreateSequential(), OrganizationUnitId.CreateSequential(), Primary: true), ThePackage),
        (new WithdrawPlacement(SeatId.CreateSequential(), OrganizationUnitId.CreateSequential()), ThePackage),
        (new MakeGrant(SeatId.CreateSequential(), OrganizationUnitId.CreateSequential(), RoleId.CreateSequential(), Until: null, Reason: null), ThePackage),
        (new RevokeGrant(SeatId.CreateSequential(), OrganizationUnitId.CreateSequential(), RoleId.CreateSequential()), ThePackage),
        (new SuspendTenantSeat(SeatId.CreateSequential()), ThePackage),
        (new ReactivateTenantSeat(SeatId.CreateSequential()), ThePackage),
        (new DeactivateTenantSeat(SeatId.CreateSequential()), ThePackage),
        (new CreateTenantRole("Storekeeper", "Keeps the stores", []), ThePackage),
        (new SetRoleKeys(RoleId.CreateSequential(), []), ThePackage),
        (new ArchiveTenantRole(RoleId.CreateSequential()), ThePackage),
        (new MarkTenantAsDemo(), new TenancyRequirement.SystemWorkInTenant()),
        (new AccessHistory(new PagingArguments(first: 10)), new TenancyRequirement.ForTheWholeTenant(TenancyKeys.HistoryView)),
        (new InvitePerson("wren@example.test", OrganizationUnitId.CreateSequential(), RoleId.CreateSequential(), Until: null, DisplayName: null), ThePackage),
        (new OpenInvitations(), ThePackage),
        (new CancelInvitation(InvitationId.CreateSequential()), ThePackage),
        (new AcceptInvitation("a-token-nobody-was-given", DisplayName: null), ThePackage),
        (new AllTenants(), new TenancyRequirement.OperatorsOnly()),
        (new TenantAccessHistory(TheTenant, new PagingArguments(first: 10)), new TenancyRequirement.OperatorsOnly()),
        (new ProjectInspections(TheProject), new InspectionsRequirement.OnProject(ProjectKeys.View, TheProject)),
        (new InspectionDetail(TheProject, InspectionId.CreateSequential()), new InspectionsRequirement.OnProject(ProjectKeys.View, TheProject)),
        (new InspectionsOfProjects(TheProjects, new PagingArguments(first: 5)), new InspectionsRequirement.OnProjectsInReach(ProjectKeys.View, TheProjects)),
        (new ProjectsOpenToRecording(TheProjects), new InspectionsRequirement.OnProjectsInReach(InspectionKeys.Record, TheProjects)),
        (new RecordInspection(TheProject, "Loose railing"), new InspectionsRequirement.OnOpenProject(InspectionKeys.Record, TheProject, BySeat: true)),
        (new TenantProjectInspections(TheTenant, TheProject), new TenancyRequirement.OperatorsOnly()),
    ];

    private static IReadOnlyList<HandledRequest> Requests => HostRegistrations.Requests;

    [Fact]
    public void The_modules_with_requests_are_found_by_their_access_vocabulary()
    {
        SampleLayout.OnTheMediator.Should().ContainKey("Tenants")
            .WhoseValue.Should().Be((typeof(ITenantsRequest), typeof(TenantsAccessBehavior<,>)));
        SampleLayout.OnTheMediator.Should().ContainKey("Projects")
            .WhoseValue.Should().Be((typeof(IProjectsRequest), typeof(ProjectsAccessBehavior<,>)));
        SampleLayout.OnTheMediator.Should().ContainKey("Inspections")
            .WhoseValue.Should().Be((typeof(IInspectionsRequest), typeof(InspectionsAccessBehavior<,>)));
        SampleLayout.OnTheMediator.Keys.Should().BeEquivalentTo(SampleLayout.Modules, "every module's use cases are commands and queries");
    }

    [Fact]
    public void Every_command_and_query_declares_its_access_or_is_marked_open()
    {
        Requests.Should().NotBeEmpty("the host handles requests, and they are found in its container");

        foreach (var request in Requests)
        {
            var application = SampleLayout.Projects.SingleOrDefault(project => project.Layer == Layer.Application && project.Anchor.Assembly == request.Type.Assembly);
            application.Should().NotBeNull("{0} is declared in a module's application project", request.Type);

            // Where in that project a request lives, with its feature, is FeatureFolderTests' to hold.
            SampleLayout.OnTheMediator.Should().ContainKey(
                application!.Module,
                "{0} has requests, so its application project declares I{0}Request in its Access folder, marked [AccessRequests], and gets {0}AccessBehavior<,> written beside it", application.Module);
            var own = SampleLayout.OnTheMediator[application.Module].Request;
            own.IsAssignableFrom(request.Type).Should().BeTrue(
                "{0} says what it requires of its caller by implementing {1}, or its module's check never sees it", request.Type.Name, own.Name);
            SampleLayout.OnTheMediator.Values.Where(other => other.Request != own && other.Request.IsAssignableFrom(request.Type))
                .Should().BeEmpty("{0} is checked by its own module, and by no other", request.Type.Name);
        }

        foreach (var (request, _) in Declared)
        {
            var declared = DeclaredBy(request);
            declared.Should().NotBeNull("{0} declares what it requires", request.GetType().Name);

            // A request that requires nothing says so, and says why.
            if (declared is AccessRequirement.Open open)
            {
                open.Reason.Should().NotBeNullOrWhiteSpace("{0} is open, and says why", request.GetType().Name);
            }
        }

        Declared.Should().Contain(row => DeclaredBy(row.Request) is AccessRequirement.Open, "one request is open, or the reason is never looked at");
    }

    [Fact]
    public void A_module_s_request_interface_is_the_one_line_the_behavior_is_written_from()
    {
        foreach (var (module, (request, behavior)) in SampleLayout.OnTheMediator)
        {
            request.IsDefined(typeof(DDDToolkit.Abstractions.Attributes.AccessRequestsAttribute), inherit: false)
                .Should().BeTrue("{0}'s request interface is marked, so the toolkit writes its behavior", module);
            request.GetInterfaces().Should().Equal([typeof(IRequireAccess)], "a request says what it requires through the toolkit's interface");
            request.GetMembers().Should().BeEmpty("{0} has no member of its own", request.Name);

            // The behavior is nobody's source file: the generator wrote it, over the module's own set of checks.
            SampleLayout.SourceFilesIn(SampleLayout.DirectoryOf(SampleLayout.Project(module, Layer.Application)))
                .Should().NotContain(file => Path.GetFileNameWithoutExtension(file).EndsWith("AccessBehavior", StringComparison.Ordinal), "no access behavior is written by hand in {0}", module);
            behavior.GetConstructors().Should().ContainSingle()
                .Which.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal([typeof(AccessChecks<>).MakeGenericType(request)]);
            behavior.GetGenericArguments()[0].GetGenericParameterConstraints().Should().BeEquivalentTo([request, typeof(IMessage)]);
        }
    }

    [Fact]
    public async Task Every_requirement_a_request_declares_has_a_check_in_its_module_s_set()
    {
        // The behavior is the same for every module; what differs is the checks a module registers. A case no check
        // of the module decides would stop every request that declares it when it is sent, so it is found here.
        await using var scope = sample.Services.CreateAsyncScope();

        foreach (var (request, declares) in Declared)
        {
            Decides(scope.ServiceProvider, ModuleOf(request), declares).Should().BeTrue(
                "{0} declares {1}, so a check registered for its module's interface decides that case", request.GetType().Name, declares.GetType().Name);
        }

        // Each module's set holds its own checks and no other module's: a case of Projects' own is nobody's to
        // decide in Tenants or Inspections, and the other way round. So the rule above proves something.
        Decides(scope.ServiceProvider, "Tenants", MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)).Should().BeFalse();
        Decides(scope.ServiceProvider, "Inspections", MemberAccess.SeenWith<ProjectId>(ProjectKeys.View)).Should().BeFalse();
        Decides(scope.ServiceProvider, "Projects", new InspectionsRequirement.OnProject(ProjectKeys.View, TheProject)).Should().BeFalse();
        Decides(scope.ServiceProvider, "Tenants", new InspectionsRequirement.OnProject(ProjectKeys.View, TheProject)).Should().BeFalse();

        // Tenancy's cases are decided in every module, by the package's check registered for each.
        foreach (var module in SampleLayout.OnTheMediator.Keys)
        {
            Decides(scope.ServiceProvider, module, new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage)).Should().BeTrue("{0} registers Tenancy's check", module);
        }

        Declared.Select(row => row.Declares.GetType().DeclaringType ?? row.Declares.GetType()).Distinct().Should().BeEquivalentTo(
            [typeof(AccessRequirement), typeof(TenancyRequirement), typeof(MemberAccess<>), typeof(ProjectsRequirement), typeof(InspectionsRequirement)],
            "the toolkit's open case, the Tenancy package's cases, the Membership package's, and the two modules' own");
    }

    [Fact]
    public void What_each_command_and_query_declares()
    {
        Declared.Select(row => row.Request.GetType().FullName).Should().BeEquivalentTo(
            Requests.Select(request => request.Type.FullName),
            "every command and query the host handles is written out here with what it declares, each once, and nothing that is gone");

        foreach (var (request, declares) in Declared)
        {
            DeclaredBy(request).Should().Be(declares, "that is what {0} requires of its caller", request.GetType().Name);
        }
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalogue()
    {
        var catalogue = sample.Services.GetRequiredService<TenancyCatalogue>();

        // A key a request requires: the Key of what it declares, and every constant of the request that names one.
        var keys = Declared.SelectMany(row => KeysOf(DeclaredBy(row.Request)!))
            .Concat(Requests.SelectMany(request => request.Type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string) && field.Name.EndsWith("Key", StringComparison.Ordinal))
                .Select(field => (string)field.GetRawConstantValue()!)))
            .Distinct()
            .ToList();

        keys.Where(key => !catalogue.Knows(key)).Should().BeEmpty("a request that required a key nobody can hold would refuse everyone");
    }

    [Fact]
    public void Only_a_query_declares_what_it_shows()
    {
        // "Seen with a key" is a filter on what a query lists, checked nowhere but in the query's own statement. A
        // command that declared it would run unchecked.
        var commands = Requests.Where(request => !request.IsQuery).Select(request => request.Type).ToHashSet();

        Declared.Where(row => commands.Contains(row.Request.GetType()))
            .Where(row => DeclaredBy(row.Request)!.GetType().Name == "SeenWith")
            .Select(row => row.Request.GetType().Name)
            .Should().BeEmpty();
        Declared.Should().Contain(row => DeclaredBy(row.Request) is MemberAccess<ProjectId>.SeenWith, "queries do declare it, or this proves nothing");
    }

    [Fact]
    public void Only_the_access_rules_make_a_reach()
    {
        // A reach is what a reading filters by: what the projects' rules say the caller the request runs as
        // reaches. Only the Membership package makes one, from the rules and the caller, so an adapter, a route or
        // a test is never the one that decides what a reading shows.
        typeof(MemberReach<ProjectId>).GetConstructors().Should().BeEmpty("a reach has no public constructor");
        typeof(MemberReach<ProjectId>).GetProperties().Should().OnlyContain(property => property.SetMethod == null, "and nothing of it can be changed afterwards");
        SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(Program).Assembly)
            .SelectMany(TypeScan.TypesOf)
            .Where(type => IsReach(type.BaseType))
            .Should().BeEmpty("nor can a reach of the sample's own be handed over in its place");

        static bool IsReach(Type? type)
            => type is not null && ((type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MemberReach<>)) || IsReach(type.BaseType));
    }

    [Fact]
    public async Task Every_request_passes_tracing_then_its_module_s_access()
    {
        await using var scope = sample.Services.CreateAsyncScope();

        foreach (var request in Requests)
        {
            var steps = scope.ServiceProvider.GetServices(typeof(IPipelineBehavior<,>).MakeGenericType(request.Type, request.Response))
                .Select(step => step!.GetType().GetGenericTypeDefinition())
                .ToList();

            steps.Should().Equal(
                [typeof(RequestTracingBehavior<,>), SampleLayout.OnTheMediator[request.Module].Behavior],
                "{0} passes tracing, then {1}'s access check, then its handler, and nothing else", request.Type.Name, request.Module);
        }
    }

    [Fact]
    public void What_the_package_decides_only_the_package_handles()
    {
        // A handler whose request leaves the decision to the Tenancy package hands it to the package and to nothing
        // else: a command to one of the package's use cases, a query to the package's directory through the read
        // port. And the other way round: a handler that calls a use case of the package says that the package
        // decides, so no check is promised here that is not made.
        //
        // Three commands take more, which decides nothing. Inviting takes the identity provider's accounts,
        // asked for an account at the address once the package has issued the invitation, the page the
        // provider's mail leads to, where the host named one, and the stores the invitation is found and saved
        // through, to keep the id of the account the provider made. Cancelling takes the accounts and the store
        // of invitations too, to delete an account nobody used once the package has cancelled. Accepting takes the
        // caller, whose own address it hands the package beside the request. And the open invitations are the one
        // answer of the package's that its directory does not give: the read port asks the package for them.
        //
        // The two queries that answer roles take Tenancy's answers about the caller as well. Who may ask is still
        // the package's to decide; what they ask beside it is whether the caller holds the key a role's keys are
        // answered to, which decides what is in the answer and refuses nobody.
        //
        // The rule is about every module, not the one that happens to declare it today. "The package decides"
        // passes the check with nothing asked, in whichever module's set the package's check sits, so a request
        // of Projects or Inspections that declared it would reach its handler unchecked unless that handler did
        // nothing but hand the request to the package.
        Declared.Where(row => ModuleOf(row.Request) == "Tenants").Select(row => row.Declares)
            .Should().OnlyContain(declares => declares is TenancyRequirement || declares is AccessRequirement.Open, "the Tenants module has no case of its own, and so no check of its own");
        Requests.Select(request => request.Module).Distinct().Should().BeEquivalentTo(SampleLayout.Modules, "every module's requests are held to it");

        foreach (var request in Requests)
        {
            var takes = request.Handler.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType)
                .Where(taken => !(request.Type == typeof(InvitePerson) && (taken == typeof(DDDToolkit.Identity.IIdentityAccounts) || taken == typeof(Examples.Tenancy.Tenants.Application.Invitations.InvitationPage))))
                .Where(taken => !((request.Type == typeof(InvitePerson) || request.Type == typeof(CancelInvitation))
                    && (taken == typeof(DDDToolkit.Identity.IIdentityAccounts)
                        || taken == typeof(SampleTenancy.IInvitationStore<Examples.Tenancy.Tenants.Domain.Aggregates.Invitations.Invitation, InvitationId>)
                        || taken == typeof(SampleTenancy.IStore))))
                .Where(taken => !(request.Type == typeof(AcceptInvitation) && taken == typeof(DDDToolkit.Access.ICallerAccessor)))
                .Where(taken => !((request.Type == typeof(TenantRoles) || request.Type == typeof(RolesById))
                    && taken == typeof(DDDToolkit.Supporting.Tenancy.Access.ITenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>)))
                .ToList();
            var decidedByThePackage = DeclaredBy(Declared.Single(row => row.Request.GetType() == request.Type).Request) is TenancyRequirement.DecidedByThePackage;

            if (decidedByThePackage)
            {
                takes.Should().ContainSingle("{0} hands its request on, and needs one thing for that", request.Handler.Name)
                    .Which.Should().Match<Type>(
                        taken => request.IsQuery ? taken == typeof(ITenancyReads) : IsPackageCommands(taken),
                        "{0} is decided by the package", request.Type.Name);

                if (request.IsQuery)
                {
                    var named = TypeScan.Of(TypeScan.WithNested(request.Handler)).Uses.Select(use => use.Type).ToList();
                    named.Should().NotContain(typeof(ITenancyReading), "{0} reads no rows itself, past the package's own check", request.Handler.Name);
                    if (request.Type != typeof(OpenInvitations))
                    {
                        named.Should().Contain(typeof(TenancyUseCases<,,,,,,,,>.TenancyDirectory), "{0} asks the package's directory", request.Handler.Name);
                    }
                }
            }
            else
            {
                takes.Where(IsPackageCommands).Should().BeEmpty("{0} does not say the package decides, so it must not lean on a check of the package's", request.Handler.Name);
            }
        }

        Requests.Should().Contain(request => !request.IsQuery && DeclaredBy(Declared.Single(row => row.Request.GetType() == request.Type).Request) is TenancyRequirement.DecidedByThePackage);
    }

    [Fact]
    public void Nothing_is_published_or_streamed()
    {
        // A notification goes to its handlers without passing a pipeline, and a plain request is neither a command
        // nor a query, so nothing says whether it may change anything. A stream passes a pipeline of its own: the
        // toolkit writes each module a second behavior for that one, so a stream query would be held to what it
        // declares, but the rules of this class are written for commands and queries. None of the three exists here.
        var types = SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(Program).Assembly)
            .SelectMany(TypeScan.TypesOf)
            .Where(type => type.Namespace is { } name && name.StartsWith("Examples.Tenancy.", StringComparison.Ordinal))
            .ToList();

        types.Where(type => typeof(INotification).IsAssignableFrom(type) || typeof(IStreamMessage).IsAssignableFrom(type) || typeof(IBaseRequest).IsAssignableFrom(type))
            .Should().BeEmpty("every request is a command or a query");
        types.Where(type => type.GetInterfaces().Any(implemented => implemented.IsGenericType && (
                implemented.GetGenericTypeDefinition() == typeof(INotificationHandler<>)
                || implemented.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                || implemented.GetGenericTypeDefinition() == typeof(IStreamRequestHandler<,>)
                || implemented.GetGenericTypeDefinition() == typeof(IStreamCommandHandler<,>)
                || implemented.GetGenericTypeDefinition() == typeof(IStreamQueryHandler<,>))))
            .Should().BeEmpty("and every handler handles a command or a query");

        // The pipeline of the streams holds what the toolkit wrote into it and nothing else: each module's access
        // behavior for streams, beside the one for its commands and queries, over the same checks. So neither
        // pipeline is one the access checks are not part of.
        var forStreams = SampleLayout.OnTheMediator.ToDictionary(
            module => module.Key,
            module => module.Value.Behavior.Assembly.GetType($"{module.Value.Behavior.Namespace}.{module.Key}AccessStreamBehavior`2"));
        forStreams.Should().NotContainValue(null, "a module that has an access behavior has the one for streams too");
        types.Where(type => type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IStreamPipelineBehavior<,>)))
            .Should().BeEquivalentTo(forStreams.Values, "nobody writes a behavior into the pipeline of the streams by hand");
        HostRegistrations.All
            .Where(descriptor => descriptor.ServiceType.IsGenericType && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IStreamPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .Should().BeEquivalentTo(forStreams.Values, "and each module's registration put its own there, once");
        foreach (var (module, behavior) in forStreams)
        {
            behavior!.GetConstructors().Should().ContainSingle()
                .Which.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal([typeof(AccessChecks<>).MakeGenericType(SampleLayout.OnTheMediator[module].Request)]);
            behavior.GetGenericArguments()[0].GetGenericParameterConstraints().Should().BeEquivalentTo([SampleLayout.OnTheMediator[module].Request, typeof(IStreamMessage)]);
        }

        HostRegistrations.All.Select(descriptor => descriptor.ServiceType)
            .Where(service => service.IsGenericType && HostRegistrations.IsRequestHandler(service.GetGenericTypeDefinition()))
            .Should().OnlyContain(service => service.GetGenericTypeDefinition() != typeof(IRequestHandler<,>));
    }

    [Fact]
    public void No_application_project_sends()
    {
        // A request sent from inside a handler or a behavior would run in the outer request's unit of work and
        // under its caller, checked as if it had come from outside. A handler that needs another module asks its
        // contract instead.
        foreach (var project in SampleLayout.Projects.Where(project => project.Layer == Layer.Application))
        {
            var scan = TypeScan.Of(project.Anchor.Assembly);

            scan.NotResolved.Should().BeEmpty("every token in the code of {0} is read", project.Name);
            scan.Uses.Where(use => use.Type == typeof(ISender) || use.Type == typeof(IMediator) || use.Type == typeof(IPublisher))
                .Select(use => use.ToString())
                .Should().BeEmpty("{0} declares requests and handles them; only what is outside the application sends them", project.Name);
        }
    }

    [Fact]
    public void No_query_handler_takes_a_write_port()
    {
        var queries = Requests.Where(request => request.IsQuery).ToList();

        queries.Should().NotBeEmpty();
        foreach (var query in queries)
        {
            TypeScan.Of(TypeScan.WithNested(query.Handler)).Uses
                .Where(use => IsWritePort(use.Type) || IsPackageCommands(use.Type))
                .Select(use => use.ToString())
                .Should().BeEmpty("{0} reads: it names nothing that loads to change, or saves", query.Handler.Name);
        }

        IsWritePort(typeof(SampleTenancy.IStore)).Should().BeTrue("the package's store saves");
        IsWritePort(typeof(IProjectStore)).Should().BeTrue("Projects' store saves");
        IsWritePort(typeof(ITenancyReads)).Should().BeFalse("a read port does not");
        IsWritePort(typeof(IProjectReads)).Should().BeFalse("nor does Projects'");
        IsWritePort(typeof(IProjectReading)).Should().BeFalse("nor what it opens");
        IsWritePort(typeof(IInspectionStore)).Should().BeTrue("Inspections' store saves");
        IsWritePort(typeof(IInspectionReads)).Should().BeFalse("and its read port does not");
    }

    [Fact]
    public void A_query_answers_with_data_only()
    {
        // What a query answers with outlives the context it was read on, and crosses to whoever asked: no entity
        // that could be changed, no query that still has to run, no service.
        foreach (var query in Requests.Where(request => request.IsQuery))
        {
            GraphOf(query.Response, [])
                .Where(type => typeof(IEntity).IsAssignableFrom(type)
                    || typeof(IQueryable).IsAssignableFrom(type)
                    || SampleLayout.IsPort(type)
                    || HostRegistrations.Services.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type))
                .Should().BeEmpty("{0} answers with data", query.Type.Name);
        }

        GraphOf(typeof(IReadOnlyList<Seat>), []).Should().Contain(type => typeof(IEntity).IsAssignableFrom(type), "an aggregate in a list would be found");
        GraphOf(typeof(HeldUnits), []).Should().Contain(typeof(OrganizationUnitId), "what a response's properties carry is looked into");
        GraphOf(typeof(IReadOnlyDictionary<ProjectId, ProjectCrew>), []).Should().Contain([typeof(CrewOverview), typeof(CrewRoleOverview)], "to the end");
        GraphOf(typeof(Page<ProjectOverview>), []).Should().Contain([typeof(ProjectOverview), typeof(DateRange)], "through a page too");
        GraphOf(typeof(InspectionList), []).Should().Contain([typeof(InspectionOverview), typeof(InspectionId)], "in every module");
        GraphOf(typeof(NotRead), []).Should().Contain(type => typeof(IQueryable).IsAssignableFrom(type), "a set that has not been read would be found");
    }

    [Fact]
    public void A_command_answers_with_an_id_or_with_nothing()
    {
        // A command changes something, and says at most which thing it made: its id, for whoever sent the command
        // to ask about with a query. It hands back no entity, and nothing it read on the way. One answers more:
        // inviting answers the invitation's token, which is kept nowhere, so no query could answer it afterwards.
        var commands = Requests.Where(request => !request.IsQuery && request.Type != typeof(InvitePerson)).ToList();
        Requests.Single(request => request.Type == typeof(InvitePerson)).Response.Should().Be(typeof(SampleTenancy.IssuedInvitation<InvitationId>));

        commands.Where(command => command.Response != typeof(Unit) && !typeof(IEntityId).IsAssignableFrom(command.Response))
            .Select(command => $"{command.Type.Name} answers with {command.Response}")
            .Should().BeEmpty("what a caller wants to know of what a command changed, a query answers");
        commands.Select(command => command.Response).Should()
            .Contain(typeof(Unit), "some answer nothing")
            .And.Contain(typeof(ProjectId), "and some the id of what they made, or this proves nothing");
    }

    [Fact]
    public void Only_the_caller_s_own_seats_are_looked_up_across_tenants()
    {
        // One read looks past the tenant filter: a person's seats in every tenant, by identity. It is there for the
        // identity of the caller's own token, which one handler checks before it asks. Nothing else calls it: not
        // another handler, not a route, not the host.
        var named = SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(Program).Assembly)
            .SelectMany(assembly => TypeScan.Of(assembly).MembersNamed)
            .ToList();

        named.Where(use => use.Member.DeclaringType == typeof(ITenancyReads) && use.Member.Name == nameof(ITenancyReads.SeatsOfAsync))
            .Select(use => TypeScan.Outermost(use.By)).Distinct()
            .Should().Equal([typeof(SeatsOfMineHandler)], "the port's lookup by identity is the one query's that asks for the caller's own seats");

        // The lookup itself is the Tenancy package's, behind its rule for which token roles hold seats: the tenant
        // selection applies it and then asks the seat directory. In the sample only the adapter of that port method
        // reaches the selection's lookup, and nothing asks the directory itself, past the rule.
        named.Where(use => use.Member.DeclaringType is { IsGenericType: true } declaring && declaring.GetGenericTypeDefinition() == typeof(TenantSelection<,>)
                           && use.Member.Name == nameof(TenantSelection<TenantId, SeatId>.SeatsOfAsync))
            .Select(use => TypeScan.Outermost(use.By).Name).Distinct()
            .Should().Equal(["EfTenancyReads"], "the package's lookup of a person's own seats is asked through the read port, and nowhere else");
        named.Where(use => use.Member.DeclaringType is { IsGenericType: true } declaring && declaring.GetGenericTypeDefinition() == typeof(ISeatDirectory<,>))
            .Should().BeEmpty("the seat directory is the package's to ask, under its rule for seated token roles");
    }

    /// <summary>An answer that would carry a set not read yet: what <see cref="A_query_answers_with_data_only"/> must find.</summary>
    private sealed record NotRead(IQueryable<ProjectId> Projects);

    /// <summary>What <paramref name="request"/> declares it requires, read the way its module's checks read it.</summary>
    private static AccessRequirement? DeclaredBy(IMessage request)
    {
        SampleLayout.OnTheMediator[ModuleOf(request)].Request.IsInstanceOfType(request).Should().BeTrue("{0} implements its module's request interface", request.GetType().Name);
        return ((IRequireAccess)request).RequiredAccess;
    }

    /// <summary>The module a request is of: the third part of its namespace.</summary>
    private static string ModuleOf(IMessage request) => request.GetType().Namespace!.Split('.')[2];

    /// <summary>
    /// Whether the checks registered for the request interface of <paramref name="module"/> decide
    /// <paramref name="requirement"/>: asked of the module's own set, as the host's container makes it.
    /// </summary>
    private static bool Decides(IServiceProvider scoped, string module, AccessRequirement requirement)
    {
        var checks = scoped.GetRequiredService(typeof(AccessChecks<>).MakeGenericType(SampleLayout.OnTheMediator[module].Request));
        return (bool)checks.GetType().GetMethod(nameof(AccessChecks<IRequireAccess>.Decides))!.Invoke(checks, [requirement])!;
    }

    /// <summary>The permission key a requirement names, if it names one.</summary>
    private static IEnumerable<string> KeysOf(object requirement)
        => requirement.GetType().GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.Name.EndsWith("Key", StringComparison.Ordinal))
            .Select(property => (string)property.GetValue(requirement)!);

    /// <summary>
    /// A use-case class of the Tenancy package that changes something: its tenant, organization, seat, role and
    /// invitation commands. The last has type parameters of its own, which its name counts after a backtick.
    /// </summary>
    private static bool IsPackageCommands(Type type)
        => type.IsGenericType
            && type.DeclaringType == typeof(TenancyUseCases<,,,,,,,,>)
            && type.Name.Split('`')[0].EndsWith("Commands", StringComparison.Ordinal);

    /// <summary>A port that saves: one of an application project's, or the Tenancy package's store.</summary>
    private static bool IsWritePort(Type type)
        => SampleLayout.IsPort(type) && type.GetMethods().Any(method => method.Name == "SaveAsync");

    /// <summary>
    /// Every type an answer is made of: the type, what a collection holds, and the types of its public properties,
    /// to the end. The type arguments of a class the answer is nested in are not part of it: the Tenancy package
    /// nests its DTOs in a class that is generic over the application's aggregates, and carries none of them.
    /// </summary>
    private static IEnumerable<Type> GraphOf(Type type, HashSet<Type> seen)
    {
        if (type.HasElementType)
        {
            type = type.GetElementType()!;
        }

        if (!seen.Add(type))
        {
            yield break;
        }

        yield return type;

        var ofTheFramework = type.Namespace is { } name && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
        var inside = ofTheFramework
            ? type.IsGenericType ? type.GetGenericArguments() : []
            : type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.PropertyType);

        foreach (var part in inside.SelectMany(part => GraphOf(part, seen)).ToList())
        {
            yield return part;
        }
    }
}
