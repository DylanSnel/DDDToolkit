namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What an application calls of Tenancy, rather than names, is closed over its ids wherever its classes are seen: who
/// acts, the tenant, and system work through the access manager, <c>TenancyAccessManager</c>, a class the container
/// makes; Tenancy's queries over a context, <c>db.Tenancy()</c>; and the registrations through the wrappers the generator
/// writes, <c>outbox.AddTenancyDomainEvents()</c>. These use the real Tenancy package, seen through metadata, and compile
/// projects that call every one of them without naming an id: one of a single project whose classes the switch writes,
/// where the class is a generator's output that no other generator sees, and a module split by layer, with a host above
/// it. A project of another module sees the ids alone: without a line it gets none of it, and names the ids where it
/// calls Tenancy; with <c>[assembly: TenancyIds&lt;...&gt;]</c> it gets the same, closed over the line's ids.
/// </summary>
public class TenancyClosedOverTheIdsTests
{
    /// <summary>The module's ids, in the project that declares Tenancy's classes.</summary>
    private const string Ids =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop.Contracts;

        [EntityId<Guid>]
        public readonly partial record struct TenantId;

        [EntityId<Guid>]
        public readonly partial record struct SeatId;

        [EntityId<Guid>]
        public readonly partial record struct OrganizationUnitId;

        [EntityId<Guid>]
        public readonly partial record struct RoleId;
        """;

    /// <summary>Tenancy's classes under the application's own names.</summary>
    private const string Classes =
        """
        using DDDToolkit.Supporting.Tenancy;
        using Shop.Contracts;

        namespace Shop.Domain;

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

    /// <summary>The ids' full names, as the generator writes them.</summary>
    private const string ShopIds = "global::Shop.Contracts.TenantId, global::Shop.Contracts.SeatId, global::Shop.Contracts.OrganizationUnitId, global::Shop.Contracts.RoleId";

    /// <summary>The work that is not the caller's own, who acts and the tenant, asked of the access manager the code takes, and nothing else of Tenancy's.</summary>
    private static string Work(string @namespace, string usings = "")
        => $$"""
             using System;
             using DDDToolkit.Supporting.Tenancy.Access;
             {{usings}}

             namespace {{@namespace}};

             public sealed class Work(TenancyAccessManager access)
             {
                 public Guid? Run(TenantId tenant, SeatId seat)
                 {
                     using (access.BeginSystem())
                     {
                     }

                     using (access.BeginSystemIn(tenant))
                     using (access.BeginSystemIn(tenant, seat, "shop"))
                     using (access.BeginTokenIn(tenant, seat, "shop"))
                     {
                         TenantId? current = access.CurrentTenant.Id;
                         SeatId? acting = access.CurrentTenant.Seat;
                         return access.CurrentUser.Id;
                     }
                 }
             }
             """;

    /// <summary>Every registration of Tenancy's that is closed over the ids, without one, over a context of the project's, and its queries over that context.</summary>
    private static string Registrations(string @namespace, string usings = "")
        => $$"""
             using System.Linq;
             using DDDToolkit.Access;
             using DDDToolkit.EntityFramework.Options;
             using DDDToolkit.Supporting.Tenancy.EntityFramework;
             using Microsoft.EntityFrameworkCore;
             using Microsoft.Extensions.DependencyInjection;
             {{usings}}

             namespace {{@namespace}};

             public interface IShopRequest : IRequireAccess;

             public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
             {
                 protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTenancy(database: Database);
             }

             public sealed class ReportsContext(DbContextOptions<ReportsContext> options) : DbContext(options)
             {
                 public static bool ReadsFunctions { get; set; }

                 protected override void OnModelCreating(ModelBuilder modelBuilder)
                     => _ = ReadsFunctions ? modelBuilder.AddTenancyReadFunctions() : modelBuilder.AddTenancyReadModel();

                 public IQueryable<OrganizationUnitId> Managed() => this.Tenancy().UnitsWhereIHold("shop.units.manage");
             }

             public static class Startup
             {
                 public static IServiceCollection Register(IServiceCollection services)
                     => services.AddTenancy<ShopContext>().AddTenancyAccess<IShopRequest, ReportsContext>();

                 public static OutboxOptions Outbox(OutboxOptions outbox)
                     => outbox.AddTenancyDomainEvents().KeepEventLog(log => log.AddTenancyEventLog());
             }
             """;

    private static string ModuleAttribute(string module) => "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"" + module + "\")]\n";

    /// <summary>The line a module of only the ids says which of them are Tenancy's with.</summary>
    private const string Line = "[assembly: DDDToolkit.Supporting.Tenancy.TenancyIds<Shop.Contracts.TenantId, Shop.Contracts.SeatId, Shop.Contracts.OrganizationUnitId, Shop.Contracts.RoleId>]\n";

    [Fact]
    public void A_project_of_one_whose_classes_the_switch_writes_calls_every_one_without_an_id()
    {
        // The hardest place: the classes, the ids and the access manager are all generators' output, which no other
        // generator sees. A call is bound by the compiler, which sees them all.
        var result = GeneratorTestHost.Create("[assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]", "Switch.cs")
            .WithSource(Work("Shop.Work", "using Shop;"), "Work.cs")
            .WithSource(Registrations("Shop.Persistence", "using Shop;"), "Registrations.cs")
            .WithBuildProperty("RootNamespace", "Shop")
            .WithTenancyOnEntityFramework()
            .WithModule("Shop")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Where(static diagnostic => diagnostic.Id.StartsWith("DDD", StringComparison.Ordinal)).Should().BeEmpty();
        result.ShouldContain("TenancyOutboxExtensions.AddTenancyDomainEvents.Registration", "AddTenancyDomainEvents<global::Shop.TenantId, global::Shop.SeatId, global::Shop.OrganizationUnitId, global::Shop.RoleId>(outbox)");
        result.ShouldContain("TenancyEventLogExtensions.AddTenancyEventLog.Registration", "AddTenancyEventLog<global::Shop.TenantId, global::Shop.SeatId, global::Shop.OrganizationUnitId, global::Shop.RoleId>(log)");
        result.ShouldHaveGenerated("TenancyModelBuilderExtensions.AddTenancyReadModel.Registration");
        result.ShouldHaveGenerated("TenancyModelBuilderExtensions.AddTenancyReadFunctions.Registration");

        // The access manager is a class the container makes, in the global namespace, and the registration registers it.
        result.ShouldContain(
            "TenancyAccessManager.TemplateFacade",
            "public sealed class TenancyAccessManager : global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccessManager<global::Shop.TenantId, global::Shop.SeatId, global::Shop.OrganizationUnitId, global::Shop.RoleId>");
        result.ShouldContain(
            "TenancyAccessManager.TemplateFacade",
            "public TenancyAccessManager(global::DDDToolkit.Supporting.Tenancy.Catalogue.TenancyCatalogue catalogue, global::System.TimeProvider clock, global::System.IServiceProvider services)");
        result.ShouldContain("TenancyEntityFrameworkServiceCollectionExtensions.AddTenancy.Registration", "global::Shop.RoleId, global::TenancyAccessManager, TContext>(services, configure)");
        result.ShouldContain("TenancyEntityFrameworkServiceCollectionExtensions.AddTenancyAccess.Registration", "global::TenancyAccessManager, TRequests, TContext>(services)");
        result.ShouldContain(
            "TenancyEntityFrameworkExtensions.Tenancy.Registration",
            "public static global::DDDToolkit.Supporting.Tenancy.Access.ITenancyQueries<global::Shop.TenantId, global::Shop.SeatId, global::Shop.OrganizationUnitId, global::Shop.RoleId> Tenancy(this global::Microsoft.EntityFrameworkCore.DbContext context)");
    }

    [Fact]
    public void The_invitations_events_take_the_invitations_id_and_close_over_the_rest()
    {
        // An application may have no invitations, so their id is named, as where invitations are registered; the
        // four ids the other classes have are not.
        const string invitations =
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.EntityFramework.Options;
            using DDDToolkit.Supporting.Tenancy.EntityFramework;

            namespace Shop.Persistence;

            [EntityId<Guid>]
            public readonly partial record struct InvitationId;

            public static class Outbox
            {
                public static OutboxOptions Invitations(OutboxOptions outbox) => outbox.AddTenancyInvitationEvents<InvitationId>();
            }
            """;

        var result = GeneratorTestHost.Create(Ids, "Ids.cs").WithSource(Classes, "Classes.cs").WithSource(invitations, "Outbox.cs")
            .WithTenancyOnEntityFramework()
            .WithModule("Shop")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "TenancyOutboxExtensions.AddTenancyInvitationEvents.Registration",
            "AddTenancyInvitationEvents<global::Shop.Contracts.TenantId, TInvitationId, global::Shop.Contracts.OrganizationUnitId, global::Shop.Contracts.RoleId, global::Shop.Contracts.SeatId>(outbox)");
    }

    [Fact]
    public void A_module_split_by_layer_calls_every_one_without_an_id_and_so_does_the_host_above_it()
    {
        // The domain project declares the classes and gets the access manager, without Entity Framework; the
        // infrastructure project, of the same module, gets the registrations and the queries over a context, and calls
        // them, the registrations closed over the domain project's access manager.
        static GeneratorTestHost Domain(GeneratorTestHost project)
            => project.WithSource(Ids, "Ids.cs").WithSource(Classes, "Classes.cs").WithSource(ModuleAttribute("Tenants"), "Module.cs");

        const string usings = "using Shop.Contracts;";
        var infrastructure = GeneratorTestHost.Create(Registrations("Shop.Tenants.Infrastructure", usings), "Registrations.cs")
            .WithSource(Work("Shop.Tenants.Infrastructure", usings), "Work.cs")
            .WithSource(ModuleAttribute("Tenants"), "Module.cs")
            .WithTenancy()
            .WithReferencedProject("Shop.Tenants.Domain", Domain)
            .WithTenancyOnEntityFramework()
            .RunCore();

        infrastructure.ShouldCompile();
        infrastructure.ReportedDiagnostics.Should().BeEmpty();
        infrastructure.ShouldHaveGenerated("TenancyOutboxExtensions.AddTenancyDomainEvents.Registration");
        infrastructure.ShouldContain("TenancyEntityFrameworkServiceCollectionExtensions.AddTenancy.Registration", "global::Shop.Contracts.RoleId, global::TenancyAccessManager, TContext>(services, configure)");
        infrastructure.HintNames.Should().NotContain("TemplateFacade", "the classes are the domain project's, and this project sees those");

        // The host declares no module and registers nothing of Tenancy's: it calls the module's registration. It
        // begins system work and asks the tenant through the domain project's access manager, and asks the selection
        // without its ids, as a host's middleware does.
        const string host =
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DDDToolkit.Abstractions.Access;
            using DDDToolkit.Supporting.Tenancy.Access;

            namespace Shop.Host;

            public static class Middleware
            {
                public static async Task<ITenancyCaller?> SeatOf(ITenantSelection selection, Caller caller, string? slug)
                {
                    var seat = await selection.ResolveAsync(caller, slug, CancellationToken.None);
                    using (TenancyCallers.Begin(seat))
                    {
                        return TenancyCallers.Ambient;
                    }
                }
            }
            """;

        var above = GeneratorTestHost.Create(host, "Middleware.cs")
            .WithSource(Work("Shop.Host", usings), "Work.cs")
            .WithTenancy()
            .WithReferencedProject("Shop.Tenants.Domain", Domain)
            .RunCore();

        above.ShouldCompile();
        above.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_project_of_another_module_without_a_line_sees_only_the_ids_and_names_them()
    {
        // Nothing it sees says which of its ids are Tenancy's: no class of Tenancy's, so no access manager and no
        // registration closed over them. It names the package's own class and the ids where it registers.
        const string projects =
            """
            using System;
            using DDDToolkit.Access;
            using DDDToolkit.Supporting.Tenancy.Access;
            using DDDToolkit.Supporting.Tenancy.EntityFramework;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.Contracts;

            namespace Shop.Projects;

            public interface IProjectsRequest : IRequireAccess;

            public sealed class ProjectsContext(DbContextOptions<ProjectsContext> options) : DbContext(options)
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>();
            }

            public static class Startup
            {
                public static IServiceCollection Register(IServiceCollection services)
                    => services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, TenancyAccessManager<TenantId, SeatId, OrganizationUnitId, RoleId>, IProjectsRequest, ProjectsContext>();

                public static IDisposable OwnPlace(TenantId tenant, SeatId seat) => TenancyWork.BeginSystemIn(tenant, seat, "projects");
            }
            """;

        GeneratorTestHost Project(string source)
            => GeneratorTestHost.Create(source, "Projects.cs")
                .WithSource(ModuleAttribute("Projects"), "Module.cs")
                .WithTenancyOnEntityFramework()
                .WithReferencedProject("Shop.Contracts", project => project.WithSource(Ids, "Ids.cs").WithSource(ModuleAttribute("Tenants"), "Module.cs"));

        var result = Project(projects).RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("a project that only refers to the ids is not meant to get anything, and hears nothing");
        result.HintNames.Should().NotContain("TenancyOutboxExtensions")
            .And.NotContain("TenancyModelBuilderExtensions")
            .And.NotContain("TenancyEntityFrameworkServiceCollectionExtensions")
            .And.NotContain("TenancyEntityFrameworkExtensions")
            .And.NotContain("TemplateFacade");

        // Without the ids, the call has nothing to close over: the compiler says so, and nothing else does.
        var closed = Project(projects.Replace(
            "modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>()",
            "modelBuilder.AddTenancyReadFunctions()",
            StringComparison.Ordinal)).RunCore();
        closed.CompilationErrors.Select(static error => error.Id).Should().Equal(["CS0411"]);
    }

    /// <summary>The application project of a module that sees only the ids: a handler that takes the access manager.</summary>
    private const string ProjectsApplication =
        """
        using System;
        using DDDToolkit.Access;
        using Shop.Contracts;

        namespace Shop.Projects.Application.Crew;

        public interface IProjectsRequest : IRequireAccess;

        public sealed class AddCrewMemberHandler(TenancyAccessManager access)
        {
            public SeatId? AddedBy() => access.CurrentTenant.Require().Seat;

            public IDisposable OwnPlace(TenantId tenant, SeatId seat) => access.BeginSystemIn(tenant, seat, "projects");
        }
        """;

    /// <summary>The infrastructure project of that module: its context, Tenancy's queries over it, and its registration.</summary>
    private const string ProjectsInfrastructure =
        """
        using System.Linq;
        using DDDToolkit.Supporting.Tenancy.EntityFramework;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.Contracts;
        using Shop.Projects.Application.Crew;

        namespace Shop.Projects.Infrastructure;

        public sealed class ProjectsContext(DbContextOptions<ProjectsContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddTenancyReadFunctions();

            public IQueryable<OrganizationUnitId> Managed() => this.Tenancy().UnitsWhereIHold("projects.manage");
        }

        public static class Startup
        {
            public static IServiceCollection Register(IServiceCollection services) => services.AddTenancyAccess<IProjectsRequest, ProjectsContext>();
        }
        """;

    private static GeneratorTestHost Contracts(GeneratorTestHost project) => project.WithSource(Ids, "Ids.cs").WithSource(ModuleAttribute("Tenants"), "Module.cs");

    private static GeneratorTestHost Application(GeneratorTestHost project)
        => project.WithSource(ProjectsApplication, "Crew.cs").WithSource(Line + ModuleAttribute("Projects"), "Module.cs");

    [Fact]
    public void A_project_of_another_module_with_a_line_gets_the_access_manager_in_its_root_namespace()
    {
        var application = Application(GeneratorTestHost.Create("namespace Shop.Projects.Application; public sealed class Nothing;"))
            .WithAssemblyName("Shop.Projects.Application")
            .WithTenancy()
            .WithReferencedProject("Shop.Contracts", Contracts)
            .RunCore();

        application.ShouldCompile();
        application.ReportedDiagnostics.Should().BeEmpty();
        application.ShouldContain("Shop.Projects.Application.TenancyAccessManager.TemplateFacade", "namespace Shop.Projects.Application;");
        application.ShouldContain(
            "Shop.Projects.Application.TenancyAccessManager.TemplateFacade",
            "public sealed class TenancyAccessManager : global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccessManager<" + ShopIds + ">");
        application.ShouldContain(
            "Shop.Projects.Application.TenancyAccessManager.TemplateFacade",
            "/// TenancyAccessManager, closed over the ids this project's line names as the package's: TenantId, SeatId, OrganizationUnitId and RoleId.");
        application.HintNames.Should().NotContain("TenancyUseCases", "the use cases take the classes, which the module does not see");

        var facade = application.OutputCompilation.GetTypeByMetadataName("Shop.Projects.Application.TenancyAccessManager")!;
        facade.IsSealed.Should().BeTrue("the container makes it, and nothing derives from it");
        facade.InstanceConstructors.Should().ContainSingle().Which.DeclaredAccessibility.Should().Be(Microsoft.CodeAnalysis.Accessibility.Public);
    }

    [Fact]
    public void The_project_of_that_module_that_holds_the_context_registers_its_access_manager_and_asks_without_an_id()
    {
        // The infrastructure project reads the line in the application project of its module: the registrations and the
        // queries over a context are closed over its ids, and the registration over the module's access manager.
        var infrastructure = GeneratorTestHost.Create(ProjectsInfrastructure, "Persistence.cs")
            .WithSource(ModuleAttribute("Projects"), "Module.cs")
            .WithTenancyOnEntityFramework()
            .WithReferencedProject("Shop.Contracts", Contracts)
            .WithReferencedProject("Shop.Projects.Application", Application)
            .RunCore();

        infrastructure.ShouldCompile();
        infrastructure.ReportedDiagnostics.Should().BeEmpty();
        infrastructure.ShouldContain(
            "TenancyEntityFrameworkServiceCollectionExtensions.AddTenancyAccess.Registration",
            "AddTenancyAccess<" + ShopIds + ", global::Shop.Projects.Application.TenancyAccessManager, TRequests, TContext>(services)");
        infrastructure.ShouldContain("TenancyEntityFrameworkExtensions.Tenancy.Registration", "Tenancy<" + ShopIds + ">(context)");
        infrastructure.ShouldContain("TenancyModelBuilderExtensions.AddTenancyReadFunctions.Registration", "AddTenancyReadFunctions<" + ShopIds + ">(");
        infrastructure.HintNames.Should().NotContain("TemplateFacade", "the access manager is the application project's, which this one sees")
            .And.NotContain("TenancyEntityFrameworkServiceCollectionExtensions.AddTenancy.Registration", "AddTenancy takes the classes, which the module does not see");

        // The API project above it of the same module gets none of it again: the infrastructure project holds them.
        var api = GeneratorTestHost.Create("namespace Shop.Projects.Api; public static class Endpoints;", "Endpoints.cs")
            .WithSource(ModuleAttribute("Projects"), "Module.cs")
            .WithTenancyOnEntityFramework()
            .WithReferencedProject("Shop.Contracts", Contracts)
            .WithReferencedProject("Shop.Projects.Application", Application)
            .WithReferencedProject("Shop.Projects.Infrastructure", project => project.WithSource(ProjectsInfrastructure, "Persistence.cs").WithSource(ModuleAttribute("Projects"), "Module.cs"))
            .RunCore();

        api.ShouldCompile();
        api.ReportedDiagnostics.Should().BeEmpty();
        api.HintNames.Should().NotContain("Registration").And.NotContain("TemplateFacade");
    }

    [Fact]
    public void A_host_that_sees_both_modules_names_the_access_manager_of_the_module_that_declares_the_classes()
    {
        // The module of the ids keeps its access manager in its own namespace, so it never meets the one of the module
        // that declares the classes, which is in the global namespace: the host names that one, and is told nothing.
        const string host =
            """
            using Shop.Contracts;

            namespace Shop.Host;

            public sealed class Seeder(TenancyAccessManager access)
            {
                public System.IDisposable Seed(TenantId tenant) => access.BeginSystemIn(tenant);
            }
            """;

        var result = GeneratorTestHost.Create(host, "Seeder.cs")
            .WithTenancy()
            .WithReferencedProject("Shop.Contracts", Contracts)
            .WithReferencedProject("Shop.Tenants.Domain", project => project.WithSource(Classes, "Classes.cs").WithSource(ModuleAttribute("Tenants"), "Module.cs"))
            .WithReferencedProject("Shop.Projects.Application", Application)
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("the two classes are not of one name where the host sees them");
        var seeder = result.OutputCompilation.GetTypeByMetadataName("Shop.Host.Seeder")!;
        seeder.InstanceConstructors.Single().Parameters.Single().Type.ContainingAssembly.Name.Should().Be("Shop.Tenants.Domain");
    }

    [Fact]
    public void A_line_in_the_module_that_declares_the_classes_changes_nothing_there()
    {
        // The classes say which ids are Tenancy's already: the access manager is closed over them, in the global namespace.
        var result = GeneratorTestHost.Create(Classes, "Classes.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Line + ModuleAttribute("Tenants"), "Module.cs")
            .WithTenancy()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("TenancyAccessManager.TemplateFacade", "public sealed class TenancyAccessManager : global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccessManager<" + ShopIds + ">");
        result.HintNames.Should().NotContain(".TenancyAccessManager.TemplateFacade", "no second one in the root namespace");
    }
}
