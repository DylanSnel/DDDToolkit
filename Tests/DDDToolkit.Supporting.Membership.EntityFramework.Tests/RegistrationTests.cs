using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The registration of a resource with members: called once for each kind of resource in one container,
/// generated for each member class under the name of the resource it names, and refusing at start-up whatever
/// it would otherwise read as something it is not.
/// </summary>
public sealed class RegistrationTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Rules a document could be registered with: its members are users, by their id.</summary>
    private static MembershipRules OtherRules(string name, MemberSource? members = null)
        => new(name, keys: [name + ".read"], roles: [new("reader", [name + ".read"])], members: members);

    [Fact]
    public void The_registration_is_generated_for_each_member_class_of_the_project_and_named_after_its_resource()
    {
        // The TestHost declares seven member classes, each naming the resource it is a member of. The generator
        // names a registration after each resource, closed over the class, its three types, the resource and the
        // resource's id, and leaves only the context to the caller; a second of the same name that takes
        // the class that answers what the resource's rules ask of the host, next to the context; and a third,
        // closed over the resource and its id, that adds the resource's access check for a request interface.
        var generated = typeof(FilingHost).Assembly.GetType("DDDToolkit.Supporting.Membership.EntityFramework.GeneratedMembershipEntityFrameworkServiceCollectionExtensions")!;

        generated.IsNotPublic.Should().BeTrue("a registration closed over a project's classes is that project's own");
        var methods = generated.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        methods.Select(method => method.Name + "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">").Should().BeEquivalentTo(
            "AddDocumentMembership<TContext>", "AddDocumentMembership<TContext, TPorts>", "AddDocumentMemberAccess<TRequests>",
            "AddBinderMembership<TContext>", "AddBinderMembership<TContext, TPorts>", "AddBinderMemberAccess<TRequests>",
            "AddFolderMembership<TContext>", "AddFolderMembership<TContext, TPorts>", "AddFolderMemberAccess<TRequests>",
            "AddPalletMembership<TContext>", "AddPalletMembership<TContext, TPorts>", "AddPalletMemberAccess<TRequests>",
            "AddCrateMembership<TContext>", "AddCrateMembership<TContext, TPorts>", "AddCrateMemberAccess<TRequests>",
            "AddPlotMembership<TContext>", "AddPlotMembership<TContext, TPorts>", "AddPlotMemberAccess<TRequests>",
            "AddShedMembership<TContext>", "AddShedMembership<TContext, TPorts>", "AddShedMemberAccess<TRequests>");
        methods.Where(method => method.Name.EndsWith("Membership", StringComparison.Ordinal)).Should().OnlyContain(
            method => method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(IServiceCollection), typeof(MembershipRules) }),
            "the rules are all a host hands over: nothing about who may change the members is said where a resource is registered");
        methods.Where(method => method.Name.EndsWith("MemberAccess", StringComparison.Ordinal)).Should().OnlyContain(
            method => method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(IServiceCollection) }),
            "whose requests the check is for is all a module says, and it says it as the type argument");
    }

    [Fact]
    public void The_access_check_of_a_resource_is_added_once_before_the_resource_is_registered_or_after_it()
    {
        // The TestHost adds each check after its resource, with the registration generated for it. Here the
        // package's own method adds one first, and again: the check is one service however it got there.
        var services = new ServiceCollection();
        services.AddMemberAccess<Document, DocumentId, IFilingRequest>();
        var before = services.Count;
        services.AddMemberAccess<Document, DocumentId, IFilingRequest>();
        services.Count.Should().Be(before, "adding a check for an interface again changes nothing");

        FilingHost.Add(services);

        services.Count(descriptor => descriptor.ServiceType == typeof(MemberAccessCheck<Document, DocumentId>)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(MemberAccessCheck<Folder, FolderId>)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(AccessChecks<IFilingRequest>)).Should().Be(1, "one set for the module's interface, with both checks in it");

        FluentActions.Invoking(() => MembershipEntityFrameworkServiceCollectionExtensions.AddMemberAccess<Document, DocumentId, IFilingRequest>(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Registering_a_resource_again_as_it_is_registered_changes_nothing()
    {
        var services = new ServiceCollection();
        FilingHost.Add(services);
        var registered = services.Count;

        FilingHost.Add(services);
        services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(DocumentMembership.Rules);

        services.Count.Should().Be(registered);
        services.Count(descriptor => descriptor.ServiceType == typeof(MembershipRegistration)).Should().Be(2);
        services.Count(descriptor => descriptor.ServiceType == typeof(IMemberQuestions<DocumentId>)).Should().Be(1);
    }

    [Fact]
    public void A_resource_is_registered_once_with_one_set_of_rules()
    {
        var services = new ServiceCollection();
        FilingHost.Add(services);

        FluentActions.Invoking(() => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(OtherRules("papers")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Document is registered already, in FilingContext with the member class DocumentShare and the rules 'documents'*");

        FluentActions.Invoking(() => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, OtherContext, Document, DocumentId>(DocumentMembership.Rules))
            .Should().Throw<InvalidOperationException>("another context is another registration");
    }

    [Fact]
    public void Rules_and_a_member_class_belong_to_one_kind_of_resource()
    {
        var services = new ServiceCollection();
        FilingHost.Add(services);

        // Rules under the name another resource's rules have: their codes and their functions would be the same.
        FluentActions.Invoking(() => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Binder, BinderId>(OtherRules("documents")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'documents' are those of Document already*declare a MembershipRules for Binder*");
        FluentActions.Invoking(() => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Binder, BinderId>(DocumentMembership.Rules))
            .Should().Throw<InvalidOperationException>();

        // The member class of another resource, under rules of its own.
        FluentActions.Invoking(() => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Binder, BinderId>(OtherRules("binders")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("DocumentShare is the member class of Document already*");
    }

    [Fact]
    public void Rules_that_need_an_answer_nobody_registered_are_refused_with_what_to_register()
    {
        // Who a member is: resolved by the host, so never read from the caller's own id instead.
        var resolved = new MembershipRules("yards", keys: ["yards.view"], roles: [new("hand", ["yards.view"])], members: MemberSource.Resolved());
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(resolved))
            .Should().Throw<ArgumentException>().WithParameterName("rules")
            .WithMessage("The rules 'yards' have the application resolve who a caller is as a member (MemberSource.Resolved), and nothing answers that for Document: "
                + "no ICallerMember<DocumentId, UserId> is registered. Register one before the resource, services.AddScoped<ICallerMember<DocumentId, UserId>, YourClass>(), "
                + "or hand the class that answers to the resource's registration: services.AddDocumentMembership<TContext, YourClass>(rules).*");

        // Where the roles come from: which of them give a key, and which there are.
        var elsewhere = new MembershipRules("yards", keys: [], ownerRole: "lead", memberKeys: MemberKeys.Only("yards.view"), rolesKeptElsewhere: new());
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId>(elsewhere))
            .Should().Throw<ArgumentException>().WithParameterName("rules")
            .WithMessage("The rules 'yards' say the roles of Crate are kept elsewhere, and nothing answers which of them give a key: no IRolesWithKey<CrateId, DepotRoleId> is registered.*");

        var withoutRoles = new ServiceCollection();
        withoutRoles.AddScoped<IRolesWithKey<CrateId, DepotRoleId>, CratesInTheDepot>();
        FluentActions.Invoking(() => withoutRoles.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId>(elsewhere))
            .Should().Throw<ArgumentException>().WithParameterName("rules")
            .WithMessage("The rules 'yards' say the roles of Crate are kept elsewhere, and nothing answers which roles there are and which is the owner's: no IMemberRoles<CrateId, DepotRoleId> is registered.*");

        // Whether something above reaches the resource: where a caller holds a key.
        var above = new MembershipRules("yards", keys: ["yards.view"], roles: [new("hand", ["yards.view"])], seeKey: "yards.view", above: new());
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(above))
            .Should().Throw<ArgumentException>().WithParameterName("rules")
            .WithMessage("The rules 'yards' let Document be reached from above, and nothing answers where a caller holds a key: no IPlacesReached<DocumentId, TPlaceId> is registered*"
                + "services.AddDocumentMembership<TContext, YourClass>(rules).*");

        // Registered before the resource, each is what the rules need, and nothing more is asked.
        var answered = new ServiceCollection();
        answered.AddScoped<ICallerMember<CrateId, PorterId>, CratesInTheDepot>();
        answered.AddScoped<IRolesWithKey<CrateId, DepotRoleId>, CratesInTheDepot>();
        answered.AddScoped<IMemberRoles<CrateId, DepotRoleId>, CratesInTheDepot>();
        answered.AddScoped<IPlacesReached<CrateId, BayId>, CratesInTheDepot>();
        answered.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId>(CrateMembership.Rules);
        answered.Should().Contain(descriptor => descriptor.ServiceType == typeof(IMemberQuestions<CrateId>));
    }

    [Fact]
    public void The_registration_that_takes_the_hosts_class_registers_every_port_it_answers_for_that_resource()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DepotDesk>();
        services.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId, CratesInTheDepot>(CrateMembership.Rules);

        // One class for a scope, and each port of the crates it implements answered by it.
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(CratesInTheDepot)).Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        Type[] ports =
        [
            typeof(ICallerMember<CrateId, PorterId>), typeof(IRolesWithKey<CrateId, DepotRoleId>), typeof(IMemberRoles<CrateId, DepotRoleId>),
            typeof(IMemberDirectory<CrateId, PorterId>), typeof(IPlacesReached<CrateId, BayId>),
        ];
        foreach (var port in ports)
        {
            services.Should().ContainSingle(descriptor => descriptor.ServiceType == port, port.Name).Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        }

        // What the class does not answer is not registered for it, and no port of another resource is.
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IMemberRolePolicy<CrateId, DepotRoleId>));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ICallerMember<PalletId, PorterId>));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IMemberRoles<CrateId, NamedRole>));

        // Said again, it changes nothing.
        var registered = services.Count;
        services.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId, CratesInTheDepot>(CrateMembership.Rules);
        services.Count.Should().Be(registered);

        // A class that answers nothing of the resource is a mistake, and said so: a pallet's class is not a crate's.
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId, PalletsInTheDepot>(CrateMembership.Rules))
            .Should().Throw<ArgumentException>().WithMessage("PalletsInTheDepot answers nothing of Crate: it implements none of ICallerMember<CrateId, PorterId>*");

        // And a class that answers less than the rules need leaves the rest to be refused as ever.
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId, OnlyWhoCalls>(CrateMembership.Rules))
            .Should().Throw<ArgumentException>().WithMessage("The rules 'crates' say the roles of Crate are kept elsewhere*no IRolesWithKey<CrateId, DepotRoleId> is registered*");
    }

    [Fact]
    public async Task A_port_the_host_registered_before_the_resource_stays()
    {
        // The host answers who can be put on a crate elsewhere than in the class it hands over: registered
        // before, that answer is the one asked, and the class answers the rest.
        using var depot = await SqliteDepot.SeededAsync(services => services.AddSingleton<IMemberDirectory<CrateId, PorterId>>(new NobodyNew()));

        await depot.Services.AsAsync(Caller.System, async provider =>
        {
            provider.GetRequiredService<IMemberDirectory<CrateId, PorterId>>().Should().BeOfType<NobodyNew>();
            provider.GetRequiredService<IMemberRoles<CrateId, DepotRoleId>>().Should().BeOfType<CratesInTheDepot>();
            provider.GetRequiredService<IMemberDirectory<PalletId, PorterId>>().Should().BeOfType<PalletsInTheDepot>("another resource's answer is its own");

            await Refused.WithCodeAsync(
                CrateMembership.Codes,
                MembershipRefusals.MemberNotActive,
                () => provider.GetRequiredService<MemberAdmission<CrateId, PorterId, DepotRoleId>>().RequireMemberAsync(depot.Scenario.Eli.Porter!.Value, Cancellation).AsTask());
        });
    }

    [Fact]
    public async Task One_class_that_answers_for_two_kinds_of_resource_is_handed_to_each_and_is_one_instance_for_a_scope()
    {
        // The host knows its callers in one place, and resolves who a caller is for its pallets and for its
        // documents there: the same class, handed to the registration of each, and each registers its own port.
        var services = new ServiceCollection();
        services.AddMembership<PalletPorter, PalletPorterId, PorterId, NamedRole, DepotContext, Pallet, PalletId, WhoCallsAnywhere>(
            new MembershipRules("pens", keys: ["pens.view"], roles: [new("hand", ["pens.view"])], members: MemberSource.Resolved()));
        services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId, WhoCallsAnywhere>(
            new MembershipRules("papers", keys: ["papers.read"], roles: [new("reader", ["papers.read"])], members: MemberSource.Resolved()));

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(WhoCallsAnywhere)).Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ICallerMember<PalletId, PorterId>));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ICallerMember<DocumentId, UserId>));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        await using var other = provider.CreateAsyncScope();
        var pallets = scope.ServiceProvider.GetRequiredService<ICallerMember<PalletId, PorterId>>();
        pallets.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ICallerMember<DocumentId, UserId>>(), "one instance for a scope answers for both");
        pallets.Should().NotBeSameAs(other.ServiceProvider.GetRequiredService<ICallerMember<PalletId, PorterId>>(), "and each scope has its own");
    }

    [Fact]
    public void Each_of_the_three_things_rules_say_asks_only_for_its_own_answer_whatever_the_others_say()
    {
        // Members that are users, by their own id, with roles kept elsewhere and reach from above; members the
        // host resolves, with roles declared in the rules and nothing above. Whatever the rules say of one,
        // only what they say needs an answer.
        var usersWithKeptRoles = new MembershipRules(
            "yards", keys: [], seeKey: "yards.view", ownerRole: "lead", memberKeys: MemberKeys.Only("yards.view"), rolesKeptElsewhere: new(), above: new());
        var users = new ServiceCollection();
        users.AddScoped<IRolesWithKey<CrateId, DepotRoleId>, CratesInTheDepot>();
        users.AddScoped<IMemberRoles<CrateId, DepotRoleId>, CratesInTheDepot>();
        users.AddScoped<IPlacesReached<CrateId, BayId>, CratesInTheDepot>();
        users.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId>(usersWithKeptRoles);
        users.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ICallerMember<CrateId, PorterId>), "the caller's own id is who it is as a member");

        var claimWithReach = new MembershipRules(
            "lots", keys: ["lots.view"], roles: [new("hand", ["lots.view"])], members: MemberSource.Claim("app_metadata.staff"), seeKey: "lots.view", above: new());
        var claimed = new ServiceCollection();
        claimed.AddScoped<IPlacesReached<FolderId, BayId>, FoldersAtBays>();
        claimed.AddMembership<FolderMember, FolderMemberId, StaffCode, NamedRole, FilingContext, Folder, FolderId>(claimWithReach);

        var resolvedAlone = new MembershipRules("pens", keys: ["pens.view"], roles: [new("hand", ["pens.view"])], members: MemberSource.Resolved());
        var resolved = new ServiceCollection();
        resolved.AddSingleton<DepotDesk>();
        resolved.AddMembership<PalletPorter, PalletPorterId, PorterId, NamedRole, DepotContext, Pallet, PalletId, PalletsInTheDepot>(resolvedAlone);
        resolved.Should().Contain(descriptor => descriptor.ServiceType == typeof(IMemberRoles<PalletId, NamedRole>), "roles the rules declare are answered from the rules");

        // Roles the host keeps may be known by name as well: what a role is known by is not what says where it is kept.
        var namedAndKept = new MembershipRules("papers", keys: [], ownerRole: "lead", memberKeys: MemberKeys.AllBut(), rolesKeptElsewhere: new());
        var named = new ServiceCollection();
        named.AddSingleton<IRolesWithKey<DocumentId, NamedRole>>(new NoRolesGive());
        named.AddSingleton<IMemberRoles<DocumentId, NamedRole>>(new NoRoles());
        named.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(namedAndKept);
        named.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IMemberRoles<DocumentId, NamedRole>));
    }

    [Fact]
    public async Task Rules_that_reach_a_resource_from_above_need_the_model_to_say_where_it_sits()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");

        // A pallet is mapped without a place, and these rules would have it reached from above.
        var rules = new MembershipRules(
            "pens", keys: ["pens.view"], roles: [new("hand", ["pens.view"])], members: MemberSource.Resolved(), seeKey: "pens.view", above: new());
        var services = new ServiceCollection();
        services.AddDbContext<DepotContext>(options => options.UseSqlite(connection));
        services.AddSingleton<DepotDesk>();
        services.AddScoped<IPlacesReached<PalletId, BayId>, PalletsAtBays>();
        services.AddMembership<PalletPorter, PalletPorterId, PorterId, NamedRole, DepotContext, Pallet, PalletId, PalletsInTheDepot>(rules);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using (Callers.Begin(TestCallers.User(UserId.CreateSequential())))
        {
            FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<IMemberQuestions<PalletId>>().Reach("pens.view"))
                .Should().Throw<InvalidOperationException>()
                .WithMessage("The rules 'pens' let Pallet be reached from above, and DepotContext does not say where it sits.*HasMembers(resource => resource.Members, resource => resource.OwnerId, at: resource => resource.PlaceId).");
        }

        // And what answers where a key is held answers for the kind of place the resource sits at: a crate
        // sits at a bay, and an answer about porters is none.
        var misplaced = new ServiceCollection();
        misplaced.AddDbContext<DepotContext>(options => options.UseSqlite(connection));
        misplaced.AddSingleton<DepotDesk>();
        misplaced.AddScoped<IPlacesReached<CrateId, PorterId>, CratesAtPorters>();
        misplaced.AddScoped<IRolesWithKey<CrateId, DepotRoleId>, CratesInTheDepot>();
        misplaced.AddScoped<IMemberRoles<CrateId, DepotRoleId>, CratesInTheDepot>();
        misplaced.AddScoped<ICallerMember<CrateId, PorterId>, CratesInTheDepot>();
        misplaced.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId>(CrateMembership.Rules);

        await using var other = misplaced.BuildServiceProvider();
        await using var asking = other.CreateAsyncScope();
        using (Callers.Begin(TestCallers.User(UserId.CreateSequential())))
        {
            FluentActions.Invoking(() => asking.ServiceProvider.GetRequiredService<IMemberQuestions<CrateId>>().Reach(CrateKeys.Pack))
                .Should().Throw<InvalidOperationException>()
                .WithMessage("Crate sits at a BayId, and nothing answers where a caller holds a key among those: no IPlacesReached<CrateId, BayId> is registered.*");
        }
    }

    [Fact]
    public async Task A_crate_under_rules_that_say_nothing_reaches_it_from_above_is_reached_through_its_members_alone()
    {
        // The same crates, the same depot, the same class that answers; only the rules differ, in one of the
        // three things they say. Where a crate sits is still mapped, and nothing asks about it.
        var membersOnly = new MembershipRules(
            "crates",
            keys: [CrateKeys.Scrap],
            members: MemberSource.Resolved(),
            seeKey: CrateKeys.See,
            ownerRole: CrateMembership.OwnerRole,
            memberKeys: MemberKeys.AllBut(CrateKeys.Move, CrateKeys.ManageDepot),
            rolesKeptElsewhere: new());

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var clock = new FixedClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<DepotDesk>();
        services.AddDbContext<DepotContext>(options => options.UseSqlite(connection));
        services.AddMembership<CratePorter, CratePorterId, PorterId, DepotRoleId, DepotContext, Crate, CrateId, CratesInTheDepot>(membersOnly);
        await using var provider = services.BuildServiceProvider();

        var data = new DepotScenario(clock.Now);
        using (Callers.Begin(Caller.System))
        {
            await using var seeding = provider.CreateAsyncScope();
            var context = seeding.ServiceProvider.GetRequiredService<DepotContext>();
            await context.Database.EnsureCreatedAsync(Cancellation);
            foreach (var porter in data.Porters)
            {
                seeding.ServiceProvider.GetRequiredService<DepotDesk>().TakeOn(porter);
            }

            context.AddRange(data.Porters);
            context.AddRange(data.Roles);
            context.AddRange(data.RoleKeys);
            context.AddRange(data.Bays);
            context.AddRange(data.BayPaths);
            context.AddRange(data.BayHolds);
            context.AddRange(data.Crates);
            await context.SaveChangesAsync(Cancellation);
        }

        async Task<MemberHold<CrateId>?> HoldAsync(DepotPerson person, CrateId crate, string key)
        {
            using (Callers.Begin(person.Caller))
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<IMemberQuestions<CrateId>>().HoldAsync(crate, key, Cancellation);
            }
        }

        // Eli and Flo hold their keys at bays as before, and are on no crate: nothing is there for them.
        (await HoldAsync(data.Eli, data.Tea, CrateKeys.Move)).Should().BeNull();
        (await HoldAsync(data.Flo, data.Salt, CrateKeys.Pack)).Should().BeNull();

        // Bo's packing ends with his role, since nothing above keeps it; the depot's roles still say what he holds.
        var pack = await HoldAsync(data.Bo, data.Tea, CrateKeys.Pack);
        (pack!.Via, pack.Until).Should().Be((MemberVia.Members, data.InThreeDays));
        (await HoldAsync(data.Bo, data.Tea, CrateKeys.Move))!.Via.Should().BeNull();
    }

    [Fact]
    public void A_member_is_known_by_what_its_source_answers()
    {
        // The caller's user id is a Guid, and a folder's staff are known by a text.
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<FolderMember, FolderMemberId, StaffCode, NamedRole, FilingContext, Folder, FolderId>(OtherRules("shelves")))
            .Should().Throw<ArgumentException>()
            .WithMessage("The rules 'shelves' take the caller's member id from MemberSource.CallerId*known by StaffCode, which is not an id over a Guid*");

        // A claim is a text, and a document's members are known by a Guid.
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(OtherRules("papers", MemberSource.Claim("app_metadata.badge"))))
            .Should().Throw<ArgumentException>()
            .WithMessage("The rules 'papers' take the caller's member id from the claim 'app_metadata.badge'*known by UserId, which is not an id over a text*");

        // Roles of the application's own are not the roles a resource's rules declare.
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<SeatedShare, DocumentShareId, UserId, BinderId, FilingContext, Document, DocumentId>(OtherRules("papers")))
            .Should().Throw<ArgumentException>()
            .WithMessage("The members of Document hold roles known by BinderId*[Member<DocumentShareId, UserId, NamedRole, Document>]*");
    }

    [Fact]
    public async Task A_resource_the_context_does_not_map_with_its_members_is_said_when_it_is_first_asked_about()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");

        async Task<Exception> AskedAsync<TContext>(Action<IServiceCollection> register)
            where TContext : DbContext
        {
            var services = new ServiceCollection();
            services.AddDbContext<TContext>(options => options.UseSqlite(connection));
            register(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            using (Callers.Begin(TestCallers.User(UserId.CreateSequential())))
            {
                return FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<IMemberQuestions<DocumentId>>().Reach(DocumentKeys.View))
                    .Should().Throw<InvalidOperationException>().Which;
            }
        }

        (await AskedAsync<OtherContext>(services => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, OtherContext, Document, DocumentId>(DocumentMembership.Rules)))
            .Message.Should().StartWith("OtherContext does not map Document");

        (await AskedAsync<UnmarkedContext>(services => services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, UnmarkedContext, Document, DocumentId>(DocumentMembership.Rules)))
            .Message.Should().StartWith("UnmarkedContext maps Document without its members").And.Contain("HasMembers");

        (await AskedAsync<FilingContext>(services => services.AddMembership<OtherShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(DocumentMembership.Rules)))
            .Message.Should().StartWith("The members of Document are of DocumentShare, and it was registered with the member class OtherShare");
    }

    [Fact]
    public async Task The_admission_of_a_resource_asks_the_hooks_the_host_registered_for_that_resource()
    {
        using var filing = await SqliteFiling.SeededAsync(services =>
        {
            services.AddSingleton<IMemberDirectory<DocumentId, UserId>>(new OnlyKnown());
            services.AddSingleton<IMemberRolePolicy<FolderId, NamedRole>>(new NoVisitors());
        });
        var stranger = UserId.CreateSequential();

        await filing.Services.AsAsync(Caller.System, async provider =>
        {
            var documents = provider.GetRequiredService<MemberAdmission<DocumentId, UserId, NamedRole>>();
            var folders = provider.GetRequiredService<MemberAdmission<FolderId, StaffCode, NamedRole>>();

            // The directory is the documents': a folder has none, and admits everybody.
            (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.MemberNotActive, () => documents.RequireMemberAsync(stranger, Cancellation).AsTask()))
                .Arguments.Should().Contain("Member", stranger);
            await documents.RequireMemberAsync(OnlyKnown.Known, Cancellation);
            await folders.RequireMemberAsync(new StaffCode("N-777"), Cancellation);

            // The policy is the folders': a document's roles all go to a member.
            await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.RoleNotForMembers, () => folders.RequireRoleAsync(FolderMembership.Visitor, Cancellation).AsTask());
            await folders.RequireRoleAsync(FolderMembership.Clerk, Cancellation);
            await documents.RequireRoleAsync(DocumentMembership.Onlooker, Cancellation);
            (await documents.OwnerRoleAsync(Cancellation)).Should().Be(DocumentMembership.Owner);
        });
    }

    [Fact]
    public async Task What_the_host_registered_before_stays()
    {
        // The roles there are for a document's members, as the host says them: registered before, they are the ones asked.
        using var filing = await SqliteFiling.SeededAsync(services => services.AddSingleton<IMemberRoles<DocumentId, NamedRole>>(new NoRoles()));

        await filing.Services.AsAsync(Caller.System, async provider =>
        {
            provider.GetRequiredService<IMemberRoles<DocumentId, NamedRole>>().Should().BeOfType<NoRoles>();
            await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NoOwnerRole, () => provider.GetRequiredService<MemberAdmission<DocumentId, UserId, NamedRole>>().OwnerRoleAsync(Cancellation).AsTask());
            provider.GetRequiredService<IMemberRoles<FolderId, NamedRole>>().Should().BeOfType<NamedRoles<FolderId>>();
            provider.GetRequiredService<TimeProvider>().Should().BeSameAs(filing.Clock, "a clock registered before is the one the questions ask");
        });
    }

    /// <summary>A context that maps nothing.</summary>
    private sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options);

    /// <summary>A context that maps the documents, and never called HasMembers on them.</summary>
    private sealed class UnmarkedContext(DbContextOptions<UnmarkedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Document>().Ignore(document => document.Shares);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A second kind of resource, written by hand: only its types matter here.</summary>
    private sealed class Binder : AggregateRoot<BinderId>;

    private readonly record struct BinderId(Guid Value) : IEntityId;

    /// <summary>A member class whose roles are known by something else than their name.</summary>
    private sealed class SeatedShare : MemberEntity<DocumentShareId, UserId, BinderId>;

    /// <summary>A member class with a document's types that is not the one the document's members are of.</summary>
    private sealed class OtherShare : MemberEntity<DocumentShareId, UserId, NamedRole>;

    private sealed class OnlyKnown : IMemberDirectory<DocumentId, UserId>
    {
        public static UserId Known { get; } = UserId.CreateSequential();

        public ValueTask<bool> IsActiveAsync(UserId member, CancellationToken cancellationToken) => ValueTask.FromResult(member == Known);
    }

    private sealed class NoVisitors : IMemberRolePolicy<FolderId, NamedRole>
    {
        public ValueTask<bool> MayHoldAsync(NamedRole role, CancellationToken cancellationToken) => ValueTask.FromResult(role != FolderMembership.Visitor);
    }

    /// <summary>A class that answers who calls, and nothing else the crates' rules need.</summary>
    private sealed class OnlyWhoCalls : ICallerMember<CrateId, PorterId>
    {
        public PorterId? Find(Caller caller) => null;
    }

    /// <summary>One class that says who a caller is for two kinds of resource, whose members are known by different ids.</summary>
    private sealed class WhoCallsAnywhere : ICallerMember<PalletId, PorterId>, ICallerMember<DocumentId, UserId>
    {
        PorterId? ICallerMember<PalletId, PorterId>.Find(Caller caller) => null;

        UserId? ICallerMember<DocumentId, UserId>.Find(Caller caller) => null;
    }

    /// <summary>A directory that admits nobody.</summary>
    private sealed class NobodyNew : IMemberDirectory<CrateId, PorterId>
    {
        public ValueTask<bool> IsActiveAsync(PorterId member, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    /// <summary>Where a caller holds a key, for folders that would sit at bays.</summary>
    private sealed class FoldersAtBays : IPlacesReached<FolderId, BayId>
    {
        public IQueryable<BayId> PlacesReached(DbContext context, Caller caller, string key) => context.Set<BayPath>().Where(path => false).Select(path => path.BayId);
    }

    /// <summary>Where a caller holds a key, for pallets that would sit at bays.</summary>
    private sealed class PalletsAtBays : IPlacesReached<PalletId, BayId>
    {
        public IQueryable<BayId> PlacesReached(DbContext context, Caller caller, string key) => context.Set<BayPath>().Where(path => false).Select(path => path.BayId);
    }

    /// <summary>An answer about the wrong kind of place: crates sit at bays, and this says at which porters a key is held.</summary>
    private sealed class CratesAtPorters : IPlacesReached<CrateId, PorterId>
    {
        public IQueryable<PorterId> PlacesReached(DbContext context, Caller caller, string key) => context.Set<Porter>().Where(porter => false).Select(porter => porter.Id);
    }

    /// <summary>Roles the host keeps, known by name, none of which gives anything.</summary>
    private sealed class NoRolesGive : IRolesWithKey<DocumentId, NamedRole>
    {
        public IQueryable<NamedRole> RolesWith(DbContext context, Caller caller, string key) => Array.Empty<NamedRole>().AsQueryable();
    }

    private sealed class NoRoles : IMemberRoles<DocumentId, NamedRole>
    {
        public ValueTask<bool> ExistsAsync(NamedRole role, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<NamedRole?> FindOwnerRoleAsync(CancellationToken cancellationToken) => ValueTask.FromResult<NamedRole?>(null);
    }
}
