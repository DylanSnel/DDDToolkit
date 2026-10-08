using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A package asks, with <c>[assembly: TemplateFacade]</c>, that the project that declares the application's classes
/// gets a generic class of the package's closed over them, as a class of its own named as the package's class is
/// without its type parameters: Tenancy's use cases, nested in a class generic over nine of the application's classes
/// and ids, are <c>TenancyUseCases.SeatCommands</c> there and in every project above it, and no project writes the
/// nine types.
/// <para>
/// Most of these use the real Tenancy package, seen through metadata as an application sees it, and a module split
/// by layer the way the sample is: each project of it is compiled with what the generators wrote, and names the
/// use cases and their records itself, so a class that is missing, or closed over the wrong classes, fails to
/// compile there.
/// </para>
/// <para>
/// Where the class cannot be written the project that declares the classes says why, DDD00065, since the projects
/// above would only hear that the name does not exist; and that project may name the class itself, with
/// <c>[assembly: TemplateFacadeName]</c>. Two modules that each declare the classes get two classes of one name, which
/// meet in a project that sees both: DDD00075 says so there, with the line that names one of them.
/// </para>
/// </summary>
public class TemplateFacadeTests
{
    /// <summary>The module's ids, in its contracts: what every module that refers to an organization sees.</summary>
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

        [EntityId<Guid>]
        public readonly partial record struct InvitationId;
        """;

    /// <summary>Tenancy's classes under the application's own names, in its domain project.</summary>
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

        [InvitationAggregate<InvitationId>]
        public sealed partial class Invitation;
        """;

    /// <summary>What the class derives from, as the generator spells it.</summary>
    private const string ClosedOverShop =
        "global::DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<global::Shop.Domain.Tenant, global::Shop.Contracts.TenantId, "
        + "global::Shop.Domain.Organization, global::Shop.Domain.OrganizationUnit, global::Shop.Contracts.OrganizationUnitId, "
        + "global::Shop.Domain.Seat, global::Shop.Contracts.SeatId, global::Shop.Domain.Role, global::Shop.Contracts.RoleId>";

    /// <summary>The name the class has when nobody names it: Tenancy's class's, without its type parameters.</summary>
    private const string Default = "TenancyUseCases";

    /// <summary>
    /// A handler of the application's: it takes two use cases, among them the invitations' closed over the module's
    /// invitation class as well, and answers two records of theirs, through the class's name alone.
    /// </summary>
    private static string Handler(string name = Default, string @namespace = "Shop.Application")
        => $$"""
             using Shop.Contracts;
             using Shop.Domain;

             namespace {{@namespace}};

             public sealed class SeatsOfMine({{name}}.SeatCommands seats, {{name}}.InvitationCommands<Invitation, InvitationId> invitations)
             {
                 public {{name}}.SeatCommands Seats => seats;

                 public {{name}}.InvitationCommands<Invitation, InvitationId> Invitations => invitations;

                 public {{name}}.SeatOverview? Last { get; set; }

                 public {{name}}.TenantToProvision? Next { get; set; }
             }
             """;

    private static string ModuleAttribute(string module) => "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"" + module + "\")]\n";

    /// <summary>The line an application names Tenancy's use cases with, in place of the package's name.</summary>
    private static string NamedItself(string name, string facade = Default)
        => "[assembly: DDDToolkit.Abstractions.Attributes.TemplateFacadeName(\"" + facade + "\", \"" + name + "\")]\n";

    /// <summary>The snippet without these lines: a class left out of <see cref="Classes"/>, its attribute and its declaration.</summary>
    private static string Without(string source, params string[] lines)
        => lines.Aggregate(source, static (left, line) => left.Replace(line, string.Empty));

    /// <summary>The seat in <see cref="Classes"/>.</summary>
    private static readonly string[] SeatLines = ["[SeatAggregate<SeatId>]", "public sealed partial class Seat;"];

    /// <summary>The invitation in <see cref="Classes"/>, whose parent takes the seat's id.</summary>
    private static readonly string[] InvitationLines = ["[InvitationAggregate<InvitationId>]", "public sealed partial class Invitation;"];

    /// <summary>The domain project of a module: its ids and Tenancy's classes, and the module it declares, if any.</summary>
    private static Func<GeneratorTestHost, GeneratorTestHost> Domain(string? module)
        => project =>
        {
            project = project.WithSource(Ids, "Ids.cs").WithSource(Classes, "Classes.cs");
            return module is null ? project : project.WithSource(ModuleAttribute(module), "Module.cs");
        };

    /// <summary>
    /// The domain project of a module of its own, Customers or Partners: its contracts' ids and its classes in
    /// namespaces after it, and the module it declares.
    /// </summary>
    private static Func<GeneratorTestHost, GeneratorTestHost> ModuleOfItsOwn(string module)
        => project => project
            .WithSource(ModuleAttribute(module), "Module.cs")
            .WithSource(Ids.Replace("Shop.Contracts", module + ".Contracts"), "Ids.cs")
            .WithSource(Classes.Replace("Shop.Contracts", module + ".Contracts").Replace("Shop.Domain", module + ".Domain"), "Classes.cs");

    /// <summary>A handler of a host over the classes of one of two modules, through the name given.</summary>
    private static string HandlerOf(string module, string name)
        => Handler(name, "Host." + module).Replace("using Shop.", "using " + module + ".");

    /// <summary>A project above the module's, with Tenancy, in the module given or in none.</summary>
    private static GeneratorTestHost Project(string source, string? module = null)
    {
        var project = GeneratorTestHost.Create(source, "Source.cs").WithTenancy();
        return module is null ? project : project.WithSource(ModuleAttribute(module), "Module.cs");
    }

    /// <summary>The type a property of a class the snippet declares has: what the name stands for, as the compiler sees it.</summary>
    private static INamedTypeSymbol TypeOf(GeneratorRunOutcome result, string type, string member)
        => (INamedTypeSymbol)((IPropertySymbol)result.OutputCompilation.GetTypeByMetadataName(type)!.GetMembers(member).Single()).Type;

    // ------------------------------------------------------------------ the project that declares the classes

    [Fact]
    public void The_project_that_declares_the_classes_gets_a_class_named_as_the_packages_class_is()
    {
        var result = Domain("Shop")(Project(Handler())).RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("TenancyUseCases.TemplateFacade", "public abstract class TenancyUseCases : " + ClosedOverShop);
        result.ShouldContain("TenancyUseCases.TemplateFacade", "    private TenancyUseCases()");
        result.ShouldContain("TenancyUseCases.TemplateFacade", "/// TenancyUseCases, closed over the classes of the module Shop: Tenant, TenantId, Organization, OrganizationUnit, OrganizationUnitId, Seat, SeatId, Role and RoleId.");
        result.ShouldContain(
            "TenancyUseCases.TemplateFacade",
            "/// Named as the package's class is; [assembly: TemplateFacadeName(\"TenancyUseCases\", \"...\")] in this project names it otherwise.",
            "the developer who goes to the class reads where its name comes from and how to give it another");
    }

    [Fact]
    public void What_it_names_is_the_packages_own_nested_type_and_the_class_is_only_a_name()
    {
        var result = Domain("Shop")(Project(Handler())).RunCore();

        result.ShouldCompile();
        var seats = TypeOf(result, "Shop.Application.SeatsOfMine", "Seats");
        seats.ContainingAssembly.Name.Should().Be("DDDToolkit.Supporting.Tenancy", "what the container registered is the package's own type");
        seats.ContainingType!.OriginalDefinition.ToDisplayString().Should().StartWith("DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<");
        seats.ContainingType.TypeArguments.Select(argument => argument.Name).Should().Equal("Tenant", "TenantId", "Organization", "OrganizationUnit", "OrganizationUnitId", "Seat", "SeatId", "Role", "RoleId");
        TypeOf(result, "Shop.Application.SeatsOfMine", "Invitations").TypeArguments.Select(argument => argument.Name).Should().Equal("Invitation", "InvitationId");

        var facade = result.OutputCompilation.GetTypeByMetadataName(Default)!;
        facade.ContainingNamespace.IsGlobalNamespace.Should().BeTrue("no project needs a using for it");
        facade.DeclaredAccessibility.Should().Be(Accessibility.Public, "the projects above see it");
        facade.IsAbstract.Should().BeTrue();
        facade.InstanceConstructors.Should().ContainSingle().Which.DeclaredAccessibility.Should().Be(Accessibility.Private, "nothing makes one, and nothing derives from it");
        SymbolEqualityComparer.Default.Equals(facade.BaseType, seats.ContainingType).Should().BeTrue();
    }

    [Fact]
    public void The_name_is_the_packages_class_without_its_type_parameters_whatever_the_module_is_called()
    {
        // A module called Tenancy gets TenancyUseCases too, where it got TenancyTenancy after its module.
        foreach (var module in new[] { "Tenancy", "order-management", "Tenants" })
        {
            var result = Domain(module)(Project(Handler())).RunCore();
            result.ShouldCompile();
            result.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
            result.HintNames.Should().NotContain("Tenancy" + "Tenancy").And.NotContain("OrderManagement");
        }

        // A project that declares no module, named after its assembly or by DDD_Module everywhere else, gets the same.
        var unnamed = Domain(module: null)(Project(Handler())).WithAssemblyName("Shop.Domain").RunCore();
        unnamed.ShouldCompile();
        unnamed.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");

        var named = Domain(module: null)(Project(Handler())).WithAssemblyName("Shop.Domain").WithModule("Shop").RunCore();
        named.ShouldCompile();
        named.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
    }

    [Fact]
    public void It_does_not_clash_with_the_packages_generic_class_in_a_file_that_imports_its_namespace()
    {
        // C# tells two classes of one name apart by their type parameters: the file names both, each by its own.
        var result = Domain("Shop")(Project(
                """
                using DDDToolkit.Supporting.Tenancy.UseCases;
                using Shop.Contracts;
                using Shop.Domain;

                namespace Shop.Application;

                public static class BothNames
                {
                    public static System.Type Facade => typeof(TenancyUseCases);

                    public static System.Type Generic => typeof(TenancyUseCases<Tenant, TenantId, Organization, OrganizationUnit, OrganizationUnitId, Seat, SeatId, Role, RoleId>);

                    public static TenancyUseCases.SeatOverview? Last { get; set; }
                }
                """))
            .RunCore();

        result.ShouldCompile();
        result.CompilationDiagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).Should().BeEmpty();
    }

    [Fact]
    public void A_module_its_folder_declares_gets_the_class_in_its_domain_project_and_its_projects_above_see_that_one()
    {
        // DDD_Module, as a Directory.Build.props sets it for a module's folder: the build declares the module, the
        // domain project gets the class, as the sample's Tenants domain project does, and the module's projects above
        // see it and write none of their own.
        var domain = Domain(module: null)(Project(Handler())).WithAssemblyName("Shop.Tenants.Domain").WithModuleFromTheBuild("Tenants").RunCore();
        domain.ShouldCompile();
        domain.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");

        var application = Project(Handler()).WithAssemblyName("Shop.Tenants.Application").WithModuleFromTheBuild("Tenants")
            .WithReferencedProject("Shop.Tenants.Domain", project => Domain(module: null)(project).WithModuleFromTheBuild("Tenants"))
            .RunCore();
        application.ShouldCompile();
        application.HintNames.Should().NotContain("TemplateFacade", "the class is the domain project's, and every project of the module above sees that one");
        application.OutputCompilation.GetTypeByMetadataName(Default)!.ContainingAssembly.Name.Should().Be("Shop.Tenants.Domain");
    }

    // ------------------------------------------------------------------ every project above them

    [Fact]
    public void Every_project_of_a_module_split_by_layer_names_the_use_cases_and_their_records_without_writing_anything()
    {
        // Each project is compiled with what the generators wrote into it, and fails to unless it sees the class.
        var host = Project(Handler(@namespace: "Shop.Host"))
            .WithReferencedProject("Shop.Tenants.Domain", Domain("Tenants"))
            .WithReferencedProject(
                "Shop.Tenants.Application",
                project => project.WithSource(ModuleAttribute("Tenants"), "Module.cs").WithSource(Handler(), "SeatsOfMine.cs"))
            .WithReferencedProject(
                "Shop.Tenants.Api",
                project => project.WithSource(ModuleAttribute("Tenants"), "Module.cs").WithSource(Handler(@namespace: "Shop.Api"), "SeatsOfMine.cs"))
            .RunCore();

        host.ShouldCompile();
        host.ReportedDiagnostics.Should().BeEmpty("one module's class, seen through three of its projects, is one class");
        host.HintNames.Should().NotContain("TemplateFacade", "the class is the domain project's, and every project above sees that one");
        host.OutputCompilation.GetTypeByMetadataName(Default)!.ContainingAssembly.Name.Should().Be("Shop.Tenants.Domain");
    }

    [Fact]
    public void A_project_of_the_module_above_the_domain_project_writes_nothing_of_its_own()
    {
        var application = Project(Handler(), module: "Tenants")
            .WithReferencedProject("Shop.Tenants.Domain", Domain("Tenants"))
            .RunCore();

        application.ShouldCompile();
        application.HintNames.Should().NotContain("TemplateFacade");
        application.CompilationDiagnostics.Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Should().BeEmpty("one class of the name, the domain project's, and no warning that two compete");
    }

    [Fact]
    public void A_generator_of_another_library_in_a_project_above_reads_the_name_as_any_type()
    {
        // HotChocolate's reads [ObjectType<TenancyUseCases.SeatOverview>] this way. An alias written by a generator
        // would be the compiler's to see and not this one's, and it would write a type that does not exist.
        var api = Project(Described("TenancyUseCases.SeatOverview", "Shop.Api"), module: "Tenants")
            .WithReferencedProject("Shop.Tenants.Domain", Domain("Tenants"))
            .Run([.. GeneratorTestHost.CoreGenerators(), new DescribesGenerator()]);

        api.ShouldCompile();
        api.ShouldContain("SeatOverviewType.Describes", "// resolved");
        api.ShouldContain("SeatOverviewType.Describes", "typeof(global::DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<global::Shop.Domain.Tenant, ");
    }

    [Fact]
    public void A_generator_of_another_library_in_the_project_that_declares_the_classes_does_not_see_the_name()
    {
        // What one generator writes, the others of the same project do not see: the class is written there, in the
        // domain project, and read from the projects above it. Such a generator knows nothing of the type, so what it
        // writes is what the code spelled, and whether that compiles is up to that generator: HotChocolate's writes
        // typeof(TenancyUseCases.RoleSummary?) for a resolver that may answer nothing, which does not.
        var domain = Domain("Tenants")(Project(Described("TenancyUseCases.SeatOverview", "Shop.Domain")))
            .Run([.. GeneratorTestHost.CoreGenerators(), new DescribesGenerator()]);

        domain.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        domain.ShouldContain("SeatOverviewType.Describes", "// unresolved");
        domain.ShouldNotContain("SeatOverviewType.Describes", "DDDToolkit.Supporting.Tenancy.UseCases", "the generator could not tell what the name stands for");
    }

    [Fact]
    public void A_module_of_one_project_keeps_an_alias_of_that_name_and_every_generator_there_reads_it()
    {
        // The way out for a module of one project with GraphQL types over the records: one alias, of exactly the
        // name, which the generator stands back for. Every generator of the project reads an alias the code declares.
        var domain = Domain("Tenants")(Project(Described("TenancyUseCases.SeatOverview", "Shop.Domain")))
            .WithSource("global using TenancyUseCases = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .Run([.. GeneratorTestHost.CoreGenerators(), new DescribesGenerator()]);

        domain.ShouldCompile();
        domain.HintNames.Should().NotContain("TemplateFacade");
        domain.ReportedDiagnostics.Should().BeEmpty("the alias is the application's own way of naming the classes");
        domain.ShouldContain("SeatOverviewType.Describes", "// resolved");
        domain.ShouldContain("SeatOverviewType.Describes", "typeof(global::DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<global::Shop.Domain.Tenant, ");
    }

    [Fact]
    public void A_project_of_another_module_names_the_class_of_the_module_that_declares_the_classes()
    {
        var result = Project(Handler(@namespace: "Shop.Projects"), module: "Projects")
            .WithReferencedProject("Shop.Tenants.Domain", Domain("Tenants"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
        result.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_project_that_sees_only_the_ids_gets_nothing()
    {
        var result = Project("namespace Shop.Projects; public sealed class Nothing;", module: "Projects")
            .WithReferencedProject("Shop.Contracts", project => project.WithSource(Ids, "Ids.cs"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
        result.OutputCompilation.GetTypeByMetadataName(Default).Should().BeNull("which of its ids are the organization's cannot be told");
    }

    /// <summary>The first project of a module whose classes are split over two: the tenant, the organization and its units.</summary>
    private const string OrganizationHalf =
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
        """;

    /// <summary>The second project of that module, which references the first: the seat, the role and the invitation.</summary>
    private const string PeopleHalf =
        """
        using DDDToolkit.Supporting.Tenancy;
        using Shop.Contracts;

        namespace Shop.Domain;

        [SeatAggregate<SeatId>]
        public sealed partial class Seat;

        [RoleAggregate<RoleId>]
        public sealed partial class Role;

        [InvitationAggregate<InvitationId>]
        public sealed partial class Invitation;
        """;

    /// <summary>The first project of the split module, with what else it holds: a line naming the class, say.</summary>
    private static Func<GeneratorTestHost, GeneratorTestHost> OrganizationProject(string extra = "")
        => project => project.WithSource(Ids, "Ids.cs").WithSource(OrganizationHalf, "First.cs").WithSource(ModuleAttribute("Shop") + extra, "Module.cs");

    [Fact]
    public void Classes_split_over_two_projects_of_a_module_get_the_class_where_they_are_complete()
    {
        var first = OrganizationProject()(Project("namespace Shop.Organization; public sealed class Nothing;")).WithAssemblyName("Shop.Organization").RunCore();
        first.ShouldCompile();
        first.HintNames.Should().NotContain("TemplateFacade", "without a seat and a role there is nothing to close over yet");
        first.ReportedDiagnostics.Should().ContainSingle()
            .Which.Severity.Should().Be(DiagnosticSeverity.Info, "the first project of a split module is told, and nothing in it is wrong");

        var second = Project(PeopleHalf, module: "Shop")
            .WithReferencedProject("Shop.Organization", OrganizationProject())
            .WithSource(Handler(), "Handler.cs")
            .RunCore();
        second.ShouldCompile();
        second.ShouldContain("TenancyUseCases.TemplateFacade", "public abstract class TenancyUseCases : " + ClosedOverShop);
        second.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_module_split_over_two_projects_names_the_class_beside_the_module_in_either()
    {
        // The line beside [assembly: Module] in the first project, where the classes are not complete yet, names the
        // module's class: the second project, which writes it, reads it in the project it takes the classes from.
        var named = OrganizationProject(NamedItself("ShopTenancy"));
        var first = named(Project("namespace Shop.Organization; public sealed class Nothing;")).WithAssemblyName("Shop.Organization").RunCore();
        first.ShouldNotHaveDiagnostic("DDD00076");

        var second = Project(PeopleHalf, module: "Shop")
            .WithReferencedProject("Shop.Organization", named)
            .WithSource(Handler("ShopTenancy"), "Handler.cs")
            .RunCore();
        second.ShouldCompile();
        second.ReportedDiagnostics.Should().BeEmpty();
        second.ShouldContain("ShopTenancy.TemplateFacade", "public abstract class ShopTenancy : " + ClosedOverShop);
        second.ShouldContain("ShopTenancy.TemplateFacade", "/// Named by [assembly: TemplateFacadeName(\"TenancyUseCases\", \"ShopTenancy\")] in 'Shop.Organization'.");

        // A name the second project gives stands, and the first project's line, which now changes nothing, is told.
        var renamed = Project(PeopleHalf, module: "Shop")
            .WithReferencedProject("Shop.Organization", named)
            .WithSource(NamedItself("StoreTenancy"), "Named.cs")
            .WithSource(Handler("StoreTenancy"), "Handler.cs")
            .RunCore();
        renamed.ShouldCompile();
        renamed.ShouldHaveGenerated("StoreTenancy.TemplateFacade");
        renamed.ShouldHaveDiagnostic("DDD00076", at: "DDDToolkit.Abstractions.Attributes.TemplateFacadeName(\"TenancyUseCases\", \"StoreTenancy\")").GetMessage().Should().Be(
            "[assembly: TemplateFacadeName(\"TenancyUseCases\", \"ShopTenancy\")] changes nothing: it is in 'Shop.Organization', a project of the module this one "
            + "takes classes from, and names the class written here; a line of this project names it 'StoreTenancy' already, and that one stands; keep one");
    }

    // ------------------------------------------------------------------ two modules with the classes

    [Fact]
    public void A_host_that_sees_two_modules_with_the_classes_is_told_at_its_project_file_to_name_one_of_them()
    {
        // Each module's domain project gets TenancyUseCases and hears nothing: neither sees the other. They meet in the
        // host, which names neither here and compiles; the warning is what keeps the first line that does from being
        // the compiler's CS0433 with no word of what to do.
        var host = Project("namespace Host; public sealed class Nothing;")
            .WithReferencedProject("Customers.Domain", ModuleOfItsOwn("Customers"))
            .WithReferencedProjectBeside("Partners.Domain", ModuleOfItsOwn("Partners"))
            .RunCore();

        host.ShouldCompile();
        var told = host.GeneratorDiagnostics.Should().ContainSingle().Subject;
        told.Id.Should().Be("DDD00075");
        told.Severity.Should().Be(DiagnosticSeverity.Warning);
        told.Location.GetLineSpan().Path.Should().Be("src/DDDToolkit.Sample/DDDToolkit.Sample.csproj", "no line of the host is wrong, so it is said at the project file");
        told.GetMessage().Should().Be(
            "'TenancyUseCases' is the name of more than one class in this project: it sees the one 'Customers.Domain' has for the module Customers "
            + "and the one 'Partners.Domain' has for the module Partners. Give one of them a name of its own, with "
            + "[assembly: TemplateFacadeName(\"TenancyUseCases\", \"CustomersTenancyUseCases\")] in 'Customers.Domain'.");
    }

    [Fact]
    public void Without_a_name_of_its_own_the_host_cannot_name_either()
    {
        // What the warning is about: the first line that names the class is ambiguous.
        var host = Project(HandlerOf("Customers", Default))
            .WithReferencedProject("Customers.Domain", ModuleOfItsOwn("Customers"))
            .WithReferencedProjectBeside("Partners.Domain", ModuleOfItsOwn("Partners"))
            .RunCore();

        host.CompilationErrors.Select(error => error.Id).Should().Contain("CS0433", "the type exists in both modules' domain projects");
        host.ShouldHaveExactlyDiagnostics("DDD00075");
    }

    [Fact]
    public void One_line_in_one_modules_domain_project_names_its_class_and_the_host_names_each()
    {
        var host = Project(HandlerOf("Customers", "CustomersTenancyUseCases"))
            .WithSource(HandlerOf("Partners", Default), "Partners.cs")
            .WithReferencedProject("Customers.Domain", project => ModuleOfItsOwn("Customers")(project).WithSource(NamedItself("CustomersTenancyUseCases"), "Named.cs"))
            .WithReferencedProjectBeside("Partners.Domain", ModuleOfItsOwn("Partners"))
            .RunCore();

        host.ShouldCompile();
        host.ReportedDiagnostics.Should().BeEmpty("each module's class has a name of its own");
        TypeOf(host, "Host.Customers.SeatsOfMine", "Seats").ContainingType!.TypeArguments[0].ToDisplayString().Should().Be("Customers.Domain.Tenant");
        TypeOf(host, "Host.Partners.SeatsOfMine", "Seats").ContainingType!.TypeArguments[0].ToDisplayString().Should().Be("Partners.Domain.Tenant");
    }

    [Fact]
    public void A_module_that_declares_the_classes_and_sees_another_modules_class_of_its_name_gets_its_own_and_is_told_on_its_classes()
    {
        // Partners' domain project references Customers', which has a TenancyUseCases already. Partners' gets its own all
        // the same: C# binds the name in Partners' code to the class of its own source (CS0436), so that code is closed
        // over Partners' classes. Without it, Partners' code would be closed over Customers' classes with a warning only
        // here. It is told on its classes.
        var partners = Project(HandlerOf("Partners", Default))
            .WithAssemblyName("Partners.Domain")
            .WithSource(ModuleAttribute("Partners"), "Module.cs")
            .WithSource(Ids.Replace("Shop.Contracts", "Partners.Contracts"), "Ids.cs")
            .WithSource(Classes.Replace("Shop.Contracts", "Partners.Contracts").Replace("Shop.Domain", "Partners.Domain"), "Classes.cs")
            .WithReferencedProject("Customers.Domain", ModuleOfItsOwn("Customers"))
            .RunCore();

        partners.ShouldCompile();
        partners.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        TypeOf(partners, "Host.Partners.SeatsOfMine", "Seats").ContainingType!.TypeArguments[0].ToDisplayString().Should().Be("Partners.Domain.Tenant", "the module's own code names its own module's classes");
        partners.CompilationDiagnostics.Select(diagnostic => diagnostic.Id).Should().Contain("CS0436", "the compiler says which of the two it took");
        var told = partners.ShouldHaveDiagnostic("DDD00075", at: "Tenant");
        told.Severity.Should().Be(DiagnosticSeverity.Warning);
        told.GetMessage().Should().Be(
            "'TenancyUseCases' is the name of more than one class in this project: it gets one for the module Partners, which its own code names, and sees "
            + "the one 'Customers.Domain' has for the module Customers, which every project above it sees beside it. Give one of them a name of its own, "
            + "with [assembly: TemplateFacadeName(\"TenancyUseCases\", \"PartnersTenancyUseCases\")] in this project.");
        partners.Count("DDD00075").Should().Be(1, "the project file is not told the same again");

        // A project above sees both, is told at its project file, and cannot name either: nothing there is closed over
        // the wrong module's classes without a word.
        var above = Project(HandlerOf("Partners", Default), module: "Partners")
            .WithReferencedProject("Customers.Domain", ModuleOfItsOwn("Customers"))
            .WithReferencedProject("Partners.Domain", ModuleOfItsOwn("Partners"))
            .RunCore();
        above.CompilationErrors.Select(error => error.Id).Should().Contain("CS0433", "the type exists in both modules' domain projects");
        above.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00075")
            .Which.Location.GetLineSpan().Path.Should().Be("src/DDDToolkit.Sample/DDDToolkit.Sample.csproj", "no line of the project above is wrong");

        var named = Project(HandlerOf("Partners", "PartnersTenancyUseCases"))
            .WithAssemblyName("Partners.Domain")
            .WithSource(ModuleAttribute("Partners"), "Module.cs")
            .WithSource(NamedItself("PartnersTenancyUseCases"), "Named.cs")
            .WithSource(Ids.Replace("Shop.Contracts", "Partners.Contracts"), "Ids.cs")
            .WithSource(Classes.Replace("Shop.Contracts", "Partners.Contracts").Replace("Shop.Domain", "Partners.Domain"), "Classes.cs")
            .WithReferencedProject("Customers.Domain", ModuleOfItsOwn("Customers"))
            .RunCore();
        named.ShouldCompile();
        named.ReportedDiagnostics.Should().BeEmpty();
        named.ShouldHaveGenerated("PartnersTenancyUseCases.TemplateFacade");
    }

    [Fact]
    public void Three_modules_are_told_to_name_each_but_one()
    {
        var host = Project("namespace Host; public sealed class Nothing;")
            .WithReferencedProject("Customers.Domain", ModuleOfItsOwn("Customers"))
            .WithReferencedProjectBeside("Partners.Domain", ModuleOfItsOwn("Partners"))
            .WithReferencedProjectBeside("Suppliers.Domain", ModuleOfItsOwn("Suppliers"))
            .RunCore();

        host.ShouldCompile();
        host.GeneratorDiagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain(
            "'Customers.Domain' has for the module Customers, the one 'Partners.Domain' has for the module Partners and the one 'Suppliers.Domain' has "
            + "for the module Suppliers. Give each of them but one a name of its own");
    }

    [Fact]
    public void A_name_one_project_gives_asks_no_project_above_it_for_a_class()
    {
        // A host that declares a second module's classes and sees the first module's domain project, where the
        // application named that module's class: the package asks for its own types, and the host gets its own class
        // under the package's name, and no second one under the name the other project chose.
        var host = Project(HandlerOf("Customers", "CustomersUseCases"))
            .WithReferencedProject("Customers.Domain", project => ModuleOfItsOwn("Customers")(project).WithSource(NamedItself("CustomersUseCases"), "Named.cs"))
            .WithSource(Ids.Replace("Shop.Contracts", "Partners.Contracts"), "Ids.cs")
            .WithSource(Classes.Replace("Shop.Contracts", "Partners.Contracts").Replace("Shop.Domain", "Partners.Domain"), "Classes.cs")
            .WithSource(ModuleAttribute("Partners"), "Module.cs")
            .RunCore();

        host.ShouldCompile();
        host.ReportedDiagnostics.Should().BeEmpty();
        host.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        host.GeneratedSources.Where(source => source.HintName.EndsWith(".TemplateFacade.g.cs", StringComparison.Ordinal)).Should().ContainSingle();
    }

    // ------------------------------------------------------------------ what keeps it from being written

    [Fact]
    public void A_module_without_a_class_of_a_template_gets_no_class_and_is_told_which_where_the_classes_are()
    {
        // Above the domain project the name would only be missing, CS0246, and AddTenancy's DDD00049 is reported in
        // the infrastructure project, which does not build once the application project fails. So it is said here.
        // The invitation is left out too: its parent takes the seat's id, so without a seat it is DDD00044 already.
        var result = Project(Without(Classes, SeatLines.Concat(InvitationLines).ToArray()), module: "Shop")
            .WithSource(Ids, "Ids.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
        var told = result.ShouldHaveDiagnostic("DDD00065", at: "Tenant");
        told.Severity.Should().Be(DiagnosticSeverity.Info, "a module still declaring its classes, or split over two projects, is not warned");
        told.GetMessage().Should().Be(
            "'TenancyUseCases' is not written, so no project can name the types nested in TenancyUseCases through it: no class is declared with "
            + "[SeatAggregate], in this project or in a project of the module Shop that it references");
        result.Count("DDD00065").Should().Be(1);
    }

    [Fact]
    public void A_template_a_parent_of_the_project_takes_from_is_that_parents_error_and_is_not_said_twice()
    {
        var result = Project(Without(Classes, "[OrganizationUnit<OrganizationUnitId>]", "public sealed partial class OrganizationUnit;"), module: "Shop")
            .WithSource(Ids, "Ids.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00044", at: "Organization");
        result.ShouldNotHaveDiagnostic("DDD00065");
        result.HintNames.Should().NotContain("TemplateFacade");
    }

    [Fact]
    public void A_module_with_two_classes_of_one_template_gets_no_class_and_is_told_which()
    {
        // Without the invitation, whose parent takes the seat's id and would say so as DDD00045.
        var result = Project(
                """
                using DDDToolkit.Supporting.Tenancy;
                using Shop.Contracts;

                namespace Shop.Domain.Other;

                [SeatAggregate<SeatId>]
                public sealed partial class Seat;
                """,
                module: "Shop")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Without(Classes, InvitationLines), "Classes.cs")
            .RunCore();

        result.HintNames.Should().NotContain("TemplateFacade", "there is no telling which seat is meant");
        result.CompilationErrors.Should().NotContain(error => error.Location.SourceTree != null && error.Location.SourceTree.FilePath.Contains("TemplateFacade"));
        result.ShouldHaveDiagnostic("DDD00065", at: "Seat").GetMessage().Should().EndWith(
            "several classes are declared with [SeatAggregate], 'Shop.Domain.Other.Seat' and 'Shop.Domain.Seat', and it is closed over one; keep one");
    }

    [Fact]
    public void A_class_that_cannot_be_generated_leaves_the_module_without_one_and_without_a_second_error()
    {
        var result = Project(Classes.Replace("[SeatAggregate<SeatId>]", "[SeatAggregate<System.Guid>]"), module: "Shop")
            .WithSource(Ids, "Ids.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00043", at: "Seat");
        result.ShouldNotHaveDiagnostic("DDD00065");
        result.HintNames.Should().NotContain("TemplateFacade");
        result.CompilationErrors.Should().BeEmpty("the class would otherwise be a compile error inside generated code, beside the seat's own diagnostic");
    }

    [Fact]
    public void A_module_name_that_is_no_xml_is_written_into_the_documentation_as_text()
    {
        // The module's name is the application's free text, and a project that builds its documentation reads the
        // class's summary as XML: an ampersand or an angle bracket in it would be CS1570 in code nobody wrote.
        var result = Domain("R&D <Lab>")(Project("namespace Shop.Application; public sealed class Nothing;")).RunCore();

        result.ShouldCompile();
        var source = result.GeneratedSources.Single(generated => generated.HintName.EndsWith("TenancyUseCases.TemplateFacade.g.cs", StringComparison.Ordinal)).SourceText.ToString();
        source.Should().Contain("closed over the classes of the module R&amp;D &lt;Lab&gt;: ");
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose), cancellationToken: TestContext.Current.CancellationToken)
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Should().BeEmpty("the summary is well-formed XML");
    }

    [Fact]
    public void A_class_closed_over_an_internal_class_is_internal()
    {
        // A public class cannot derive over an internal one; an internal one reaches as far as that class does.
        var result = Project(Classes.Replace("public sealed partial class Seat;", "internal sealed partial class Seat;"), module: "Shop")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Handler().Replace("public sealed class SeatsOfMine", "internal sealed class SeatsOfMine"), "Handler.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("TenancyUseCases.TemplateFacade", "internal abstract class TenancyUseCases : " + ClosedOverShop);
    }

    [Fact]
    public void An_alias_the_project_writes_itself_under_the_same_name_stays_its_own()
    {
        // The form every project wrote before: the generator stands back, and the compiler is not handed two.
        var result = Domain("Shop")(Project(Handler()))
            .WithSource("global using TenancyUseCases = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
    }

    [Fact]
    public void An_alias_of_the_same_name_in_a_project_above_is_the_compilers_error()
    {
        // The one thing a project that wrote the alias before has to do: take it out. The compiler names it.
        var result = Project(Handler(), module: "Shop")
            .WithReferencedProject("Shop.Domain", Domain("Shop"))
            .WithSource("global using TenancyUseCases = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();

        result.CompilationErrors.Select(error => error.Id).Should().Contain("CS0576", "an alias and a class of one name in the global namespace are two things the compiler cannot tell apart");
    }

    [Fact]
    public void An_alias_under_another_name_stands_beside_the_class()
    {
        var result = Domain("Shop")(Project(Handler()))
            .WithSource(Handler("SampleTenancy", "Shop.Old"), "Old.cs")
            .WithSource("global using SampleTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        SymbolEqualityComparer.Default.Equals(TypeOf(result, "Shop.Application.SeatsOfMine", "Seats"), TypeOf(result, "Shop.Old.SeatsOfMine", "Seats"))
            .Should().BeTrue("both names stand for the one type");
    }

    [Fact]
    public void A_type_of_that_name_in_the_global_namespace_keeps_the_name()
    {
        var result = Domain("Shop")(Project("public static class TenancyUseCases { public const int Mine = 1; }"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade", "a second type of the name would be the compiler's error");
        result.ReportedDiagnostics.Should().BeEmpty("the type is the application's own, and not a class written for another module");
    }

    [Fact]
    public void A_namespace_of_that_name_closer_to_the_code_hides_the_class_there_only()
    {
        var result = Domain("Shop")(Project(Handler()))
            .WithSource(
                """
                namespace Shop.Reports.TenancyUseCases
                {
                    public sealed class SeatCommands;
                }

                namespace Shop.Reports
                {
                    public sealed class Report(TenancyUseCases.SeatCommands mine)
                    {
                        public TenancyUseCases.SeatCommands Mine => mine;
                    }
                }
                """,
                "Reports.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        TypeOf(result, "Shop.Reports.Report", "Mine").ContainingAssembly.Name.Should().Be(GeneratorTestHost.DefaultAssemblyName, "C# finds the nearer name first");
    }

    [Fact]
    public void A_type_of_that_name_the_project_declares_in_a_namespace_keeps_the_class_out_and_is_told_where()
    {
        // C# looks in the global namespace before it looks in what a file imports, so a class written there would be
        // what TenancyUseCases means in every file that imports Shop.Settings: code that compiled would stop compiling.
        var result = Domain("Shop")(Project(
                """
                using Shop.Settings;

                namespace Shop.Application;

                public static class Uses
                {
                    public const string Slug = TenancyUseCases.DefaultSlug;
                }
                """))
            .WithSource("namespace Shop.Settings; public static class TenancyUseCases { public const string DefaultSlug = \"shop\"; }", "Settings.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
        var told = result.ShouldHaveDiagnostic("DDD00065", at: "TenancyUseCases");
        told.Location.GetLineSpan().Path.Should().Be("Settings.cs", "it is said on the type the class would hide");
        told.GetMessage().Should().EndWith(
            "this project declares 'Shop.Settings.TenancyUseCases', which a class of that name in the global namespace would hide in every file that imports "
            + "'Shop.Settings' with a using; name the class otherwise, with [assembly: TemplateFacadeName(\"TenancyUseCases\", \"...\")] in "
            + "this project, or rename 'TenancyUseCases'");
    }

    [Fact]
    public void A_type_of_that_name_nested_in_another_is_no_matter()
    {
        var result = Domain("Shop")(Project(Handler()))
            .WithSource("namespace Shop.Settings; public static class Defaults { public static class TenancyUseCases { public const string Slug = \"shop\"; } }", "Settings.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        result.ReportedDiagnostics.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ a name of the application's choosing

    [Fact]
    public void The_project_that_declares_the_classes_names_the_class_itself_in_one_line()
    {
        // One attribute beside [assembly: Module] names it, and every project above names the use cases by that name,
        // writing nothing.
        var application = Project(Handler("ShopTenancy"), module: "Shop")
            .WithReferencedProject("Shop.Domain", project => Domain("Shop")(project).WithSource(NamedItself("ShopTenancy"), "Named.cs"))
            .RunCore();

        application.ShouldCompile();
        application.ReportedDiagnostics.Should().BeEmpty();
        application.HintNames.Should().NotContain("TemplateFacade", "the domain project wrote it");
        application.OutputCompilation.GetTypeByMetadataName("ShopTenancy")!.ContainingAssembly.Name.Should().Be("Shop.Domain");
        application.OutputCompilation.GetTypeByMetadataName(Default).Should().BeNull("the application's name stands instead of the package's");
    }

    [Fact]
    public void The_class_a_name_was_given_says_where_its_name_comes_from()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy"))).WithSource(NamedItself("ShopTenancy"), "Named.cs").RunCore();

        result.ShouldCompile();
        result.ShouldContain("ShopTenancy.TemplateFacade", "public abstract class ShopTenancy : " + ClosedOverShop);
        result.ShouldContain("ShopTenancy.TemplateFacade", "/// Named by [assembly: TemplateFacadeName(\"TenancyUseCases\", \"ShopTenancy\")] in this project.");
        result.HintNames.Should().NotContain("TenancyUseCases.TemplateFacade");
    }

    [Fact]
    public void A_line_may_name_the_class_with_its_namespace()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource(NamedItself("ShopTenancy", "DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases"), "Named.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldHaveGenerated("ShopTenancy.TemplateFacade");
    }

    [Fact]
    public void A_name_the_application_gives_stands_back_for_an_alias_of_its_own_as_the_packages_does()
    {
        // The project's own alias of the name is the application's way of naming the classes.
        var aliased = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .WithSource("global using ShopTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();
        aliased.ShouldCompile();
        aliased.HintNames.Should().NotContain("TemplateFacade");
        aliased.ReportedDiagnostics.Should().BeEmpty();

        var hidden = Domain("Shop")(Project("namespace Shop.Settings; public static class ShopTenancy { public const int Mine = 1; }"))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .RunCore();
        hidden.ShouldCompile();
        hidden.HintNames.Should().NotContain("TemplateFacade");
        hidden.ShouldHaveDiagnostic("DDD00065", at: "ShopTenancy");

        var above = Project(Handler("ShopTenancy"), module: "Shop")
            .WithReferencedProject("Shop.Domain", project => Domain("Shop")(project).WithSource(NamedItself("ShopTenancy"), "Named.cs"))
            .WithSource("global using ShopTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();
        above.CompilationErrors.Select(error => error.Id).Should().Contain("CS0576", "an alias above of the name the application gave is in the class's way as one of the package's name is");
    }

    [Fact]
    public void A_name_given_that_a_namespace_or_a_global_type_has_changes_nothing_and_is_told_at_the_line()
    {
        // The module's root namespace is the short name that comes to mind. A class cannot have it beside the namespace,
        // and the projects above would only hear CS0234, that the namespace has no SeatCommands.
        var rooted = ModuleOfItsOwn("Customers")(Project("namespace Customers.Application; public sealed class Nothing;"))
            .WithSource(NamedItself("Customers"), "Named.cs")
            .RunCore();
        rooted.ShouldCompile();
        rooted.HintNames.Should().NotContain("TemplateFacade");
        rooted.ShouldHaveDiagnostic("DDD00076", at: "DDDToolkit.Abstractions.Attributes.TemplateFacadeName(\"TenancyUseCases\", \"Customers\")").GetMessage().Should().Be(
            "[assembly: TemplateFacadeName(\"TenancyUseCases\", \"Customers\")] changes nothing: 'Customers' is the name of a namespace this project sees, "
            + "which the class cannot have as well; give another name");

        var typed = Domain("Shop")(Project("public static class ShopTenancy { public const int Mine = 1; }"))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .RunCore();
        typed.ShouldCompile();
        typed.HintNames.Should().NotContain("TemplateFacade");
        typed.ShouldHaveDiagnostic("DDD00076", at: "DDDToolkit.Abstractions.Attributes.TemplateFacadeName(\"TenancyUseCases\", \"ShopTenancy\")").GetMessage().Should().EndWith(
            "changes nothing: 'ShopTenancy' is the name of a type in the global namespace this project sees, which the class cannot have as well; give another name");
    }

    [Fact]
    public void Where_the_application_named_the_class_already_each_message_asks_for_another_name_in_that_line()
    {
        // A second line for the class would change nothing, DDD00076, so the line to change is the one there.
        var hidden = Domain("Shop")(Project("namespace Shop.Settings; public static class ShopTenancy { public const int Mine = 1; }"))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .RunCore();
        hidden.ShouldHaveDiagnostic("DDD00065", at: "ShopTenancy").GetMessage().Should().EndWith(
            "name the class otherwise, with another name in [assembly: TemplateFacadeName(\"TenancyUseCases\", \"ShopTenancy\")] in this project, "
            + "or rename 'ShopTenancy'");

        var partners = ModuleOfItsOwn("Partners")(Project("namespace Partners.Application; public sealed class Nothing;").WithAssemblyName("Partners.Domain"))
            .WithSource(NamedItself("SharedTenancy"), "Named.cs")
            .WithReferencedProject("Customers.Domain", project => ModuleOfItsOwn("Customers")(project).WithSource(NamedItself("SharedTenancy"), "Named.cs"))
            .RunCore();
        partners.ShouldHaveDiagnostic("DDD00075", at: "Tenant").GetMessage().Should().EndWith(
            "Give one of them a name of its own, with another name in [assembly: TemplateFacadeName(\"TenancyUseCases\", \"SharedTenancy\")] in this project.");

        var host = Project("namespace Host; public sealed class Nothing;")
            .WithReferencedProject("Customers.Domain", project => ModuleOfItsOwn("Customers")(project).WithSource(NamedItself("SharedTenancy"), "Named.cs"))
            .WithReferencedProjectBeside("Partners.Domain", project => ModuleOfItsOwn("Partners")(project).WithSource(NamedItself("SharedTenancy"), "Named.cs"))
            .RunCore();
        host.GeneratorDiagnostics.Should().ContainSingle().Which.GetMessage().Should().EndWith(
            "Give one of them a name of its own, with another name in [assembly: TemplateFacadeName(\"TenancyUseCases\", \"SharedTenancy\")] in 'Customers.Domain'.");

        var twice = GeneratorTestHost.Create(Package.Replace(", IAudited", string.Empty), "Package.cs")
            .WithSource(Accounts.Replace("BillingUseCases.Statement Statement(AccountId id) => new(id, 0m);", "int Nothing => 0;"), "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .WithSource(NamedItself("Shared", "BillingUseCases") + NamedItself("Shared", "Ledger"), "Named.cs")
            .RunCore();
        twice.ShouldHaveDiagnostic("DDD00075", at: "Account").GetMessage().Should().EndWith(
            "Give one of them a name of its own, with another name in [assembly: TemplateFacadeName(\"BillingUseCases\", \"Shared\")] in this project.");
    }

    [Fact]
    public void A_line_that_names_no_class_a_package_asks_for_changes_nothing_and_is_told_what_it_can_name()
    {
        var result = Domain("Shop")(Project(Handler())).WithSource(NamedItself("ShopTenancy", "TenancyUsecases"), "Named.cs").RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        var told = result.ShouldHaveDiagnostic("DDD00076", at: "DDDToolkit.Abstractions.Attributes.TemplateFacadeName(\"TenancyUsecases\", \"ShopTenancy\")");
        told.Severity.Should().Be(DiagnosticSeverity.Warning);
        told.GetMessage().Should().Be(
            "[assembly: TemplateFacadeName(\"TenancyUsecases\", \"ShopTenancy\")] changes nothing: no package this project references asks for a class of that "
            + "name; it can name 'TenancyUseCases'");
    }

    [Fact]
    public void A_line_in_a_project_above_the_classes_changes_nothing_and_is_told_where_it_goes()
    {
        var application = Project(Handler(), module: "Shop")
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .WithReferencedProject("Shop.Domain", Domain("Shop"))
            .RunCore();

        application.ShouldCompile();
        application.HintNames.Should().NotContain("TemplateFacade");
        application.ShouldHaveDiagnostic("DDD00076", at: "DDDToolkit.Abstractions.Attributes.TemplateFacadeName(\"TenancyUseCases\", \"ShopTenancy\")").GetMessage().Should().EndWith(
            "changes nothing: this project declares no class with the templates of TenancyUseCases, so it gets no class of it to name; the line goes in the "
            + "project that declares them, where the class is written");
    }

    [Fact]
    public void A_line_with_no_name_a_class_can_have_or_a_second_line_for_one_class_changes_nothing()
    {
        var keyword = Domain("Shop")(Project(Handler())).WithSource(NamedItself("class"), "Named.cs").RunCore();
        keyword.ShouldCompile();
        keyword.ShouldHaveGenerated("TenancyUseCases.TemplateFacade");
        keyword.ReportedDiagnostics.Should().ContainSingle().Which.GetMessage().Should().EndWith("changes nothing: 'class' is no name a class can have");

        var twice = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource(NamedItself("ShopTenancy") + NamedItself("StoreTenancy"), "Named.cs")
            .RunCore();
        twice.ShouldCompile();
        twice.ShouldHaveGenerated("ShopTenancy.TemplateFacade");
        twice.ReportedDiagnostics.Should().ContainSingle().Which.GetMessage().Should().EndWith(
            "changes nothing: a line before it names TenancyUseCases 'ShopTenancy' already, and that one stands; keep one");
    }

    // ------------------------------------------------------------------ what a package can ask

    /// <summary>A made-up package with several classes it asks for: one every application can have, and ones it cannot.</summary>
    private const string Package =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateFacade(typeof(Acme.Billing.BillingUseCases<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Ledger<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Open<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.BillingUseCases<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Statics<,>))]
        [assembly: TemplateFacade(typeof(Acme.Billing.Closed<,>))]

        namespace Acme.Billing;

        [AggregateRootBase]
        public abstract partial class AccountAggregate<TAccountId>
            where TAccountId : IEntityId, IEquatable<TAccountId>
        {
            protected AccountAggregate(TAccountId id) : base(id) { }
        }

        [AggregateRootTemplate(typeof(AccountAggregate<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class AccountAttribute<TAccountId> : Attribute;

        public interface IAudited;

        public abstract class BillingUseCases<
            [TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount,
            [TemplateType(typeof(AccountAttribute<>))] TAccountId>
            where TAccount : AccountAggregate<TAccountId>
            where TAccountId : struct, IEntityId, IEquatable<TAccountId>
        {
            public sealed record Statement(TAccountId Account, decimal Balance);
        }

        // Asks more of the account than its template gives it.
        public abstract class Ledger<
            [TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount,
            [TemplateType(typeof(AccountAttribute<>))] TAccountId>
            where TAccount : AccountAggregate<TAccountId>, IAudited
            where TAccountId : struct, IEntityId, IEquatable<TAccountId>
        {
            public sealed record Line(TAccountId Account);
        }

        // Leaves a type parameter for the application to choose, which a class closed over the classes cannot.
        public abstract class Open<[TemplateType(typeof(AccountAttribute<>))] TAccountId, TCurrency>
            where TAccountId : struct, IEntityId, IEquatable<TAccountId>
        {
            public sealed record Amount(TAccountId Account, TCurrency Currency);
        }

        // Static, and sealed with a constructor of its own: nothing derives from either.
        public static class Statics<[TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount, [TemplateType(typeof(AccountAttribute<>))] TAccountId>;

        public sealed class Closed<[TemplateType(typeof(AccountAttribute<>), Take = TemplateArgumentKind.Type)] TAccount, [TemplateType(typeof(AccountAttribute<>))] TAccountId>
        {
            private Closed() { }
        }
        """;

    private const string Accounts =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using Acme.Billing;

        namespace Shop.Billing;

        [EntityId<Guid>]
        public readonly partial record struct AccountId;

        [Account<AccountId>]
        public sealed partial class Account
        {
            public Account(AccountId id) : base(id) { }
        }

        public static class Uses
        {
            public static BillingUseCases.Statement Statement(AccountId id) => new(id, 0m);
        }
        """;

    [Fact]
    public void A_package_of_its_own_gets_its_class_and_one_its_classes_do_not_fit_is_told_and_its_own_mistakes_pass_without_a_word()
    {
        // The package is compiled with the application here, its generic class in a namespace of the project: a class
        // of the name with type parameters is no class the generated one would hide.
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Accounts, "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("BillingUseCases.TemplateFacade", "public abstract class BillingUseCases : global::Acme.Billing.BillingUseCases<global::Shop.Billing.Account, global::Shop.Billing.AccountId>");
        result.HintNames.Should().NotContain("Ledger.TemplateFacade", "the account does not meet what the ledger asks of it, and a compile error in generated code would fix nothing");
        result.ShouldHaveDiagnostic("DDD00065", at: "Account").GetMessage().Should().Be(
            "'Ledger' is not written, so no project can name the types nested in Ledger through it: it takes 'Account' as 'TAccount', which requires 'IAudited'; 'Account' does not meet it");
        result.ReportedDiagnostics.Should().ContainSingle("what is the package's own mistake is the package's tests' to show, and a type asked twice is asked once");
        result.HintNames.Should().NotContain("Open.TemplateFacade", "the class leaves no type parameter open");
        result.HintNames.Should().NotContain("Statics.TemplateFacade").And.NotContain("Closed.TemplateFacade", "only a class that can be derived from names its nested types through another");
    }

    [Fact]
    public void Two_classes_one_project_gets_that_come_to_one_name_are_both_left_out_and_told()
    {
        // A name the application gives the ledger that the package's other class has already.
        var result = GeneratorTestHost.Create(Package.Replace(", IAudited", string.Empty), "Package.cs")
            .WithSource(Accounts.Replace("BillingUseCases.Statement Statement(AccountId id) => new(id, 0m);", "int Nothing => 0;"), "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .WithSource(NamedItself("BillingUseCases", "Ledger"), "Named.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade", "neither could be told from the other");
        result.ShouldHaveDiagnostic("DDD00075", at: "Account").GetMessage().Should().Be(
            "'BillingUseCases' is the name of more than one class in this project: it would get the one for Acme.Billing.BillingUseCases<,> and the one for "
            + "Acme.Billing.Ledger<,>, and gets neither. Give one of them a name of its own, with [assembly: TemplateFacadeName(\"BillingUseCases\", \"...\")] "
            + "in this project.");
    }

    // ------------------------------------------------------------------ incremental

    [Fact]
    public void An_edit_that_changes_no_class_writes_nothing_anew()
    {
        var first = Domain("Shop")(Project(Handler())).RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) =>
        {
            var tree = compilation.SyntaxTrees.First();
            return compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(SourceText.From(tree.GetText() + "\n// A comment that concerns nobody.\n", Encoding.UTF8), parseOptions, tree.FilePath));
        });

        second.ShouldCompile();
        var reasons = second.OutputStepReasons();
        reasons.Should().NotBeEmpty("the driver must be tracking steps, or this means nothing");
        reasons.Should().OnlyContain(
            step => step.Reason == IncrementalStepRunReason.Cached || step.Reason == IncrementalStepRunReason.Unchanged,
            "the class is worked out on every edit, and compares equal while the classes are the same");
    }

    [Fact]
    public void A_name_given_anew_writes_the_class_anew_under_it()
    {
        // The control for the test above: a change that matters shows.
        var first = Domain("Shop")(Project(Handler())).RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) =>
        {
            var handler = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Source.cs");
            return compilation
                .ReplaceSyntaxTree(handler, CSharpSyntaxTree.ParseText(SourceText.From(Handler("StoreTenancy"), Encoding.UTF8), parseOptions, handler.FilePath))
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(NamedItself("StoreTenancy"), Encoding.UTF8), parseOptions, "Named.cs"));
        });

        second.ShouldCompile();
        second.OutputStepReasons().Should().Contain(step => step.Reason == IncrementalStepRunReason.New || step.Reason == IncrementalStepRunReason.Modified);
        second.ShouldHaveGenerated("StoreTenancy.TemplateFacade");
        second.HintNames.Should().NotContain("TenancyUseCases.TemplateFacade");
    }

    // ------------------------------------------------------------------ another library's generator

    /// <summary>A class that names a type in an attribute, as an API project names a record in HotChocolate's <c>[ObjectType&lt;T&gt;]</c>.</summary>
    private static string Described(string type, string @namespace)
        => $$"""
             namespace {{@namespace}}
             {
                 [global::Shop.Describes<{{type}}>]
                 public static partial class SeatOverviewType;
             }

             namespace Shop
             {
                 [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                 public sealed class DescribesAttribute<T> : global::System.Attribute;
             }
             """;

    /// <summary>
    /// A generator of another library, as HotChocolate's is: it reads the type a class names in an attribute and
    /// writes it out in full, beside whether it could resolve it.
    /// </summary>
    private sealed class DescribesGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var described = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Shop.DescribesAttribute`1",
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) =>
                {
                    var type = attributeContext.Attributes[0].AttributeClass!.TypeArguments[0];
                    return (
                        Class: attributeContext.TargetSymbol.Name,
                        Namespace: attributeContext.TargetSymbol.ContainingNamespace.ToDisplayString(),
                        Type: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Resolved: type.TypeKind != TypeKind.Error);
                });

            context.RegisterSourceOutput(described, static (production, each) => production.AddSource(
                each.Class + ".Describes.g.cs",
                "// " + (each.Resolved ? "resolved" : "unresolved") + "\nnamespace " + each.Namespace + ";\n\npartial class " + each.Class
                + "\n{\n    public static readonly global::System.Type Described = typeof(" + each.Type + ");\n}\n"));
        }
    }
}
