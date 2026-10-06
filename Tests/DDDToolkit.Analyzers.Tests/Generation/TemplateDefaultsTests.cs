using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A package's switch, <c>[assembly: GenerateTenancyClasses]</c>, has the generator write the package's classes a
/// project leaves out, as the package ships them, and their ids. Nothing is written without it, and whatever the
/// project declares itself wins.
/// <para>
/// A generator never sees what another writes, so the classes and ids written here get everything only because every
/// provider the other generators read hands them on beside the declared ones. These tests run on the real Tenancy
/// package, seen through metadata as an application sees it, and compile every project with what all the generators
/// wrote: a class or an id that misses a part of what a declared one gets fails to compile there. A module split by
/// layer is compiled project by project, each above the one below it, as a build compiles it.
/// </para>
/// </summary>
public class TemplateDefaultsTests
{
    /// <summary>The switch, and nothing else: the shortest start.</summary>
    internal const string Switch =
        """
        [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]
        """;

    /// <summary>The switch of a module's contracts project: the ids alone.</summary>
    private const string IdsSwitch =
        """
        [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyIds]
        """;

    /// <summary>The classes the switch writes, and their ids, by name.</summary>
    private static readonly string[] Classes = ["Tenant", "Organization", "OrganizationUnit", "Role", "Seat"];

    private static readonly string[] Ids = ["TenantId", "OrganizationUnitId", "RoleId", "SeatId"];

    /// <summary>The fragment of the hint name of what the switch writes for <paramref name="name"/>.</summary>
    private static string Written(string name) => name + ".TemplateDefault.";

    /// <summary>A project of one file with the switch in it, and Tenancy.</summary>
    private static GeneratorTestHost Project(string source = Switch) => GeneratorTestHost.Create(source).WithTenancy();

    /// <summary>A type of the compilation after generation, by its metadata name.</summary>
    private static INamedTypeSymbol TypeIn(GeneratorRunOutcome result, string metadataName)
        => result.OutputCompilation.GetTypeByMetadataName(metadataName)
           ?? throw new InvalidOperationException("'" + metadataName + "' is not in the compilation.\n" + result.HintNames);

    /// <summary>The class a type derives from, as C# writes it.</summary>
    private static string ParentOf(GeneratorRunOutcome result, string metadataName) => TypeIn(result, metadataName).BaseType!.ToDisplayString();

    /// <summary>
    /// What the switch writes and what it leaves out compile, and the switch's own error, DDD00066, is the only one: no
    /// generator threw over what it could not write, two ids of one name say, and none wrote code that fails.
    /// </summary>
    private static void CompilesBesidesWhatItSays(GeneratorRunOutcome result)
    {
        result.ShouldNotCrash();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(static diagnostic => diagnostic.Id)
            .Should().OnlyContain(static id => id == "DDD00066");
    }

    // ------------------------------------------------------------------ a single project

    [Fact]
    public void The_switch_alone_writes_every_class_and_id_and_the_project_compiles()
    {
        var result = Project().RunCore();

        result.ShouldCompile();
        foreach (var name in Classes.Concat(Ids))
        {
            result.ShouldHaveGenerated(Written(name));
        }

        result.HintNames.Should().NotContain(Written("Invitation"), "the invitation is the class an application may leave out, and the switch leaves it out");
        result.HintNames.Should().NotContain(Written("OrganizationId"), "an organization shares its tenant's id");

        ParentOf(result, "DDDToolkit.Sample.Tenant").Should().Be("DDDToolkit.Supporting.Tenancy.TenantAggregate<DDDToolkit.Sample.TenantId>");
        ParentOf(result, "DDDToolkit.Sample.Organization")
            .Should().Be("DDDToolkit.Supporting.Tenancy.OrganizationAggregate<DDDToolkit.Sample.TenantId, DDDToolkit.Sample.OrganizationUnit, DDDToolkit.Sample.OrganizationUnitId>");
        ParentOf(result, "DDDToolkit.Sample.Seat")
            .Should().Be("DDDToolkit.Supporting.Tenancy.SeatAggregate<DDDToolkit.Sample.SeatId, DDDToolkit.Sample.TenantId, DDDToolkit.Sample.OrganizationUnitId, DDDToolkit.Sample.RoleId>");

        var tenantId = TypeIn(result, "DDDToolkit.Sample.TenantId");
        tenantId.IsValueType.Should().BeTrue();
        tenantId.AllInterfaces.Select(static type => type.ToDisplayString()).Should().Contain("DDDToolkit.Abstractions.Interfaces.IEntityId");
        tenantId.GetAttributes().Select(static attribute => attribute.AttributeClass!.Name)
            .Should().Contain("ModuleContractAttribute", "an id is what other modules store, so it is published");
    }

    [Fact]
    public void Nothing_is_written_without_the_switch()
    {
        var result = Project("namespace Shop; public sealed class Unrelated;").RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain(".TemplateDefault.");
    }

    [Fact]
    public void What_the_switch_writes_says_so_and_how_to_declare_it_yourself()
    {
        var result = Project().RunCore();

        result.ShouldContain(Written("Seat"), "because this project says <c>[assembly: GenerateTenancyClasses]</c>");
        result.ShouldContain(Written("Seat"), "[SeatAggregate&lt;SeatId&gt;]\n/// public sealed partial class Seat");
        result.ShouldContain(Written("Seat"), "[global::DDDToolkit.Supporting.Tenancy.SeatAggregateAttribute<global::DDDToolkit.Sample.SeatId>]\npublic sealed partial class Seat");
        result.ShouldContain(Written("TenantId"), "The id of the classes declared with <c>[TenantAggregate]</c> and <c>[OrganizationAggregate]</c>");
        result.ShouldContain(Written("TenantId"), "[global::DDDToolkit.Abstractions.Attributes.EntityId<global::System.Guid>]\npublic readonly partial record struct TenantId;");
    }

    [Fact]
    public void What_the_switch_writes_goes_in_the_projects_root_namespace()
    {
        var result = Project().WithBuildProperty("RootNamespace", "Shop").RunCore();

        result.ShouldCompile();
        ParentOf(result, "Shop.Role").Should().Be("DDDToolkit.Supporting.Tenancy.RoleAggregate<Shop.RoleId, Shop.TenantId>");
    }

    [Fact]
    public void A_written_class_is_made_and_put_in_use_the_way_a_declared_one_is()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Provisioning;

            using DDDToolkit.Supporting.Tenancy;

            public static class FirstTenant
            {
                public static Tenant Make()
                {
                    var tenant = TenancyInstances.NewTenant<Tenant, TenantId, SeatId>(TenantId.CreateSequential(), TenantSlug.Create("acme").ToValid(), TenantShape.Flat);
                    tenant.Activate<SeatId>();
                    return tenant;
                }
            }
            """;

        var assembly = Project(source).RunCore().Emit();

        var tenant = assembly.CallStatic("DDDToolkit.Sample.Provisioning.FirstTenant", "Make")!;
        tenant.GetType().FullName.Should().Be("DDDToolkit.Sample.Tenant");
        assembly.Property(tenant, "IsActive").Should().Be(true);
        assembly.Property(assembly.Property(tenant, "Slug")!, "Value").Should().Be("acme");

        var id = assembly.CallStatic("DDDToolkit.Sample.SeatId", "CreateSequential")!;
        assembly.TryParse("DDDToolkit.Sample.SeatId", id.ToString()).Value.Should().Be(id, "a written id parses what it prints, as a declared one does");
    }

    // ------------------------------------------------------------------ what the project declares wins

    [Fact]
    public void A_class_the_project_declares_is_not_written_and_takes_the_id_the_switch_writes()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Seats;

            using DDDToolkit.Supporting.Tenancy;

            [SeatAggregate<SeatId>]
            public sealed partial class Seat
            {
                public string? JobTitle { get; private set; }

                public void ChangeJobTitle(string? jobTitle) => JobTitle = jobTitle;
            }

            public static class FirstSeat
            {
                public static Seat Make()
                {
                    var seat = TenancyInstances.NewSeat<Seat, SeatId, TenantId, OrganizationUnitId, RoleId>(SeatId.CreateSequential(), TenantId.CreateSequential(), System.Guid.NewGuid(), "Ada");
                    seat.ChangeJobTitle("Surveyor");
                    return seat;
                }
            }
            """;

        var result = Project(source).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain(Written("Seat"), "the project's own seat wins");
        result.ShouldHaveGenerated(Written("SeatId"));
        ParentOf(result, "DDDToolkit.Sample.Seats.Seat")
            .Should().Be("DDDToolkit.Supporting.Tenancy.SeatAggregate<DDDToolkit.Sample.SeatId, DDDToolkit.Sample.TenantId, DDDToolkit.Sample.OrganizationUnitId, DDDToolkit.Sample.RoleId>");
        result.OutputCompilation.GetTypeByMetadataName("DDDToolkit.Sample.Seat").Should().BeNull("no second seat is written beside the project's own");

        // And it runs: the package's seat, made through the package, with the project's own field on it.
        var assembly = result.Emit();
        var seat = assembly.CallStatic("DDDToolkit.Sample.Seats.FirstSeat", "Make")!;
        assembly.Property(seat, "JobTitle").Should().Be("Surveyor");
        assembly.Property(seat, "DisplayName").Should().Be("Ada");
    }

    [Fact]
    public void An_id_the_project_declares_is_not_written_and_the_classes_take_it()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Contracts;

            using DDDToolkit.Abstractions.Attributes;

            [EntityId<long>("TEN")]
            public readonly partial record struct TenantId;
            """;

        var result = Project(source).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain(Written("TenantId"));
        ParentOf(result, "DDDToolkit.Sample.Tenant").Should().Be("DDDToolkit.Supporting.Tenancy.TenantAggregate<DDDToolkit.Sample.Contracts.TenantId>");
        ParentOf(result, "DDDToolkit.Sample.Role").Should().Be("DDDToolkit.Supporting.Tenancy.RoleAggregate<DDDToolkit.Sample.RoleId, DDDToolkit.Sample.Contracts.TenantId>");
    }

    [Fact]
    public void A_class_written_beside_one_the_project_declares_shares_that_ones_id()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Tenants;

            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Tenancy;

            [EntityId<Guid>]
            public readonly partial record struct ShopTenantId;

            [TenantAggregate<ShopTenantId>]
            public sealed partial class ShopTenant
            {
                public bool IsDemo { get; private set; }
            }
            """;

        var result = Project(source).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain(Written("Tenant")).And.NotContain(Written("TenantId"), "the tenant's id is the one the project's tenant is declared with");
        ParentOf(result, "DDDToolkit.Sample.Organization")
            .Should().Be("DDDToolkit.Supporting.Tenancy.OrganizationAggregate<DDDToolkit.Sample.Tenants.ShopTenantId, DDDToolkit.Sample.OrganizationUnit, DDDToolkit.Sample.OrganizationUnitId>");
    }

    // ------------------------------------------------------------------ what every generator writes for them

    [Fact]
    public void The_written_classes_and_ids_get_their_converters_registrations_and_the_class_the_use_cases_are_named_through()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Persistence;

            using DDDToolkit.EntityFramework.Conventions;
            using DDDToolkit.Supporting.Tenancy.EntityFramework;
            using DDDToolkit.Sample.Converters;
            using Microsoft.EntityFrameworkCore;

            public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTenancy(database: Database);

                protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.AddDDDToolkitConventions();
                    configurationBuilder.AddShopConverters();
                }
            }

            public sealed class Handler(ShopTenancy.SeatCommands seats)
            {
                public ShopTenancy.SeatCommands Seats => seats;

                public ShopTenancy.SeatOverview? Last { get; set; }
            }
            """;

        var result = Project(source)
            .WithTenancyOnEntityFramework()
            .WithModule("Shop")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        foreach (var id in Ids)
        {
            result.ShouldHaveGenerated(id + ".Converter.");
            result.ShouldContain("ConverterExtensions", "Properties<global::DDDToolkit.Sample." + id + ">().HaveConversion");
        }

        result.ShouldContain("TenancyModelBuilderExtensions.AddTenancy.Registration", "AddTenancy<global::DDDToolkit.Sample.Tenant, global::DDDToolkit.Sample.TenantId, global::DDDToolkit.Sample.Organization");
        result.ShouldContain("ShopTenancy.TemplateFacade", "public abstract class ShopTenancy : global::DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<global::DDDToolkit.Sample.Tenant,");
        result.ShouldContain("OrganizationUnit.EntityFramework", "[global::Microsoft.EntityFrameworkCore.Owned]");
    }

    /// <summary>
    /// A course whose members are seats, in a project of one with the switch: Membership's template names the
    /// <c>SeatId</c> and the <c>RoleId</c> the switch writes, which no generator sees as a type.
    /// </summary>
    private const string Courses =
        """
        [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

        namespace Campus.Courses;

        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Membership;
        using DDDToolkit.Supporting.Membership.Access;
        using DDDToolkit.Supporting.Membership.EntityFramework;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        [EntityId<Guid>]
        public readonly partial record struct CourseId;

        [EntityId<Guid>]
        public readonly partial record struct CourseTutorId;

        [Member<CourseTutorId, SeatId, RoleId, Course>]
        public sealed partial class CourseTutor;

        [AggregateRoot<CourseId>]
        public sealed partial class Course
        {
            public static MembershipCodes Codes { get; } = MembershipCodes.Under("courses");

            public Course(CourseId id, SeatId owner, OrganizationUnitId unit) : base(id)
            {
                OwnerSeatId = owner;
                UnitId = unit;
            }

            public SeatId OwnerSeatId { get; private set; }

            public OrganizationUnitId UnitId { get; private set; }

            public partial IReadOnlyList<CourseTutor> Tutors { get; }

            public void Appoint(SeatId seat, RoleId role, MemberPeriod period, DateTimeOffset now) => Members.Add(seat, role, period, now);
        }

        public sealed class CampusContext(DbContextOptions<CampusContext> options) : DbContext(options);

        public static class Startup
        {
            public static IServiceCollection Register(IServiceCollection services, MembershipRules rules)
                => services.AddCourseMembership<CampusContext>(rules).AddCourseMembershipWithTenancy<CampusContext>(rules);
        }
        """;

    [Fact]
    public void Another_packages_template_over_a_written_id_gets_its_registrations_and_its_member_list_in_a_project_of_one()
    {
        var result = GeneratorTestHost.Create(Courses)
            .WithBuildProperty("RootNamespace", "Campus")
            .WithMembership()
            .WithTenancyOnEntityFramework()
            .RunCoreAnd([.. GeneratorTestHost.EntityFrameworkGenerators(), .. GeneratorTestHost.MemberListGenerators(), .. GeneratorTestHost.MembershipGenerators()]);

        result.ShouldCompile();
        result.ReportedDiagnostics.Where(static diagnostic => diagnostic.Id.StartsWith("DDD", StringComparison.Ordinal)).Should().BeEmpty();
        result.ShouldContain("AddMembership.Registration", "AddCourseMembership<TContext>");
        result.ShouldContain("AddCourseMembershipWithTenancy", "AddCourseMembershipWithTenancy<TContext>");
        result.ShouldContain(
            "Course.Members.",
            "private global::DDDToolkit.Supporting.Membership.MemberList<global::Campus.Courses.CourseTutor, global::Campus.Courses.CourseTutorId, global::Campus.SeatId, global::Campus.RoleId> Members");
    }

    /// <summary>
    /// A package of two templates, the second of which names an id as a later type argument, with a registration and a
    /// class the use cases are named through that take that argument, and a switch that writes the first class and
    /// its id: the shape of a member class whose members are seats, without either supporting package.
    /// </summary>
    private const string Pinboard =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateRegistrations(typeof(Acme.Pins.NoteRegistrations))]
        [assembly: TemplateFacade(typeof(Acme.Pins.NoteUseCases<,,>), "{Module}Notes")]

        namespace Acme.Pins;

        [AggregateRootBase]
        public abstract partial class PinAggregate<TPinId>
            where TPinId : IEntityId, IEquatable<TPinId>
        {
            protected PinAggregate(TPinId id) : base(id) { }
        }

        [AggregateRootTemplate(typeof(PinAggregate<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class PinAttribute<TPinId> : Attribute;

        [AggregateRootBase]
        public abstract partial class NoteAggregate<TNoteId, TPinId>
            where TNoteId : IEntityId, IEquatable<TNoteId>
            where TPinId : struct, IEquatable<TPinId>
        {
            protected NoteAggregate(TNoteId id, TPinId pin) : base(id) => Pin = pin;

            public TPinId Pin { get; private set; }
        }

        [AggregateRootTemplate(typeof(NoteAggregate<,>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class NoteAttribute<TNoteId, TPinId> : Attribute;

        [TemplateDefaults(typeof(PinAttribute<>))]
        [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
        public sealed class GeneratePinClassesAttribute : Attribute;

        public static class NoteRegistrations
        {
            [TemplateRegistration]
            public static string AddNotes<
                [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
                [TemplateType(typeof(NoteAttribute<,>))] TNoteId,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TPinId>(this string prefix)
                where TNote : NoteAggregate<TNoteId, TPinId>
                where TNoteId : IEntityId, IEquatable<TNoteId>
                where TPinId : struct, IEquatable<TPinId>
                => prefix + typeof(TPinId).FullName;
        }

        public abstract class NoteUseCases<
            [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
            [TemplateType(typeof(NoteAttribute<,>))] TNoteId,
            [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TPinId>
            where TNote : NoteAggregate<TNoteId, TPinId>
            where TNoteId : IEntityId, IEquatable<TNoteId>
            where TPinId : struct, IEquatable<TPinId>
        {
            protected NoteUseCases()
            {
            }

            public sealed record Pinned(TNoteId Note, TPinId Pin);
        }
        """;

    [Fact]
    public void A_later_type_argument_that_names_a_written_id_is_closed_into_the_registrations_and_the_class_of_the_use_cases()
    {
        const string source =
            """
            [assembly: Acme.Pins.GeneratePinClasses]
            [assembly: DDDToolkit.Abstractions.Attributes.Module("Board")]

            namespace Shop.Notes;

            using System;
            using Acme.Pins;
            using DDDToolkit.Abstractions.Attributes;

            [EntityId<Guid>]
            public readonly partial record struct NoteId;

            [Note<NoteId, PinId>]
            public sealed partial class Note
            {
                public Note(NoteId id, PinId pin) : base(id, pin)
                {
                }
            }

            public static class Startup
            {
                public static string Register() => "pinned by ".AddNotes();

                public static BoardNotes.Pinned Pinned(Note note) => new(note.Id, note.Pin);
            }
            """;

        var result = GeneratorTestHost.Create(source).WithBuildProperty("RootNamespace", "Shop").WithReferencedAssembly(Pinboard, "Acme.Pins").RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldHaveGenerated(Written("Pin")).ShouldHaveGenerated(Written("PinId"));
        result.ShouldContain("Note.", ": global::Acme.Pins.NoteAggregate<global::Shop.Notes.NoteId, global::Shop.PinId>");
        result.ShouldContain("BoardNotes.TemplateFacade", "global::Acme.Pins.NoteUseCases<global::Shop.Notes.Note, global::Shop.Notes.NoteId, global::Shop.PinId>");
        result.ShouldContain("NoteRegistrations.AddNotes.Registration", "AddNotes<global::Shop.Notes.Note, global::Shop.Notes.NoteId, global::Shop.PinId>(prefix)");
    }

    // ------------------------------------------------------------------ a module split by layer

    /// <summary>A module's contracts project, Shop.Contracts, of the module Tenants, with the ids switch.</summary>
    private static GeneratorTestHost WithContracts(GeneratorTestHost host)
        => host.WithReferencedProject("Shop.Contracts", project => project.WithSource(IdsSwitch, "Ids.cs").WithModuleFromTheBuild("Tenants"));

    /// <summary>The module's domain project, Shop.Domain, with the classes switch: it takes the ids of the contracts project.</summary>
    private static GeneratorTestHost WithDomain(GeneratorTestHost host)
        => host.WithReferencedProject("Shop.Domain", project => project.WithSource(Switch, "Classes.cs").WithModuleFromTheBuild("Tenants"));

    [Fact]
    public void A_contracts_project_with_the_ids_switch_gets_the_ids_and_no_class()
    {
        var result = GeneratorTestHost.Create(IdsSwitch).WithAssemblyName("Shop.Contracts").WithTenancy().WithModuleFromTheBuild("Tenants").RunCore();

        result.ShouldCompile();
        foreach (var id in Ids)
        {
            result.ShouldHaveGenerated(Written(id));
        }

        foreach (var name in Classes)
        {
            result.HintNames.Should().NotContain(Written(name), "a contracts project holds no aggregates");
        }

        result.HintNames.Should().NotContain("TemplateFacade").And.NotContain("Registration");
    }

    [Fact]
    public void A_domain_project_takes_the_ids_of_its_contracts_project_and_writes_the_classes()
    {
        var result = WithContracts(GeneratorTestHost.Create(Switch).WithAssemblyName("Shop.Domain").WithTenancy())
            .WithModuleFromTheBuild("Tenants")
            .RunCore();

        result.ShouldCompile();
        foreach (var id in Ids)
        {
            result.HintNames.Should().NotContain(Written(id), "the ids are the contracts project's");
        }

        foreach (var name in Classes)
        {
            result.ShouldHaveGenerated(Written(name));
        }

        ParentOf(result, "Shop.Domain.Tenant").Should().Be("DDDToolkit.Supporting.Tenancy.TenantAggregate<Shop.Contracts.TenantId>");
        result.ShouldHaveGenerated("TenantsTenancy.TemplateFacade");
    }

    [Fact]
    public void The_infrastructure_project_of_a_module_split_by_layer_registers_the_classes_its_domain_project_got_written()
    {
        const string infrastructure =
            """
            namespace Shop.Infrastructure;

            using DDDToolkit.EntityFramework.Conventions;
            using DDDToolkit.Supporting.Tenancy.EntityFramework;
            using Microsoft.EntityFrameworkCore;

            public sealed class TenantsContext(DbContextOptions<TenantsContext> options) : DbContext(options)
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTenancy(database: Database);

                protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.AddDDDToolkitConventions();
            }

            public sealed class Handler(TenantsTenancy.SeatCommands seats, Shop.Domain.Seat? seat)
            {
                public TenantsTenancy.SeatCommands Seats => seats;

                public Shop.Domain.Seat? Seat => seat;
            }
            """;

        var host = GeneratorTestHost.Create(infrastructure).WithAssemblyName("Shop.Infrastructure").WithTenancyOnEntityFramework();
        var result = WithDomain(WithContracts(host))
            .WithModuleFromTheBuild("Tenants")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain(
            "TenancyModelBuilderExtensions.AddTenancy.Registration",
            "AddTenancy<global::Shop.Domain.Tenant, global::Shop.Contracts.TenantId, global::Shop.Domain.Organization, global::Shop.Domain.OrganizationUnit, global::Shop.Contracts.OrganizationUnitId, global::Shop.Domain.Seat, global::Shop.Contracts.SeatId, global::Shop.Domain.Role, global::Shop.Contracts.RoleId>");
        result.HintNames.Should().NotContain(".TemplateDefault.", "the infrastructure project says no switch, and has every class and id from below");
    }

    // ------------------------------------------------------------------ what the switch does not write

    [Fact]
    public void A_type_in_the_way_of_a_class_keeps_that_class_out_and_says_so()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample;

            public sealed class Role;
            """;

        var result = Project(source).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses");
        diagnostic.GetMessage().Should().Be(
            "[assembly: GenerateTenancyClasses] does not write 'Role': this project has a class 'DDDToolkit.Sample.Role' already, which is not declared with [RoleAggregate]: "
            + "if it is meant to be that class, declare it [RoleAggregate<RoleId>] and it is yours; if not, rename it, or declare the package's class yourself under a name of your own, [RoleAggregate<RoleId>]");
        result.HintNames.Should().NotContain(Written("Role")).And.NotContain(Written("Seat"), "a seat takes its role's id");
        result.HintNames.Should().NotContain(Written("RoleId")).And.NotContain(Written("SeatId"), "an id is written for a class that is written, and neither is");
        result.ShouldHaveGenerated(Written("Tenant"));
        result.Count("DDD00066").Should().Be(1);
    }

    [Fact]
    public void A_class_of_the_project_given_the_template_is_the_packages_class_and_the_switch_writes_the_rest()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample;

            [DDDToolkit.Supporting.Tenancy.RoleAggregate<RoleId>]
            public sealed partial class Role
            {
                public string? Note { get; private set; }
            }
            """;

        var result = Project(source).RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00066");
        result.HintNames.Should().NotContain(Written("Role"));
        result.ShouldHaveGenerated(Written("RoleId")).ShouldHaveGenerated(Written("Seat"));
    }

    [Fact]
    public void A_folder_of_a_classes_name_keeps_that_class_out_and_says_so()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Organization.Commands;

            public sealed class AddUnit;
            """;

        var result = Project(source).RunCore();

        CompilesBesidesWhatItSays(result);
        result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses").GetMessage().Should().Be(
            "[assembly: GenerateTenancyClasses] does not write 'Organization': this project has a namespace 'DDDToolkit.Sample.Organization', a folder of that name, say; "
            + "declare the package's class yourself under a name of your own, [OrganizationAggregate<TenantId>], or rename the namespace");
        result.Count("DDD00066").Should().Be(1);
        result.HintNames.Should().NotContain(Written("Organization"));
        result.ShouldHaveGenerated(Written("Tenant")).ShouldHaveGenerated(Written("TenantId"));
    }

    [Fact]
    public void A_type_of_a_classes_name_in_a_project_it_references_keeps_that_class_out_and_says_so()
    {
        var result = Project()
            .WithReferencedAssembly("namespace DDDToolkit.Sample; public sealed class Seat;", "DDDToolkit.Sample.Desks")
            .RunCore();

        CompilesBesidesWhatItSays(result);
        result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses").GetMessage().Should().Be(
            "[assembly: GenerateTenancyClasses] does not write 'Seat': a project it references has a type 'DDDToolkit.Sample.Seat' already; "
            + "declare the package's class yourself under a name of your own, [SeatAggregate<SeatId>]");
        result.HintNames.Should().NotContain(Written("Seat")).And.NotContain(Written("SeatId"));
    }

    [Fact]
    public void An_aggregate_of_a_classes_name_keeps_that_class_out_without_a_second_id_beside_its_own()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample;

            using System;
            using DDDToolkit.Abstractions.Attributes;

            [AggregateRoot<Guid>]
            public sealed partial class Tenant;
            """;

        var result = Project(source).RunCore();

        CompilesBesidesWhatItSays(result);
        result.Count("DDD00066").Should().Be(1, "the organization shares the tenant's id, and a role and a seat take it, so they stand back behind the tenant");
        result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses").GetMessage().Should().StartWith(
            "[assembly: GenerateTenancyClasses] does not write 'Tenant': this project has a class 'DDDToolkit.Sample.Tenant' already");
        result.HintNames.Should().NotContain(Written("TenantId"), "the aggregate's own TenantId is the only one");
        result.ShouldHaveGenerated(Written("OrganizationUnit"));
    }

    [Fact]
    public void An_id_the_generator_writes_for_an_aggregate_of_the_project_is_not_written_twice()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Rentals;

            using System;
            using DDDToolkit.Abstractions.Attributes;

            [AggregateRoot<Guid>]
            public sealed partial class Tenant;
            """;

        var result = Project(source).RunCore();

        CompilesBesidesWhatItSays(result);
        result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses").GetMessage().Should().Be(
            "[assembly: GenerateTenancyClasses] does not write 'Tenant': its id would be 'TenantId', and the generator writes 'DDDToolkit.Sample.Rentals.TenantId' already, "
            + "the id of this project's aggregate root 'Tenant'; rename that class, or declare the package's class yourself under a name of your own, with an id of its own");
        result.Count("DDD00066").Should().Be(1);
        result.HintNames.Should().NotContain(Written("TenantId")).And.NotContain(Written("Tenant")).And.NotContain(Written("Organization"));
    }

    [Fact]
    public void What_the_switch_cannot_write_is_said_once_where_the_registrations_are_closed_too()
    {
        foreach (var source in new[]
                 {
                     "[assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]\nnamespace DDDToolkit.Sample;\npublic sealed class Role;",
                     "[assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]\nnamespace DDDToolkit.Sample.Billing;\npublic sealed class TenantId;",
                 })
        {
            var result = Project(source).WithTenancyOnEntityFramework().WithModule("Shop").RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

            result.ReportedDiagnostics.Select(static diagnostic => diagnostic.Id).Where(static id => id.StartsWith("DDD", StringComparison.Ordinal))
                .Should().Equal(["DDD00066"], "the registrations and the class the use cases are named through stand back behind it, as they do behind DDD00044");
        }
    }

    [Fact]
    public void A_type_of_an_ids_name_that_is_no_entity_id_keeps_the_classes_that_take_it_out_and_is_said_once()
    {
        const string source =
            """
            [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

            namespace DDDToolkit.Sample.Billing;

            public sealed class TenantId;
            """;

        var result = Project(source).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses");
        diagnostic.GetMessage().Should().Be(
            "[assembly: GenerateTenancyClasses] does not write 'Tenant': its id would be 'TenantId', and this project declares 'DDDToolkit.Sample.Billing.TenantId', which is no entity id; mark it [EntityId<Guid>], or name it otherwise");
        result.Count("DDD00066").Should().Be(1, "the classes that take the tenant's class, or its id, stand back behind it");
        result.ShouldNotHaveDiagnostic("DDD00044");
        foreach (var name in new[] { "Tenant", "Organization", "Role", "Seat" })
        {
            result.HintNames.Should().NotContain(Written(name), "{0} takes the tenant's id, or the tenant", name);
        }

        result.ShouldHaveGenerated(Written("OrganizationUnit"));
    }

    [Fact]
    public void Two_ids_of_one_name_in_the_projects_referenced_keep_the_classes_that_take_it_out()
    {
        const string first = "namespace Shop.One; [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>] public readonly partial record struct RoleId;";
        const string second = "namespace Shop.Two; [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>] public readonly partial record struct RoleId;";

        var result = Project()
            .WithReferencedAssembly(first, "Shop.One")
            .WithReferencedAssembly(second, "Shop.Two")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00066", at: "DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses").GetMessage()
            .Should().Be("[assembly: GenerateTenancyClasses] does not write 'Role': its id would be 'RoleId', and the projects it references declare 'Shop.One.RoleId' and 'Shop.Two.RoleId'; keep one");
        result.HintNames.Should().NotContain(Written("Role")).And.NotContain(Written("RoleId"));
    }
}
