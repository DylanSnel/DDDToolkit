using System.Reflection;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// What DDDToolkit.Supporting.Membership.EntityFramework's own generator writes into an application that has
/// an organization from the Tenancy packages as well: for each member class whose members are seats, the
/// class that answers what the resource's rules ask of the organization, and a registration named after the
/// resource. Neither package knows the other, so this is the one place they meet, and it is written into the
/// application.
/// <para>
/// Both packages are the real ones, seen through metadata the way an application sees them, and each snippet
/// is compiled with what the generators wrote: the core ones, which close the resource's own registration,
/// and Membership's, which builds on exactly the classes that one was closed over.
/// </para>
/// </summary>
public class MembershipWithTenancyGeneratorTests
{
    /// <summary>The four ids of an organization: what a module that only refers to it knows of it.</summary>
    internal const string TenancyIds =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Campus.Tenants;

        [EntityId<Guid>]
        public readonly partial record struct TenantId;

        [EntityId<Guid>]
        public readonly partial record struct SeatId;

        [EntityId<Guid>]
        public readonly partial record struct OrganizationUnitId;

        [EntityId<Guid>]
        public readonly partial record struct RoleId;
        """;

    /// <summary>The organization itself: Tenancy's classes, declared with its templates under the application's own names.</summary>
    internal const string TenancyClasses =
        """
        using DDDToolkit.Supporting.Tenancy;

        namespace Campus.Tenants;

        [TenantAggregate<TenantId>]
        public sealed partial class Tenant;

        [OrganizationAggregate<TenantId>]
        public sealed partial class Organization;

        [OrganizationUnit<OrganizationUnitId>]
        public sealed partial class OrganizationUnit;

        [SeatAggregate<SeatId>]
        public sealed partial class Seat;

        [RoleAggregate<RoleId>]
        public sealed partial class Role;
        """;

    /// <summary>A course: its members are seats, and its roles are kept for it, rows of a role class of its own.</summary>
    internal const string Courses =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Membership.Access;
        using Campus.Tenants;

        namespace Campus.Courses;

        [EntityId<Guid>]
        public readonly partial record struct CourseId;

        [EntityId<Guid>]
        public readonly partial record struct CourseTutorId;

        [EntityId<Guid>]
        public readonly partial record struct CourseRoleId;

        [Member<CourseTutorId, SeatId, CourseRoleId, Course>]
        public sealed partial class CourseTutor;

        [KeptRole<CourseRoleId, Course>]
        public sealed partial class CourseRole
        {
            public CourseRole(CourseRoleId id, KeptRoleDraft draft, MembershipRules rules) : base(id, draft, rules) { }
        }

        [AggregateRoot<CourseId>]
        public sealed partial class Course
        {
            public Course(CourseId id, SeatId owner, OrganizationUnitId unit) : base(id)
            {
                OwnerSeatId = owner;
                UnitId = unit;
            }

            public SeatId OwnerSeatId { get; private set; }

            public OrganizationUnitId UnitId { get; private set; }

            public partial IReadOnlyList<CourseTutor> Tutors { get; }
        }
        """;

    /// <summary>A lab: its members are seats, and what they hold in it are roles of the tenant's own, by Tenancy's role id.</summary>
    private const string Labs =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;
        using Campus.Tenants;

        namespace Campus.Labs;

        [EntityId<Guid>]
        public readonly partial record struct LabId;

        [EntityId<Guid>]
        public readonly partial record struct LabTechnicianId;

        [Member<LabTechnicianId, SeatId, RoleId, Lab>]
        public sealed partial class LabTechnician;

        [AggregateRoot<LabId>]
        public sealed partial class Lab
        {
            public Lab(LabId id, SeatId owner, OrganizationUnitId unit) : base(id)
            {
                OwnerSeatId = owner;
                UnitId = unit;
            }

            public SeatId OwnerSeatId { get; private set; }

            public OrganizationUnitId UnitId { get; private set; }

            public partial IReadOnlyList<LabTechnician> Technicians { get; }
        }
        """;

    /// <summary>A bulletin: its members are users of an identity provider, in roles its rules declare. No seat is on it.</summary>
    private const string Bulletins =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;

        namespace Campus.Bulletins;

        [EntityId<Guid>]
        public readonly partial record struct UserId;

        [EntityId<Guid>]
        public readonly partial record struct BulletinId;

        [EntityId<Guid>]
        public readonly partial record struct BulletinClerkId;

        [Member<BulletinClerkId, UserId, NamedRole, Bulletin>]
        public sealed partial class BulletinClerk;

        [AggregateRoot<BulletinId>]
        public sealed partial class Bulletin
        {
            public Bulletin(BulletinId id, UserId owner) : base(id) => OwnerId = owner;

            public UserId OwnerId { get; private set; }

            public partial IReadOnlyList<BulletinClerk> Clerks { get; }
        }
        """;

    internal static string Startup(string calls)
        => $$"""
             using DDDToolkit.Supporting.Membership.Access;
             using DDDToolkit.Supporting.Membership.EntityFramework;
             using Microsoft.EntityFrameworkCore;
             using Microsoft.Extensions.DependencyInjection;
             using Campus.Tenants;

             namespace Campus;

             public sealed class CampusContext(DbContextOptions<CampusContext> options) : DbContext(options);

             public static class Startup
             {
                 public static IServiceCollection Register(IServiceCollection services, MembershipRules rules)
                 {
                     {{calls}}
                     return services;
                 }
             }
             """;

    private static string ModuleAttribute(string module) => "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"" + module + "\")]\n";

    private const string Written = "WithTenancy";
    private const string Package = "DDDToolkit.Supporting.Membership.EntityFramework.";
    private const string Extensions = Package + "GeneratedMembershipWithTenancyExtensions";

    /// <summary>An application in one project that declares its organization and <paramref name="resources"/>, and sees both packages.</summary>
    internal static GeneratorTestHost CampusWith(string startup, params (string Source, string Path)[] resources)
    {
        var host = GeneratorTestHost.Create(TenancyIds, "TenancyIds.cs").WithSource(TenancyClasses, "Tenancy.cs");
        foreach (var (source, path) in resources)
        {
            host = host.WithSource(source, path);
        }

        return host.WithSource(startup, "Startup.cs").WithMembership().WithTenancyOnEntityFramework();
    }

    private static GeneratorRunOutcome Run(GeneratorTestHost host) => host.RunCoreAnd(GeneratorTestHost.MembershipGenerators());

    /// <summary>The public methods of a class written into the project, each with its type parameters.</summary>
    private static string[] MethodsOf(GeneratorRunOutcome result, string written)
        => [.. WrittenClass(result, written).GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary && method.DeclaredAccessibility == Accessibility.Public)
            .Select(method => method.Name + "<" + string.Join(", ", method.TypeParameters.Select(parameter => parameter.Name)) + ">")];

    /// <summary>The registrations written for the project, each with its type parameters.</summary>
    private static string[] RegistrationsOf(GeneratorRunOutcome result) => MethodsOf(result, Extensions);

    /// <summary>The ports a written class answers, by their names without type arguments.</summary>
    private static string[] PortsOf(GeneratorRunOutcome result, string written)
        => [.. WrittenClass(result, Package + written).Interfaces.Select(port => port.MetadataName)];

    /// <summary>A class as it is in the project once the generators ran: read from the compilation, so nothing has to be loaded.</summary>
    private static INamedTypeSymbol WrittenClass(GeneratorRunOutcome result, string metadataName)
        => result.OutputCompilation.Assembly.GetTypeByMetadataName(metadataName)
           ?? throw new InvalidOperationException("No type '" + metadataName + "' was written. Generated: " + result.HintNames + ".");

    // ------------------------------------------------------------------ with both

    [Fact]
    public void An_application_with_both_gets_a_registration_named_after_the_resource_and_the_class_that_joins_them()
    {
        var result = Run(CampusWith(Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules);"), (Courses, "Courses.cs")));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        const string File = "GeneratedMembershipWithTenancyExtensions.AddCourseMembershipWithTenancy";
        result.ShouldContain(File, "internal static partial class GeneratedMembershipWithTenancyExtensions");
        result.ShouldContain(
            File,
            """
            public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddCourseMembershipWithTenancy<TContext>(
                    this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    global::DDDToolkit.Supporting.Membership.Access.MembershipRules rules)
                    where TContext : global::Microsoft.EntityFrameworkCore.DbContext
            """,
            "the context is all the application writes: the classes, their ids and the organization's are closed");

        // It registers the resource as its own registration does, closed over the same classes, with the written class as the one that answers.
        result.ShouldContain(
            File,
            """
            MembershipEntityFrameworkServiceCollectionExtensions.AddMembership<
                        global::Campus.Courses.CourseTutor, global::Campus.Courses.CourseTutorId, global::Campus.Tenants.SeatId, global::Campus.Courses.CourseRoleId, TContext, global::Campus.Courses.Course, global::Campus.Courses.CourseId,
                        GeneratedCourseMembershipWithTenancy<TContext>>(services, rules);
            """);

        // Tenancy's four ids are taken from the classes the application declares with Tenancy's templates.
        result.ShouldContain(
            File,
            "ITenancyAnswers<global::Campus.Tenants.TenantId, global::Campus.Tenants.SeatId, global::Campus.Tenants.OrganizationUnitId, global::Campus.Tenants.RoleId> _tenancy;");

        // Every question is forwarded: who the caller is, who can be made a member, and where a key is held.
        result.ShouldContain(File, "return current.Kind == global::DDDToolkit.Supporting.Tenancy.Access.TenancyCallerKind.Seat ? current.Seat : null;");
        result.ShouldContain(File, "=> _tenancy.RequireTenant();");
        result.ShouldContain(File, "seat => seat.Id.Equals(member) && seat.Status == global::DDDToolkit.Supporting.Tenancy.SeatStatus.Active,");
        result.ShouldContain(File, "? tenancy.UnitsWhereIHold(key)");

        RegistrationsOf(result).Should().BeEquivalentTo(["AddCourseMembershipWithTenancy<TContext>"]);
        PortsOf(result, "GeneratedCourseMembershipWithTenancy`1").Should().BeEquivalentTo(
            ["ICallerMember`2", "IMemberDirectory`2", "IPlacesReached`2"],
            "a course's roles are kept for it, so nothing is answered about roles: the package reads them where they are kept");
        result.ShouldNotContain(File, "RolesWithKey");
    }

    [Fact]
    public void A_member_that_holds_roles_by_tenancys_role_id_holds_the_tenants_roles_and_the_class_answers_about_them()
    {
        var result = Run(CampusWith(Startup("services.AddLabMembershipWithTenancy<CampusContext>(rules);"), (Labs, "Labs.cs")));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        const string File = "GeneratedMembershipWithTenancyExtensions.AddLabMembershipWithTenancy";
        PortsOf(result, "GeneratedLabMembershipWithTenancy`1").Should().BeEquivalentTo(
            "ICallerMember`2", "IMemberDirectory`2", "IPlacesReached`2", "IRolesWithKey`2", "IMemberRoles`2");

        // Which roles give a key, which roles there are, and which is the owner's: found by the pack the rules name.
        result.ShouldContain(File, "? tenancy.RolesWithKey(key)");
        result.ShouldContain(File, "row => row.Id.Equals(role) && row.Status == global::DDDToolkit.Supporting.Tenancy.RoleStatus.Active,");
        result.ShouldContain(File, "_ownerRole = rules.OwnerRole;");
        result.ShouldContain(File, "row => row.FromPack == pack && row.Status == global::DDDToolkit.Supporting.Tenancy.RoleStatus.Active");
    }

    [Fact]
    public void Two_resources_in_one_project_each_get_a_registration_and_a_class_of_their_own()
    {
        var result = Run(CampusWith(
            Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules); services.AddLabMembershipWithTenancy<CampusContext>(rules);"),
            (Courses, "Courses.cs"),
            (Labs, "Labs.cs")));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        RegistrationsOf(result).Should().BeEquivalentTo("AddCourseMembershipWithTenancy<TContext>", "AddLabMembershipWithTenancy<TContext>");
        result.GeneratedSources.Select(source => source.HintName).Where(name => name.Contains(Written, StringComparison.Ordinal)).Should().BeEquivalentTo(
            ["GeneratedMembershipWithTenancyExtensions.AddCourseMembershipWithTenancy.g.cs", "GeneratedMembershipWithTenancyExtensions.AddLabMembershipWithTenancy.g.cs"],
            "one file for each member class");

        // Each is about its own resource, and neither knows the other's.
        var course = result.Source("AddCourseMembershipWithTenancy");
        var lab = result.Source("AddLabMembershipWithTenancy");
        course.Should().NotContain("Lab");
        lab.Should().NotContain("Course");
        PortsOf(result, "GeneratedCourseMembershipWithTenancy`1").Should().HaveCount(3);
        PortsOf(result, "GeneratedLabMembershipWithTenancy`1").Should().HaveCount(5);

        // The resources' own registrations are there as they were: the written ones stand beside them.
        MethodsOf(result, Package + "GeneratedMembershipEntityFrameworkServiceCollectionExtensions").Should().BeEquivalentTo(
            "AddCourseMembership<TContext>", "AddCourseMembership<TContext, TPorts>", "AddCourseMemberAccess<TRequests>",
            "AddLabMembership<TContext>", "AddLabMembership<TContext, TPorts>", "AddLabMemberAccess<TRequests>");
    }

    // ------------------------------------------------------------------ with one

    [Fact]
    public void Nothing_is_written_without_tenancy()
    {
        // The same course, in an application that has no organization: its seat id is an id like any other.
        var alone = Run(GeneratorTestHost.Create(TenancyIds, "Ids.cs")
            .WithSource(Courses, "Courses.cs")
            .WithSource(Startup("services.AddCourseMembership<CampusContext>(rules);"), "Startup.cs")
            .WithMembership());

        alone.ShouldCompile();
        alone.ReportedDiagnostics.Should().BeEmpty();
        alone.HintNames.Should().NotContain(Written);

        // And in one that references Tenancy without its Entity Framework package: its questions could not be asked
        // over a context there, so there is nothing to write the class with.
        var withoutItsStorage = Run(GeneratorTestHost.Create(TenancyIds, "Ids.cs")
            .WithSource(Courses, "Courses.cs")
            .WithSource(Startup("services.AddCourseMembership<CampusContext>(rules);"), "Startup.cs")
            .WithMembership()
            .WithTenancy());

        withoutItsStorage.ShouldCompile();
        withoutItsStorage.HintNames.Should().NotContain(Written);
    }

    [Fact]
    public void Nothing_is_written_without_membership()
    {
        var result = Run(GeneratorTestHost.Create(TenancyIds, "TenancyIds.cs").WithSource(TenancyClasses, "Tenancy.cs").WithTenancyOnEntityFramework());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().NotContain(Written);
    }

    // ------------------------------------------------------------------ ids that do not fit

    [Fact]
    public void A_member_class_whose_members_are_not_seats_gets_nothing()
    {
        // An application with an organization, and a bulletin written by users of its identity provider.
        var result = Run(CampusWith(Startup("services.AddBulletinMembership<CampusContext>(rules);"), (Bulletins, "Bulletins.cs")));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().NotContain(Written, "a member of a bulletin is known by a user id, which is not the seat's");
    }

    [Fact]
    public void Beside_a_resource_whose_members_are_seats_one_whose_members_are_not_still_gets_nothing()
    {
        var result = Run(CampusWith(
            Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules); services.AddBulletinMembership<CampusContext>(rules);"),
            (Courses, "Courses.cs"),
            (Bulletins, "Bulletins.cs")));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        RegistrationsOf(result).Should().BeEquivalentTo(["AddCourseMembershipWithTenancy<TContext>"]);
        result.ShouldNotHaveGeneratedFor("GeneratedBulletinMembershipWithTenancy");
    }

    [Fact]
    public void An_organization_that_is_declared_in_part_or_twice_gets_nothing()
    {
        // Which id is the seat's cannot be told where two classes are declared seats. What is wrong with the organization
        // is reported where its own classes are closed, by the generator that closes them, and not a second time here.
        var twoSeats = Run(CampusWith(
            Startup("services.AddCourseMembership<CampusContext>(rules);"),
            (Courses, "Courses.cs"),
            ("""
             using System;
             using DDDToolkit.Abstractions.Attributes;
             using DDDToolkit.Supporting.Tenancy;

             namespace Campus.Tenants;

             [EntityId<Guid>]
             public readonly partial record struct GuestSeatId;

             [SeatAggregate<GuestSeatId>]
             public sealed partial class GuestSeat;
             """, "GuestSeat.cs")));

        twoSeats.HintNames.Should().NotContain(Written);
        twoSeats.GeneratorExceptions.Should().BeEmpty();

        var inPart = Run(GeneratorTestHost.Create(TenancyIds, "TenancyIds.cs")
            .WithSource(
                """
                using DDDToolkit.Supporting.Tenancy;

                namespace Campus.Tenants;

                [TenantAggregate<TenantId>]
                public sealed partial class Tenant;
                """,
                "Tenant.cs")
            .WithSource(Courses, "Courses.cs")
            .WithSource(Startup("services.AddCourseMembership<CampusContext>(rules);"), "Startup.cs")
            .WithMembership()
            .WithTenancyOnEntityFramework());

        inPart.HintNames.Should().NotContain(Written, "a tenant without seats, units and roles is no organization to join a resource to");
        inPart.GeneratorExceptions.Should().BeEmpty();
    }

    [Fact]
    public void A_member_class_its_own_registration_refuses_gets_nothing_and_is_reported_once()
    {
        // Two member classes of one resource are refused by the generator that writes the resource's registration.
        // This one builds on what that one closed, so it writes nothing for them and says nothing of its own.
        const string Watchers =
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;
            using Campus.Tenants;

            namespace Campus.Courses;

            [EntityId<Guid>]
            public readonly partial record struct CourseWatcherId;

            [Member<CourseWatcherId, SeatId, CourseRoleId, Course>]
            public sealed partial class CourseWatcher;
            """;

        var host = CampusWith(Startup(string.Empty), (Courses, "Courses.cs"), (Watchers, "Watchers.cs"));
        var result = Run(host);

        result.ShouldHaveDiagnostic("DDD00045", at: "CourseWatcher");
        result.Count("DDD00045").Should().Be(host.RunCore().Count("DDD00045"), "the refusal is the other generator's to report");
        result.HintNames.Should().NotContain(Written);
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    // ------------------------------------------------------------------ a module that knows the organization by its ids

    [Fact]
    public void A_module_that_knows_the_organization_only_by_its_ids_names_them_where_it_registers()
    {
        // The organization's classes are another module's, which this one does not reference: it has the ids, from that
        // module's contracts, and nothing that says which of them is Tenancy's tenant, unit or role. A member is a seat
        // all the same, so the registration is written, and leaves those three for the application to name.
        var result = Run(GeneratorTestHost.Create(Courses, "Courses.cs")
            .WithSource(Labs, "Labs.cs")
            .WithSource(
                Startup(
                    """
                    services.AddCourseMembershipWithTenancy<CampusContext, TenantId, OrganizationUnitId, RoleId>(rules);
                            services.AddLabMembershipWithTenancy<CampusContext, TenantId, OrganizationUnitId>(rules);
                    """),
                "Startup.cs")
            .WithReferencedAssembly(TenancyIds, "Campus.Tenants.Contracts")
            .WithMembership()
            .WithTenancyOnEntityFramework());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();

        RegistrationsOf(result).Should().BeEquivalentTo(
            ["AddCourseMembershipWithTenancy<TContext, TTenantId, TUnitId, TRoleId>", "AddLabMembershipWithTenancy<TContext, TTenantId, TUnitId>"],
            "a lab's members hold a role by an id that is no role class of a resource's, so it is Tenancy's role id, and is not asked for again");

        const string Course = "GeneratedMembershipWithTenancyExtensions.AddCourseMembershipWithTenancy";
        result.ShouldContain(Course, "where TTenantId : struct, global::DDDToolkit.Abstractions.Interfaces.IEntityId, global::System.IEquatable<TTenantId>");
        result.ShouldContain(Course, "ITenancyAnswers<TTenantId, global::Campus.Tenants.SeatId, TUnitId, TRoleId> _tenancy;", "the seat is the member, whatever the others are");
        result.ShouldContain(Course, "IPlacesReached<global::Campus.Courses.CourseId, TUnitId>");
        result.ShouldContain(
            Course,
            "/// <typeparam name=\"TUnitId\">The id of the application's organization unit class, which this project sees no class of and so cannot be given.</typeparam>",
            "whoever writes the call is told which id goes where");
        PortsOf(result, "GeneratedCourseMembershipWithTenancy`4").Should().HaveCount(3, "a course's roles are kept for it: its role class says so");

        const string Lab = "GeneratedMembershipWithTenancyExtensions.AddLabMembershipWithTenancy";
        result.ShouldContain(Lab, "ITenancyAnswers<TTenantId, global::Campus.Tenants.SeatId, TUnitId, global::Campus.Tenants.RoleId> _tenancy;");
        PortsOf(result, "GeneratedLabMembershipWithTenancy`3").Should().HaveCount(5);
    }

    [Fact]
    public void A_module_whose_members_hold_roles_by_name_names_tenancys_role_id_as_well()
    {
        const string Clubs =
            """
            using System;
            using System.Collections.Generic;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Membership;
            using Campus.Tenants;

            namespace Campus.Clubs;

            [EntityId<Guid>]
            public readonly partial record struct ClubId;

            [EntityId<Guid>]
            public readonly partial record struct ClubMemberId;

            [Member<ClubMemberId, SeatId, NamedRole, Club>]
            public sealed partial class ClubMember;

            [AggregateRoot<ClubId>]
            public sealed partial class Club
            {
                public Club(ClubId id, SeatId owner) : base(id) => OwnerSeatId = owner;

                public SeatId OwnerSeatId { get; private set; }

                public partial IReadOnlyList<ClubMember> Members { get; }
            }
            """;

        var result = Run(GeneratorTestHost.Create(Clubs, "Clubs.cs")
            .WithSource(Startup("services.AddClubMembershipWithTenancy<CampusContext, TenantId, OrganizationUnitId, RoleId>(rules);"), "Startup.cs")
            .WithReferencedAssembly(TenancyIds, "Campus.Tenants.Contracts")
            .WithMembership()
            .WithTenancyOnEntityFramework());

        result.ShouldCompile();
        RegistrationsOf(result).Should().BeEquivalentTo(["AddClubMembershipWithTenancy<TContext, TTenantId, TUnitId, TRoleId>"]);
        PortsOf(result, "GeneratedClubMembershipWithTenancy`4").Should().HaveCount(3, "roles the rules declare are held by name, and are nobody's to answer");
    }

    [Fact]
    public void Such_a_module_cannot_be_told_a_seat_from_another_id_so_each_member_class_is_offered_the_registration()
    {
        // Without the seat class in sight, nothing says whether a member id is the seat's. The registration is there for
        // each member class, and takes its member id for the seat's: the application calls it where the members are
        // seats, and the resource's own registration where they are not, as the bulletin's clerks, who are users.
        var result = Run(GeneratorTestHost.Create(Courses, "Courses.cs")
            .WithSource(Bulletins, "Bulletins.cs")
            .WithSource(
                Startup(
                    """
                    services.AddCourseMembershipWithTenancy<CampusContext, TenantId, OrganizationUnitId, RoleId>(rules);
                            services.AddBulletinMembership<CampusContext>(rules);
                    """),
                "Startup.cs")
            .WithReferencedAssembly(TenancyIds, "Campus.Tenants.Contracts")
            .WithMembership()
            .WithTenancyOnEntityFramework());

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        RegistrationsOf(result).Should().BeEquivalentTo(
            "AddCourseMembershipWithTenancy<TContext, TTenantId, TUnitId, TRoleId>", "AddBulletinMembershipWithTenancy<TContext, TTenantId, TUnitId, TRoleId>");

        // Called for a resource whose members are no seats, it would ask for answers of Tenancy's about a seat known by
        // a user's id, which no application registers: a mistake that is found when the first question is asked.
        result.ShouldContain(
            "AddBulletinMembershipWithTenancy",
            "ITenancyAnswers<TTenantId, global::Campus.Bulletins.UserId, TUnitId, TRoleId> _tenancy;");
    }

    [Fact]
    public void What_is_written_for_such_a_module_is_what_an_application_gets_with_the_ids_it_names_in_their_place()
    {
        // What an application that declares its organization gets is asked against a database, by the suite of both
        // domains. A module's is held to it here: the same class and the same registration, line for line, once the
        // ids the module names are put where the application's own stand. Nothing else may differ between the two.
        var application = Run(CampusWith(Startup(string.Empty), (Courses, "Courses.cs"), (Labs, "Labs.cs")));
        var module = Run(GeneratorTestHost.Create(Courses, "Courses.cs")
            .WithSource(Labs, "Labs.cs")
            .WithSource(Startup(string.Empty), "Startup.cs")
            .WithReferencedAssembly(TenancyIds, "Campus.Tenants.Contracts")
            .WithMembership()
            .WithTenancyOnEntityFramework());

        application.ShouldCompile();
        module.ShouldCompile();

        WithTheIdsNamed(module.Source("AddCourseMembershipWithTenancy"), "TTenantId", "TUnitId", "TRoleId")
            .Should().Be(application.Source("AddCourseMembershipWithTenancy"));
        WithTheIdsNamed(module.Source("AddLabMembershipWithTenancy"), "TTenantId", "TUnitId")
            .Should().Be(application.Source("AddLabMembershipWithTenancy"));
    }

    /// <summary>
    /// What was written for a module, with the ids it leaves for the application to name closed over the campus'
    /// own: the type parameters taken out of every list, with their constraints, and each replaced by the id.
    /// </summary>
    private static string WithTheIdsNamed(string written, params string[] left)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TTenantId"] = "global::Campus.Tenants.TenantId",
            ["TUnitId"] = "global::Campus.Tenants.OrganizationUnitId",
            ["TRoleId"] = "global::Campus.Tenants.RoleId",
        };

        written.Should().Contain("<TContext, " + string.Join(", ", left) + ">", "these are the ids the module names, in this order");
        var closed = written.Replace("<TContext, " + string.Join(", ", left) + ">", "<TContext>", StringComparison.Ordinal);
        var lines = closed.Split('\n').Where(line => !left.Any(parameter =>
            line.TrimStart().StartsWith("where " + parameter + " :", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("/// <typeparam name=\"" + parameter + "\">", StringComparison.Ordinal)));
        closed = string.Join('\n', lines);

        foreach (var parameter in left)
        {
            closed = System.Text.RegularExpressions.Regex.Replace(closed, @"\b" + parameter + @"\b", ids[parameter]);
        }

        return closed;
    }

    // ------------------------------------------------------------------ a module in layers

    private static GeneratorTestHost DomainProject(GeneratorTestHost project)
        => project
            .WithSource(ModuleAttribute("Campus"), "Module.cs")
            .WithSource(TenancyIds, "TenancyIds.cs")
            .WithSource(TenancyClasses, "Tenancy.cs")
            .WithSource(Courses, "Courses.cs");

    [Fact]
    public void In_a_module_split_by_layer_the_project_that_holds_the_context_gets_them()
    {
        // The domain project declares the organization and the course, and knows neither package's storage.
        var domain = Run(DomainProject(GeneratorTestHost.Create(string.Empty, "Empty.cs").WithTenancy().WithMembershipAlone()));
        domain.ShouldCompile();
        domain.HintNames.Should().NotContain(Written, "a project that cannot register the resource is not asked to join it to anything");

        // The infrastructure project declares none of the classes and holds the context: it gets the course's own
        // registration, closed over the domain project's classes, and with it the one that joins it to the organization.
        var infrastructure = Run(GeneratorTestHost.Create(Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules);"), "Startup.cs")
            .WithSource(ModuleAttribute("Campus"), "Module.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedProject("Campus.Domain", DomainProject)
            .WithMembership()
            .WithTenancyOnEntityFramework());

        infrastructure.ShouldCompile();
        infrastructure.ReportedDiagnostics.Should().BeEmpty();
        RegistrationsOf(infrastructure).Should().BeEquivalentTo(["AddCourseMembershipWithTenancy<TContext>"]);
        infrastructure.ShouldContain(
            "AddCourseMembershipWithTenancy",
            "ITenancyAnswers<global::Campus.Tenants.TenantId, global::Campus.Tenants.SeatId, global::Campus.Tenants.OrganizationUnitId, global::Campus.Tenants.RoleId> _tenancy;",
            "the organization's ids are taken from the classes the domain project declares");
    }

    [Fact]
    public void A_project_above_the_one_that_holds_the_context_gets_nothing()
    {
        // The API project composes the module: it references the infrastructure project, and through it sees the classes
        // and both packages. What joins the course to the organization is in the project below it already.
        var result = Run(GeneratorTestHost.Create(
                """
                using DDDToolkit.Supporting.Membership.Access;
                using Microsoft.Extensions.DependencyInjection;

                namespace Campus.Api;

                public static class Entry
                {
                    public static IServiceCollection Register(IServiceCollection services, MembershipRules rules) => Campus.Startup.Register(services, rules);
                }
                """,
                "Entry.cs")
            .WithSource(ModuleAttribute("Campus"), "Module.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedProject("Campus.Domain", DomainProject)
            .WithMembership()
            .WithTenancyOnEntityFramework()
            .WithReferencedProject(
                "Campus.Infrastructure",
                project => project
                    .WithSource(ModuleAttribute("Campus"), "Module.cs")
                    .WithSource(Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules);"), "Startup.cs"),
                GeneratorTestHost.MembershipGenerators()));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().NotContain(Written);
    }

    // ------------------------------------------------------------------ what the registration does when it is called

    private static MembershipRules CourseRules(MemberSource? members)
        => new(
            "courses",
            keys: ["courses.see", "courses.grade"],
            roles: [new("tutor", ["courses.see", "courses.grade"])],
            members: members,
            seeKey: "courses.see",
            above: new("tenancy/units_where_i_hold"),
            rolesKept: true);

    [Fact]
    public void The_registration_registers_the_resource_with_the_class_that_answers_for_every_port_it_implements()
    {
        var emitted = Run(CampusWith(Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules);"), (Courses, "Courses.cs"))).Emit();
        var services = new ServiceCollection();
        var rules = CourseRules(MemberSource.Resolved("tenancy/caller_seat"));

        emitted.CallStatic("Campus.Startup", "Register", services, rules);

        var written = emitted.Type(Package + "GeneratedCourseMembershipWithTenancy`1").MakeGenericType(emitted.Type("Campus.CampusContext"));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == written).Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        services.Where(descriptor => written.GetInterfaces().Contains(descriptor.ServiceType)).Select(descriptor => descriptor.ServiceType.Name)
            .Should().BeEquivalentTo(
                ["ICallerMember`2", "IMemberDirectory`2", "IPlacesReached`2"],
                "what the rules ask of the organization is answered by the written class, one for a scope");
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IMemberQuestions<>).MakeGenericType(emitted.Type("Campus.Courses.CourseId")));

        // Said twice it is said once, as for the resource's own registration.
        var registered = services.Count;
        emitted.CallStatic("Campus.Startup", "Register", services, rules);
        services.Should().HaveCount(registered);
    }

    [Fact]
    public void The_registration_of_a_module_closes_the_class_over_the_ids_it_is_given()
    {
        // The ids are declared here and the organization's classes are not: what a module sees whose organization is another's.
        var emitted = Run(GeneratorTestHost.Create(TenancyIds, "TenancyIds.cs")
            .WithSource(Courses, "Courses.cs")
            .WithSource(Startup("services.AddCourseMembershipWithTenancy<CampusContext, TenantId, OrganizationUnitId, RoleId>(rules);"), "Startup.cs")
            .WithMembership()
            .WithTenancyOnEntityFramework()).Emit();
        var services = new ServiceCollection();

        emitted.CallStatic("Campus.Startup", "Register", services, CourseRules(MemberSource.Resolved("tenancy/caller_seat")));

        var written = emitted.Type(Package + "GeneratedCourseMembershipWithTenancy`4").MakeGenericType(
            emitted.Type("Campus.CampusContext"), emitted.Type("Campus.Tenants.TenantId"), emitted.Type("Campus.Tenants.OrganizationUnitId"), emitted.Type("Campus.Tenants.RoleId"));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == written);

        // Where a course sits is known by the unit id the application named, which is what the resource is asked about.
        var above = typeof(DDDToolkit.Supporting.Membership.EntityFramework.IPlacesReached<,>)
            .MakeGenericType(emitted.Type("Campus.Courses.CourseId"), emitted.Type("Campus.Tenants.OrganizationUnitId"));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == above);
        written.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal(typeof(IServiceProvider), typeof(MembershipRules));
    }

    [Fact]
    public void The_registration_refuses_rules_that_take_a_member_from_the_caller_itself()
    {
        // A seat's id is over the same value as a user's, so rules that take the member from the caller's own id would be
        // accepted by the resource's own registration, and read a user for a seat. Here that is a mistake, and said.
        var emitted = Run(CampusWith(Startup("services.AddCourseMembershipWithTenancy<CampusContext>(rules);"), (Courses, "Courses.cs"))).Emit();

        foreach (var (members, said) in new[] { ((MemberSource?)null, "MemberSource.CallerId"), (MemberSource.Claim("app_metadata.seat"), "MemberSource.Claim") })
        {
            var services = new ServiceCollection();

            FluentActions.Invoking(() => emitted.CallStatic("Campus.Startup", "Register", services, CourseRules(members)))
                .Should().Throw<TargetInvocationException>()
                .WithInnerException<ArgumentException>()
                .WithMessage("The rules 'courses' take a member from the caller itself (" + said + "), and AddCourseMembershipWithTenancy registers Course for members that are "
                             + "seats of a tenant, which no token carries. Say members: MemberSource.Resolved(\"tenancy/caller_seat\") in the rules, or register Course with "
                             + "AddCourseMembership where its members are not seats.*");
            services.Should().BeEmpty("nothing is registered for rules that are refused");
        }
    }
}
