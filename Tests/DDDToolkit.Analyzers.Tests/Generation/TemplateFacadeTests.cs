using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A package asks, with <c>[assembly: TemplateFacade]</c>, that the project that declares the application's classes
/// gets a generic class of the package's closed over them, as a class of its own named after its module: Tenancy's
/// use cases, nested in a class generic over nine of the application's classes and ids, are
/// <c>ShopTenancy.SeatCommands</c> there and in every project above it, and no project writes the nine types.
/// <para>
/// Most of these use the real Tenancy package, seen through metadata as an application sees it, and a module split
/// by layer the way the sample is: each project of it is compiled with what the generators wrote, and names the
/// use cases and their records itself, so a class that is missing, or closed over the wrong classes, fails to
/// compile there.
/// </para>
/// <para>
/// Where the class cannot be written the project that declares the classes says why, DDD00065, since the projects
/// above would only hear that the name does not exist; and that project may name the class itself.
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

    /// <summary>
    /// A handler of the application's: it takes two use cases, among them the invitations' closed over the module's
    /// invitation class as well, and answers two records of theirs, through the class's name alone.
    /// </summary>
    private static string Handler(string name, string @namespace = "Shop.Application")
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

    /// <summary>What an application's class names Tenancy's use cases with, in place of the module's name.</summary>
    private static string NamedItself(string name)
        => "[assembly: DDDToolkit.Abstractions.Attributes.TemplateFacade(typeof(DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<,,,,,,,,>), \"" + name + "\")]\n";

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
    public void The_project_that_declares_the_classes_gets_a_class_named_after_its_module()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy"))).RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("ShopTenancy.TemplateFacade", "public abstract class ShopTenancy : " + ClosedOverShop);
        result.ShouldContain("ShopTenancy.TemplateFacade", "    private ShopTenancy()");
        result.ShouldContain("ShopTenancy.TemplateFacade", "/// TenancyUseCases, closed over the classes of the module Shop: Tenant, TenantId, Organization, OrganizationUnit, OrganizationUnitId, Seat, SeatId, Role and RoleId.");
    }

    [Fact]
    public void What_it_names_is_the_packages_own_nested_type_and_the_class_is_only_a_name()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy"))).RunCore();

        result.ShouldCompile();
        var seats = TypeOf(result, "Shop.Application.SeatsOfMine", "Seats");
        seats.ContainingAssembly.Name.Should().Be("DDDToolkit.Supporting.Tenancy", "what the container registered is the package's own type");
        seats.ContainingType!.OriginalDefinition.ToDisplayString().Should().StartWith("DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<");
        seats.ContainingType.TypeArguments.Select(argument => argument.Name).Should().Equal("Tenant", "TenantId", "Organization", "OrganizationUnit", "OrganizationUnitId", "Seat", "SeatId", "Role", "RoleId");
        TypeOf(result, "Shop.Application.SeatsOfMine", "Invitations").TypeArguments.Select(argument => argument.Name).Should().Equal("Invitation", "InvitationId");

        var facade = result.OutputCompilation.GetTypeByMetadataName("ShopTenancy")!;
        facade.ContainingNamespace.IsGlobalNamespace.Should().BeTrue("no project needs a using for it");
        facade.DeclaredAccessibility.Should().Be(Accessibility.Public, "the projects above see it");
        facade.IsAbstract.Should().BeTrue();
        facade.InstanceConstructors.Should().ContainSingle().Which.DeclaredAccessibility.Should().Be(Accessibility.Private, "nothing makes one, and nothing derives from it");
        SymbolEqualityComparer.Default.Equals(facade.BaseType, seats.ContainingType).Should().BeTrue();
    }

    [Fact]
    public void A_module_name_is_written_as_the_generators_write_it_in_code()
    {
        var result = Domain("order-management")(Project(Handler("OrderManagementTenancy"))).RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("OrderManagementTenancy.TemplateFacade");
    }

    [Fact]
    public void Without_a_module_the_class_is_named_as_every_generated_name_of_the_project_is()
    {
        // DDD_Module, the default beneath [assembly: Module]: written once, so the projects above see the same name.
        var named = Domain(module: null)(Project(Handler("ShopTenancy"))).WithAssemblyName("Shop.Domain").WithModule("Shop").RunCore();
        named.ShouldCompile();
        named.ShouldHaveGenerated("ShopTenancy.TemplateFacade");

        // And otherwise the assembly, without the dots.
        var unnamed = Domain(module: null)(Project(Handler("ShopDomainTenancy"))).WithAssemblyName("Shop.Domain").RunCore();
        unnamed.ShouldCompile();
        unnamed.ShouldHaveGenerated("ShopDomainTenancy.TemplateFacade");

        var above = Project(Handler("ShopTenancy"))
            .WithReferencedProject("Shop.Domain", project => Domain(module: null)(project).WithModule("Shop"))
            .WithModule("Elsewhere")
            .RunCore();
        above.ShouldCompile();
        above.HintNames.Should().NotContain("TemplateFacade", "the project above declares nothing, and names the class the domain project was given");
    }

    [Fact]
    public void A_module_its_folder_declares_names_the_class_as_one_its_attribute_declares()
    {
        // DDD_Module, as a Directory.Build.props sets it for a module's folder: the
        // build declares the module, and the class is named after it in the domain project, as the sample's
        // TenantsTenancy is, and seen by the module's projects above, which write none of their own.
        var domain = Domain(module: null)(Project(Handler("TenantsTenancy"))).WithAssemblyName("Shop.Tenants.Domain").WithModuleFromTheBuild("Tenants").RunCore();
        domain.ShouldCompile();
        domain.ShouldHaveGenerated("TenantsTenancy.TemplateFacade");

        var application = Project(Handler("TenantsTenancy")).WithAssemblyName("Shop.Tenants.Application").WithModuleFromTheBuild("Tenants")
            .WithReferencedProject("Shop.Tenants.Domain", project => Domain(module: null)(project).WithModuleFromTheBuild("Tenants"))
            .RunCore();
        application.ShouldCompile();
        application.HintNames.Should().NotContain("TemplateFacade", "the class is the domain project's, and every project of the module above sees that one");
        application.OutputCompilation.GetTypeByMetadataName("TenantsTenancy")!.ContainingAssembly.Name.Should().Be("Shop.Tenants.Domain");
    }

    // ------------------------------------------------------------------ every project above them

    [Fact]
    public void Every_project_of_a_module_split_by_layer_names_the_use_cases_and_their_records_without_writing_anything()
    {
        // Each project is compiled with what the generators wrote into it, and fails to unless it sees the class.
        var host = Project(Handler("TenantsTenancy", "Shop.Host"))
            .WithReferencedProject("Shop.Tenants.Domain", Domain("Tenants"))
            .WithReferencedProject(
                "Shop.Tenants.Application",
                project => project.WithSource(ModuleAttribute("Tenants"), "Module.cs").WithSource(Handler("TenantsTenancy"), "SeatsOfMine.cs"))
            .WithReferencedProject(
                "Shop.Tenants.Api",
                project => project.WithSource(ModuleAttribute("Tenants"), "Module.cs").WithSource(Handler("TenantsTenancy", "Shop.Api"), "SeatsOfMine.cs"))
            .RunCore();

        host.ShouldCompile();
        host.ReportedDiagnostics.Should().BeEmpty();
        host.HintNames.Should().NotContain("TemplateFacade", "the class is the domain project's, and every project above sees that one");
        host.OutputCompilation.GetTypeByMetadataName("TenantsTenancy")!.ContainingAssembly.Name.Should().Be("Shop.Tenants.Domain");
    }

    [Fact]
    public void A_project_of_the_module_above_the_domain_project_writes_nothing_of_its_own()
    {
        var application = Project(Handler("TenantsTenancy"), module: "Tenants")
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
        // HotChocolate's reads [ObjectType<TenantsTenancy.SeatOverview>] this way. An alias written by a generator
        // would be the compiler's to see and not this one's, and it would write a type that does not exist.
        var api = Project(Described("TenantsTenancy.SeatOverview", "Shop.Api"), module: "Tenants")
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
        // typeof(TenantsTenancy.RoleSummary?) for a resolver that may answer nothing, which does not.
        var domain = Domain("Tenants")(Project(Described("TenantsTenancy.SeatOverview", "Shop.Domain")))
            .Run([.. GeneratorTestHost.CoreGenerators(), new DescribesGenerator()]);

        domain.ShouldHaveGenerated("TenantsTenancy.TemplateFacade");
        domain.ShouldContain("SeatOverviewType.Describes", "// unresolved");
        domain.ShouldNotContain("SeatOverviewType.Describes", "TenancyUseCases", "the generator could not tell what the name stands for");
    }

    [Fact]
    public void A_module_of_one_project_keeps_an_alias_of_that_name_and_every_generator_there_reads_it()
    {
        // The way out for a module of one project with GraphQL types over the records: one alias, of exactly the
        // name, which the generator stands back for. Every generator of the project reads an alias the code declares.
        var domain = Domain("Tenants")(Project(Described("TenantsTenancy.SeatOverview", "Shop.Domain")))
            .WithSource("global using TenantsTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
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
        var result = Project(Handler("TenantsTenancy", "Shop.Projects"), module: "Projects")
            .WithReferencedProject("Shop.Tenants.Domain", Domain("Tenants"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
    }

    [Fact]
    public void A_project_that_sees_only_the_ids_gets_nothing()
    {
        var result = Project("namespace Shop.Projects; public sealed class Nothing;", module: "Projects")
            .WithReferencedProject("Shop.Contracts", project => project.WithSource(Ids, "Ids.cs"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
        result.OutputCompilation.GetTypeByMetadataName("ProjectsTenancy").Should().BeNull("which of its ids are the organization's cannot be told");
    }

    [Fact]
    public void A_host_that_sees_the_classes_of_two_modules_names_each_by_its_module()
    {
        static Func<GeneratorTestHost, GeneratorTestHost> Module(string module)
            => project => project
                .WithSource(ModuleAttribute(module), "Module.cs")
                .WithSource(Ids.Replace("Shop.Contracts", module + ".Contracts"), "Ids.cs")
                .WithSource(Classes.Replace("Shop.Contracts", module + ".Contracts").Replace("Shop.Domain", module + ".Domain"), "Classes.cs");

        var host = Project(Handler("CustomersTenancy", "Host.Customers").Replace("using Shop.", "using Customers."))
            .WithSource(Handler("PartnersTenancy", "Host.Partners").Replace("using Shop.", "using Partners."), "Partners.cs")
            .WithReferencedProject("Customers.Domain", Module("Customers"))
            .WithReferencedProject("Partners.Domain", Module("Partners"))
            .RunCore();

        host.ShouldCompile();
        host.ReportedDiagnostics.Should().BeEmpty("neither module is in the way of the other");
        TypeOf(host, "Host.Customers.SeatsOfMine", "Seats").ContainingType!.TypeArguments[0].ToDisplayString().Should().Be("Customers.Domain.Tenant");
        TypeOf(host, "Host.Partners.SeatsOfMine", "Seats").ContainingType!.TypeArguments[0].ToDisplayString().Should().Be("Partners.Domain.Tenant");
    }

    [Fact]
    public void Classes_split_over_two_projects_of_a_module_get_the_class_where_they_are_complete()
    {
        const string First =
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
        const string Second =
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

        var first = Project(First, module: "Shop").WithSource(Ids, "Ids.cs").WithAssemblyName("Shop.Organization").RunCore();
        first.ShouldCompile();
        first.HintNames.Should().NotContain("TemplateFacade", "without a seat and a role there is nothing to close over yet");
        first.ReportedDiagnostics.Should().ContainSingle()
            .Which.Severity.Should().Be(DiagnosticSeverity.Info, "the first project of a split module is told, and nothing in it is wrong");

        var second = Project(Second, module: "Shop")
            .WithReferencedProject("Shop.Organization", project => project.WithSource(Ids, "Ids.cs").WithSource(First, "First.cs").WithSource(ModuleAttribute("Shop"), "Module.cs"))
            .WithSource(Handler("ShopTenancy"), "Handler.cs")
            .RunCore();
        second.ShouldCompile();
        second.ShouldContain("ShopTenancy.TemplateFacade", "public abstract class ShopTenancy : " + ClosedOverShop);
        second.ReportedDiagnostics.Should().BeEmpty();
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
            "'ShopTenancy' is not written, so no project can name the types nested in TenancyUseCases through it: no class is declared with "
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
        var source = result.GeneratedSources.Single(generated => generated.HintName.EndsWith("Tenancy.TemplateFacade.g.cs", StringComparison.Ordinal)).SourceText.ToString();
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
            .WithSource(Handler("ShopTenancy").Replace("public sealed class SeatsOfMine", "internal sealed class SeatsOfMine"), "Handler.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("ShopTenancy.TemplateFacade", "internal abstract class ShopTenancy : " + ClosedOverShop);
    }

    [Fact]
    public void An_alias_the_project_writes_itself_under_the_same_name_stays_its_own()
    {
        // The form every project wrote before: the generator stands back, and the compiler is not handed two.
        var result = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource("global using ShopTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
    }

    [Fact]
    public void An_alias_of_the_same_name_in_a_project_above_is_the_compilers_error()
    {
        // The one thing a project that wrote the alias before has to do: take it out. The compiler names it.
        var result = Project(Handler("ShopTenancy"), module: "Shop")
            .WithReferencedProject("Shop.Domain", Domain("Shop"))
            .WithSource("global using ShopTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();

        result.CompilationErrors.Select(error => error.Id).Should().Contain("CS0576", "an alias and a class of one name in the global namespace are two things the compiler cannot tell apart");
    }

    [Fact]
    public void An_alias_under_another_name_stands_beside_the_class()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource(Handler("SampleTenancy", "Shop.Old"), "Old.cs")
            .WithSource("global using SampleTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("ShopTenancy.TemplateFacade");
        SymbolEqualityComparer.Default.Equals(TypeOf(result, "Shop.Application.SeatsOfMine", "Seats"), TypeOf(result, "Shop.Old.SeatsOfMine", "Seats"))
            .Should().BeTrue("both names stand for the one type");
    }

    [Fact]
    public void A_type_of_that_name_in_the_global_namespace_keeps_the_name()
    {
        var result = Domain("Shop")(Project("public static class ShopTenancy { public const int Mine = 1; }"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade", "a second type of the name would be the compiler's error");
    }

    [Fact]
    public void A_namespace_of_that_name_closer_to_the_code_hides_the_class_there_only()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource(
                """
                namespace Shop.Reports.ShopTenancy
                {
                    public sealed class SeatCommands;
                }

                namespace Shop.Reports
                {
                    public sealed class Report(ShopTenancy.SeatCommands mine)
                    {
                        public ShopTenancy.SeatCommands Mine => mine;
                    }
                }
                """,
                "Reports.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("ShopTenancy.TemplateFacade");
        TypeOf(result, "Shop.Reports.Report", "Mine").ContainingAssembly.Name.Should().Be(GeneratorTestHost.DefaultAssemblyName, "C# finds the nearer name first");
    }

    [Fact]
    public void A_type_of_that_name_the_project_declares_in_a_namespace_keeps_the_class_out_and_is_told_where()
    {
        // C# looks in the global namespace before it looks in what a file imports, so a class written there would be
        // what ShopTenancy means in every file that imports Shop.Settings: code that compiled would stop compiling.
        var result = Domain("Shop")(Project(
                """
                using Shop.Settings;

                namespace Shop.Application;

                public static class Uses
                {
                    public const string Slug = ShopTenancy.DefaultSlug;
                }
                """))
            .WithSource("namespace Shop.Settings; public static class ShopTenancy { public const string DefaultSlug = \"shop\"; }", "Settings.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("TemplateFacade");
        var told = result.ShouldHaveDiagnostic("DDD00065", at: "ShopTenancy");
        told.Location.GetLineSpan().Path.Should().Be("Settings.cs", "it is said on the type the class would hide");
        told.GetMessage().Should().EndWith(
            "this project declares 'Shop.Settings.ShopTenancy', which a class of that name in the global namespace would hide in every file that imports "
            + "'Shop.Settings' with a using; name the class otherwise, with [assembly: TemplateFacade(typeof(TenancyUseCases<,,,,,,,,>), \"...\")] in "
            + "this project, or rename 'ShopTenancy'");
    }

    [Fact]
    public void A_type_of_that_name_nested_in_another_is_no_matter()
    {
        var result = Domain("Shop")(Project(Handler("ShopTenancy")))
            .WithSource("namespace Shop.Settings; public static class Defaults { public static class ShopTenancy { public const string Slug = \"shop\"; } }", "Settings.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("ShopTenancy.TemplateFacade");
        result.ReportedDiagnostics.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ a name of the application's choosing

    [Fact]
    public void The_project_that_declares_the_classes_names_the_class_itself_in_one_line()
    {
        // A module called Tenancy would get TenancyTenancy. One attribute beside [assembly: Module] names it, and every
        // project above names the use cases by that name, writing nothing.
        var application = Project(Handler("ShopTenancy"), module: "Tenancy")
            .WithReferencedProject("Shop.Tenancy.Domain", project => Domain("Tenancy")(project).WithSource(NamedItself("ShopTenancy"), "Named.cs"))
            .RunCore();

        application.ShouldCompile();
        application.HintNames.Should().NotContain("TemplateFacade", "the domain project wrote it");
        application.OutputCompilation.GetTypeByMetadataName("ShopTenancy")!.ContainingAssembly.Name.Should().Be("Shop.Tenancy.Domain");
        application.OutputCompilation.GetTypeByMetadataName("TenancyTenancy").Should().BeNull("the application's name stands instead of the package's");
    }

    [Fact]
    public void A_name_the_application_gives_may_hold_the_module_as_the_packages_does()
    {
        var result = Domain("Shop")(Project(Handler("ShopUseCases"))).WithSource(NamedItself("{Module}UseCases"), "Named.cs").RunCore();

        result.ShouldCompile();
        result.ShouldContain("ShopUseCases.TemplateFacade", "public abstract class ShopUseCases : " + ClosedOverShop);
        result.HintNames.Should().NotContain("ShopTenancy");
    }

    [Fact]
    public void A_name_the_application_gives_stands_back_as_the_packages_does()
    {
        // The project's own alias of the name, and a type of it in the global namespace, are the application's.
        var aliased = Domain("Tenancy")(Project(Handler("ShopTenancy")))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .WithSource("global using ShopTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();
        aliased.ShouldCompile();
        aliased.HintNames.Should().NotContain("TemplateFacade");

        var typed = Domain("Tenancy")(Project("public static class ShopTenancy { public const int Mine = 1; }"))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .RunCore();
        typed.ShouldCompile();
        typed.HintNames.Should().NotContain("TemplateFacade");

        var hidden = Domain("Tenancy")(Project("namespace Shop.Settings; public static class ShopTenancy { public const int Mine = 1; }"))
            .WithSource(NamedItself("ShopTenancy"), "Named.cs")
            .RunCore();
        hidden.ShouldCompile();
        hidden.HintNames.Should().NotContain("TemplateFacade");
        hidden.ShouldHaveDiagnostic("DDD00065", at: "ShopTenancy");

        var above = Project(Handler("ShopTenancy"), module: "Tenancy")
            .WithReferencedProject("Shop.Tenancy.Domain", project => Domain("Tenancy")(project).WithSource(NamedItself("ShopTenancy"), "Named.cs"))
            .WithSource("global using ShopTenancy = " + ClosedOverShop + ";", "GlobalUsings.cs")
            .RunCore();
        above.CompilationErrors.Select(error => error.Id).Should().Contain("CS0576", "an alias above of the name the application gave is in the class's way as one of the package's name is");
    }

    [Fact]
    public void A_name_one_project_gives_asks_no_project_above_it_for_a_class()
    {
        // A host that declares a second module's classes and sees the first module's domain project, where the
        // application named that module's class: the package asks for its own types, and the host gets its own class
        // under the package's name, and no second one under the name the other project chose.
        static Func<GeneratorTestHost, GeneratorTestHost> Module(string module)
            => project => project
                .WithSource(ModuleAttribute(module), "Module.cs")
                .WithSource(Ids.Replace("Shop.Contracts", module + ".Contracts"), "Ids.cs")
                .WithSource(Classes.Replace("Shop.Contracts", module + ".Contracts").Replace("Shop.Domain", module + ".Domain"), "Classes.cs");

        var host = Project(Handler("CustomersUseCases", "Host.Customers").Replace("using Shop.", "using Customers."))
            .WithReferencedProject("Customers.Domain", project => Module("Customers")(project).WithSource(NamedItself("CustomersUseCases"), "Named.cs"))
            .WithSource(Ids.Replace("Shop.Contracts", "Partners.Contracts"), "Ids.cs")
            .WithSource(Classes.Replace("Shop.Contracts", "Partners.Contracts").Replace("Shop.Domain", "Partners.Domain"), "Classes.cs")
            .WithSource(ModuleAttribute("Partners"), "Module.cs")
            .RunCore();

        host.ShouldCompile();
        host.ReportedDiagnostics.Should().BeEmpty();
        host.ShouldHaveGenerated("PartnersTenancy.TemplateFacade");
        host.GeneratedSources.Where(source => source.HintName.EndsWith(".TemplateFacade.g.cs", StringComparison.Ordinal)).Should().ContainSingle();
    }

    // ------------------------------------------------------------------ what a package can ask

    /// <summary>A made-up package with several classes it asks for: one every application can have, and ones it cannot.</summary>
    private const string Package =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateFacade(typeof(Acme.Billing.BillingUseCases<,>), "{Module}Billing")]
        [assembly: TemplateFacade(typeof(Acme.Billing.Ledger<,>), "{Module}Ledger")]
        [assembly: TemplateFacade(typeof(Acme.Billing.Open<,>), "{Module}Open")]
        [assembly: TemplateFacade(typeof(Acme.Billing.BillingUseCases<,>), "{Customer}Billing")]
        [assembly: TemplateFacade(typeof(Acme.Billing.Statics<,>), "{Module}Statics")]
        [assembly: TemplateFacade(typeof(Acme.Billing.Closed<,>), "{Module}Closed")]

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
            public static ShopBilling.Statement Statement(AccountId id) => new(id, 0m);
        }
        """;

    [Fact]
    public void A_package_of_its_own_gets_its_class_and_one_its_classes_do_not_fit_is_told_and_its_own_mistakes_pass_without_a_word()
    {
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Accounts, "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("ShopBilling.TemplateFacade", "public abstract class ShopBilling : global::Acme.Billing.BillingUseCases<global::Shop.Billing.Account, global::Shop.Billing.AccountId>");
        result.HintNames.Should().NotContain("ShopLedger", "the account does not meet what the ledger asks of it, and a compile error in generated code would fix nothing");
        result.ShouldHaveDiagnostic("DDD00065", at: "Account").GetMessage().Should().Be(
            "'ShopLedger' is not written, so no project can name the types nested in Ledger through it: it takes 'Account' as 'TAccount', which requires 'IAudited'; 'Account' does not meet it");
        result.ReportedDiagnostics.Should().ContainSingle("what is the package's own mistake is the package's tests' to show");
        result.HintNames.Should().NotContain("ShopOpen", "the class leaves no type parameter open");
        result.HintNames.Should().NotContain("{Customer}").And.NotContain("CustomerBilling", "a name with braces around anything but the module is no name");
        result.HintNames.Should().NotContain("ShopStatics").And.NotContain("ShopClosed", "only a class that can be derived from names its nested types through another");
    }

    [Fact]
    public void Two_classes_that_come_to_one_name_are_both_left_out()
    {
        var result = GeneratorTestHost.Create(Package.Replace("\"{Module}Ledger\"", "\"{Module}Billing\"").Replace(", IAudited", string.Empty), "Package.cs")
            .WithSource(Accounts.Replace("ShopBilling.Statement Statement(AccountId id) => new(id, 0m);", "int Nothing => 0;"), "Accounts.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("ShopBilling", "neither could be told from the other");
    }

    // ------------------------------------------------------------------ incremental

    [Fact]
    public void An_edit_that_changes_no_class_writes_nothing_anew()
    {
        var first = Domain("Shop")(Project(Handler("ShopTenancy"))).RunCore();
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
    public void A_module_that_is_renamed_gets_the_class_anew_under_its_new_name()
    {
        // The control for the test above: a change that matters shows.
        var first = Domain("Shop")(Project(Handler("ShopTenancy"))).RunCore();
        first.ShouldCompile();

        var second = first.RunAgain(static (compilation, parseOptions) =>
        {
            var module = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Module.cs");
            var renamed = compilation.ReplaceSyntaxTree(module, CSharpSyntaxTree.ParseText(SourceText.From(ModuleAttribute("Store"), Encoding.UTF8), parseOptions, module.FilePath));
            var handler = renamed.SyntaxTrees.Single(tree => tree.FilePath == "Source.cs");
            return renamed.ReplaceSyntaxTree(handler, CSharpSyntaxTree.ParseText(SourceText.From(Handler("StoreTenancy"), Encoding.UTF8), parseOptions, handler.FilePath));
        });

        second.ShouldCompile();
        second.OutputStepReasons().Should().Contain(step => step.Reason == IncrementalStepRunReason.New || step.Reason == IncrementalStepRunReason.Modified);
        second.ShouldHaveGenerated("StoreTenancy.TemplateFacade");
        second.HintNames.Should().NotContain("ShopTenancy");
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
