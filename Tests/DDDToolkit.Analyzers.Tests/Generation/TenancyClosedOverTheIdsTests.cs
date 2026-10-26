namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// What an application calls of Tenancy, rather than names, is closed over its ids wherever its classes are seen: the
/// system work and the current caller through the class the use cases are named through, <c>TenancyUseCases.BeginSystem()</c>,
/// and the registrations through the wrappers the generator writes, <c>outbox.AddTenancyDomainEvents()</c>. These use
/// the real Tenancy package, seen through metadata, and compile projects that call every one of them without naming an
/// id: one of a single project whose classes the switch writes, where the class is a generator's output that no other
/// generator sees, and a module split by layer, with a host above it. A project of another module sees the ids alone,
/// gets none of it, and names the ids where it calls Tenancy, unless they are arguments, which C# infers.
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

    /// <summary>The system work and the current caller, named through <paramref name="facade"/>, and nothing else of Tenancy's.</summary>
    private static string Work(string facade, string @namespace, string usings = "")
        => $$"""
             using System;
             using DDDToolkit.Supporting.Tenancy.Access;
             {{usings}}

             namespace {{@namespace}};

             public static class Work
             {
                 public static ITenancyCaller Run(TenantId tenant, SeatId seat, Guid person)
                 {
                     using ({{facade}}.BeginSystem())
                     {
                     }

                     using ({{facade}}.BeginSystemIn(tenant))
                     using ({{facade}}.BeginSystemIn(tenant, seat, "shop"))
                     using ({{facade}}.BeginOperator(person))
                     using ({{facade}}.BeginOperatorIn(tenant, person))
                     using ({{facade}}.BeginTokenIn(tenant, seat, "shop"))
                     using (TenancyWork.BeginSystemIn(tenant, seat))
                     {
                         return {{facade}}.CurrentCaller();
                     }
                 }
             }
             """;

    /// <summary>Every registration of Tenancy's that is closed over the ids, without one, over a context of the project's.</summary>
    private static string Registrations(string @namespace, string usings = "")
        => $$"""
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

    [Fact]
    public void A_project_of_one_whose_classes_the_switch_writes_calls_every_one_without_an_id()
    {
        // The hardest place: the classes, the ids and the class the work is named through are all generators' output,
        // which no other generator sees. A call is bound by the compiler, which sees them all.
        var result = GeneratorTestHost.Create("[assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]", "Switch.cs")
            .WithSource(Work("TenancyUseCases", "Shop.Work"), "Work.cs")
            .WithSource(Registrations("Shop.Persistence"), "Registrations.cs")
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
        // The domain project declares the classes and gets the class the work is named through, without Entity
        // Framework; the infrastructure project, of the same module, gets the registrations and calls them.
        static GeneratorTestHost Domain(GeneratorTestHost project)
            => project.WithSource(Ids, "Ids.cs").WithSource(Classes, "Classes.cs").WithSource(ModuleAttribute("Tenants"), "Module.cs");

        const string usings = "using Shop.Contracts;";
        var infrastructure = GeneratorTestHost.Create(Registrations("Shop.Tenants.Infrastructure", usings), "Registrations.cs")
            .WithSource(Work("TenancyUseCases", "Shop.Tenants.Infrastructure", usings), "Work.cs")
            .WithSource(ModuleAttribute("Tenants"), "Module.cs")
            .WithTenancy()
            .WithReferencedProject("Shop.Tenants.Domain", Domain)
            .WithTenancyOnEntityFramework()
            .RunCore();

        infrastructure.ShouldCompile();
        infrastructure.ReportedDiagnostics.Should().BeEmpty();
        infrastructure.ShouldHaveGenerated("TenancyOutboxExtensions.AddTenancyDomainEvents.Registration");
        infrastructure.HintNames.Should().NotContain("TemplateFacade", "the class is the domain project's, and this project sees that one");

        // The host declares no module and registers nothing of Tenancy's: it calls the module's registration. It
        // begins system work and asks the caller through the domain project's class, and asks the selection without
        // its ids, as a host's middleware does.
        const string host =
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DDDToolkit.Abstractions.Access;
            using DDDToolkit.Supporting.Tenancy.Access;

            namespace Shop.Host;

            public static class Middleware
            {
                public static async Task<ITenancyCaller> SeatOf(ITenantSelection selection, Caller caller, string? slug)
                {
                    var seat = await selection.ResolveAsync(caller, slug, CancellationToken.None);
                    using (TenancyCallers.Begin(seat))
                    {
                        return TenancyUseCases.CurrentCaller();
                    }
                }
            }
            """;

        var above = GeneratorTestHost.Create(host, "Middleware.cs")
            .WithSource(Work("TenancyUseCases", "Shop.Host", usings), "Work.cs")
            .WithTenancy()
            .WithReferencedProject("Shop.Tenants.Domain", Domain)
            .RunCore();

        above.ShouldCompile();
        above.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_project_of_another_module_sees_only_the_ids_and_names_them_unless_they_are_arguments()
    {
        // Nothing it sees says which of its ids are Tenancy's: no class of Tenancy's, so no class the work is named
        // through and no registration closed over them. What it begins for a seat infers both ids all the same.
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
                    => services.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IProjectsRequest, ProjectsContext>();

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
            .And.NotContain("TemplateFacade");

        // Without the ids, the call has nothing to close over: the compiler says so, and nothing else does.
        var closed = Project(projects.Replace(
            "modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>()",
            "modelBuilder.AddTenancyReadFunctions()",
            StringComparison.Ordinal)).RunCore();
        closed.CompilationErrors.Select(static error => error.Id).Should().Equal(["CS0411"]);
    }
}
