using System.Collections;
using System.Reflection;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A package ships a registration generic over the application's classes, and the application calls it
/// without type arguments: <c>modelBuilder.AddTenancy()</c>. The package marks the method
/// <c>[TemplateRegistration]</c> and its type parameters <c>[TemplateType]</c>, names the declaring type in
/// <c>[assembly: TemplateRegistrations]</c>, and the generator writes a wrapper into the application closed over
/// the classes it declared with the package's templates.
/// <para>
/// Every positive test calls the wrapper from the application and runs it, so a wrapper that compiles but
/// forwards to the wrong types, or loses a default value on the way, fails too.
/// </para>
/// </summary>
public class TemplateRegistrationTests
{
    /// <summary>
    /// The registrations of <see cref="TemplateEntityTests.Package"/>: each writes down what it was closed over
    /// and called with, so a test reads back what the wrapper forwarded.
    /// </summary>
    internal const string Registrations =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateRegistrations(typeof(Sample.Tenancy.TenancyRegistrations))]

        namespace Sample.Tenancy;

        public sealed class Registry
        {
            public List<string> Entries { get; } = new();
        }

        public enum Shape { Flat, Hierarchical }

        public interface ISchemaOptions<TTenantId>;

        public static class TenancyRegistrations
        {
            [TemplateRegistration]
            public static Registry AddTenancy<
                [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
                [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
                [TemplateType(typeof(OrganizationAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
                [TemplateType(typeof(OrganizationAttribute<>))] TOrganizationId,
                [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
                [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId>(this Registry registry)
                where TTenant : TenantAggregate<TTenantId>
                where TTenantId : IEntityId, IEquatable<TTenantId>
                where TOrganization : OrganizationAggregate<TOrganizationId, TTenantId, TUnit, TUnitId>
                where TOrganizationId : IEntityId, IEquatable<TOrganizationId>
                where TUnit : OrganizationUnitEntity<TUnitId>, IOrganizationUnitFactory<TUnit, TUnitId>
                where TUnitId : IEntityId, IEquatable<TUnitId>
            {
                registry.Entries.AddRange(new[] { typeof(TTenant).Name, typeof(TTenantId).Name, typeof(TOrganization).Name, typeof(TOrganizationId).Name, typeof(TUnit).Name, typeof(TUnitId).Name });
                return registry;
            }

            [TemplateRegistration]
            public static Registry AddTenancyWith<
                [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
                TContext,
                [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
                TOptions>(this Registry registry, TOptions options)
                where TTenant : TenantAggregate<TTenantId>
                where TContext : class, IDisposable, new()
                where TTenantId : IEntityId, IEquatable<TTenantId>
                where TOptions : ISchemaOptions<TTenantId>
            {
                registry.Entries.AddRange(new[] { typeof(TTenant).Name, typeof(TContext).Name, typeof(TTenantId).Name, options.GetType().Name });
                return registry;
            }

            [TemplateRegistration]
            public static Registry AddTenancyDefaults<[TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant>(
                this Registry registry,
                string schema = "tenancy \"main\"",
                int depth = 32,
                long limit = 5_000_000_000,
                double ratio = 0.25,
                float weight = 1.5f,
                decimal price = 9.95m,
                char separator = '/',
                bool strict = true,
                Shape shape = Shape.Hierarchical,
                Shape? optional = null,
                string? note = null,
                CancellationToken cancellationToken = default,
                int? maximum = 7)
                where TTenant : class
            {
                registry.Entries.Add(FormattableString.Invariant(
                    $"{schema}|{depth}|{limit}|{ratio}|{weight}|{price}|{separator}|{strict}|{shape}|{optional}|{note ?? "null"}|{cancellationToken.CanBeCanceled}|{maximum}"));
                return registry;
            }
        }
        """;

    /// <summary>What the application calls: every registration without the classes it declared.</summary>
    internal const string Startup =
        """
        using System;
        using System.Collections.Generic;
        using Sample.Tenancy;

        namespace Sample;

        public sealed class SchemaContext : IDisposable
        {
            public void Dispose() { }
        }

        public sealed class SchemaOptions : ISchemaOptions<TenantId>;

        public static class Startup
        {
            public static List<string> Register() => new Registry().AddTenancy().Entries;

            public static List<string> RegisterWith() => new Registry().AddTenancyWith<SchemaContext, SchemaOptions>(new SchemaOptions()).Entries;

            public static List<string> RegisterDefaults() => new Registry().AddTenancyDefaults().Entries;

            public static List<string> RegisterDefaultsByHand() => TenancyRegistrations.AddTenancyDefaults<ShopTenant>(new Registry()).Entries;
        }
        """;

    private const string Usings =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using Sample.Tenancy;

        namespace Sample;


        """;

    private static GeneratorTestHost Package()
        => GeneratorTestHost.Create(TemplateEntityTests.Package, "Package.cs")
            .WithSource(Registrations, "Registrations.cs")
            .WithSource(TemplateEntityTests.Ids, "Ids.cs");

    private static GeneratorTestHost Together()
        => Package()
            .WithSource(TemplateEntityTests.Application, "Application.cs")
            .WithSource(Startup, "Startup.cs");

    private static List<string> Call(EmittedAssembly emitted, string method)
        => ((IEnumerable)emitted.CallStatic("Sample.Startup", method)!).Cast<string>().ToList();

    // ------------------------------------------------------------------ what is generated

    [Fact]
    public void A_registration_is_closed_over_the_applications_template_classes_and_ids()
    {
        var result = Together().RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "TenancyRegistrations.AddTenancy.Registration.",
            "public static global::Sample.Tenancy.Registry AddTenancy(this global::Sample.Tenancy.Registry registry)");
        result.ShouldContain(
            "TenancyRegistrations.AddTenancy.Registration.",
            "=> global::Sample.Tenancy.TenancyRegistrations.AddTenancy<global::Sample.ShopTenant, global::Sample.TenantId, global::Sample.ShopOrganization, global::Sample.OrganizationId, global::Sample.ShopUnit, global::Sample.UnitId>(registry);");

        Call(result.Emit(), "Register").Should().Equal("ShopTenant", "TenantId", "ShopOrganization", "OrganizationId", "ShopUnit", "UnitId");
    }

    [Fact]
    public void Parameters_without_a_template_stay_open_with_their_constraints()
    {
        var result = Together().RunCore();

        result.ShouldCompile();
        var source = result.Source("TenancyRegistrations.AddTenancyWith.Registration.");
        source.Should().Contain("AddTenancyWith<TContext, TOptions>(this global::Sample.Tenancy.Registry registry, TOptions options)");
        source.Should().Contain("where TContext : class, global::System.IDisposable, new()");
        source.Should().Contain(
            "where TOptions : global::Sample.Tenancy.ISchemaOptions<global::Sample.TenantId>",
            "a constraint that names a type parameter the project fills names the type that fills it");
        source.Should().Contain("AddTenancyWith<global::Sample.ShopTenant, TContext, global::Sample.TenantId, TOptions>(registry, options)");

        Call(result.Emit(), "RegisterWith").Should().Equal("ShopTenant", "SchemaContext", "TenantId", "SchemaOptions");
    }

    [Fact]
    public void Default_parameter_values_are_copied()
    {
        var result = Together().RunCore();

        result.ShouldCompile();
        var source = result.Source("TenancyRegistrations.AddTenancyDefaults.Registration.");
        source.Should().Contain("string schema = \"tenancy \\\"main\\\"\"");
        source.Should().Contain("global::Sample.Tenancy.Shape shape = global::Sample.Tenancy.Shape.Hierarchical");
        source.Should().Contain("global::System.Threading.CancellationToken cancellationToken = default");
        source.Should().Contain("string? note = null");

        var emitted = result.Emit();
        var byWrapper = Call(emitted, "RegisterDefaults");
        byWrapper.Should().Equal(Call(emitted, "RegisterDefaultsByHand"), "leaving a parameter out means on the wrapper what it means on the package's method");
        byWrapper.Should().Equal("tenancy \"main\"|32|5000000000|0.25|1.5|9.95|/|True|Hierarchical||null|False|7");
    }

    [Fact]
    public void The_wrapper_is_internal_and_lives_in_the_methods_namespace()
    {
        var result = Together().RunCore();

        result.ShouldCompile();
        var source = result.Source("TenancyRegistrations.AddTenancy.Registration.");
        source.Should().Contain("namespace Sample.Tenancy;", "the application's using for the package is all a call needs");
        source.Should().Contain("internal static partial class GeneratedTenancyRegistrations");

        var wrapper = result.Emit().Type("Sample.Tenancy.GeneratedTenancyRegistrations");
        wrapper.IsPublic.Should().BeFalse("two projects that both declare the classes must not clash where one references the other");
        wrapper.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name).Distinct()
            .Should().BeEquivalentTo(["AddTenancy", "AddTenancyWith", "AddTenancyDefaults"], "each method name gets its own file, of one partial class");
    }

    [Fact]
    public void Nothing_is_emitted_in_a_project_that_declares_none_of_the_templates()
    {
        // The package itself, and a module of the application that only references the package.
        var package = Package().RunCore();
        package.ShouldCompile();
        package.HintNames.Should().NotContain("Registration");

        var module = GeneratorTestHost.Create(
                """
                using Sample.Tenancy;

                namespace Sample.Projects;

                public static class Nothing
                {
                    public static Registry Registry() => new();
                }
                """,
                "Module.cs")
            .WithReferencedAssembly(TemplateEntityTests.Package, "Sample.Tenancy")
            .WithReferencedAssembly(Registrations, "Sample.Tenancy.Registrations")
            .RunCore();
        module.ShouldCompile();
        module.HintNames.Should().NotContain("Registration");
        module.ReportedDiagnostics.Should().BeEmpty("a project that declares none of the templates is not meant to get the registration");
    }

    [Fact]
    public void The_package_can_be_a_referenced_assembly()
    {
        var result = GeneratorTestHost.Create(TemplateEntityTests.Ids, "Ids.cs")
            .WithSource(TemplateEntityTests.Application, "Application.cs")
            .WithSource(Startup, "Startup.cs")
            .WithReferencedAssembly(TemplateEntityTests.Package, "Sample.Tenancy")
            .WithReferencedAssembly(Registrations, "Sample.Tenancy.Registrations")
            .RunCore();

        // Startup's calls without type arguments compile only against the wrapper. The package is an in-memory
        // assembly the test process cannot load, so this reads what was written rather than running it.
        result.ShouldCompile();
        result.ShouldContain(
            "TenancyRegistrations.AddTenancy.Registration.",
            "=> global::Sample.Tenancy.TenancyRegistrations.AddTenancy<global::Sample.ShopTenant, global::Sample.TenantId, global::Sample.ShopOrganization, global::Sample.OrganizationId, global::Sample.ShopUnit, global::Sample.UnitId>(registry);");
        result.OutputCompilation.Assembly.GetTypeByMetadataName("Sample.Tenancy.GeneratedTenancyRegistrations")
            .Should().NotBeNull("the wrapper is written into the application, not the package");
    }

    [Fact]
    public void A_class_a_referenced_project_declares_is_taken_when_this_one_declares_none()
    {
        // The tenant lives in a module this one references; this project declares the organization and its
        // units, and so gets the registration, with the tenant taken from the reference. The tenant's module
        // does not reference the registrations, as a domain module does not reference the package that stores
        // it, so it is not meant to get them and is not told it lacks the organization.
        var result = GeneratorTestHost.Create(
                Usings +
                """
                [OrganizationUnit<UnitId>]
                public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
                {
                    private ShopUnit(UnitId id, string name) : base(id, name) { }

                    public static ShopUnit Create(UnitId id, string name) => new(id, name);
                }

                [Organization<OrganizationId>]
                public sealed partial class ShopOrganization
                {
                    public ShopOrganization(OrganizationId id, TenantId tenantId) : base(id, tenantId) { }
                }

                public static class Startup
                {
                    public static System.Collections.Generic.List<string> Register() => new Registry().AddTenancy().Entries;
                }
                """,
                "Organizations.cs")
            .WithReferencedAssembly(TemplateEntityTests.Package, "Sample.Tenancy")
            .WithReferencedAssembly(
                TemplateEntityTests.Ids +
                """


                [Sample.Tenancy.TenantAggregate<TenantId>]
                public sealed partial class ShopTenant
                {
                    public ShopTenant(TenantId id, string name) : base(id, name) { }
                }
                """,
                "Shop.Tenancy")
            .WithReferencedAssembly(Registrations, "Sample.Tenancy.Registrations")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "TenancyRegistrations.AddTenancy.Registration.",
            "AddTenancy<global::Sample.ShopTenant, global::Sample.TenantId, global::Sample.ShopOrganization, global::Sample.OrganizationId, global::Sample.ShopUnit, global::Sample.UnitId>(registry)");
    }

    // ------------------------------------------------------------------ a module in layers

    /// <summary>What every project of a module declares, in a file of its own.</summary>
    private static string ModuleAttribute(string module) => "[assembly: DDDToolkit.Abstractions.Attributes.Module(\"" + module + "\")]\n";

    /// <summary>
    /// A module's infrastructure project: it declares none of the classes, only its module and a call to the
    /// registration without type arguments, which compiles only against the wrapper.
    /// </summary>
    private const string Infrastructure =
        """
        using System.Collections.Generic;
        using Sample.Tenancy;

        namespace Sample.Infrastructure;

        public static class Startup
        {
            public static List<string> Register() => new Registry().AddTenancy().Entries;
        }
        """;

    private const string NothingOfItsOwn =
        """
        namespace Sample.Elsewhere;

        public static class Nothing;
        """;

    /// <summary>
    /// A project of <paramref name="module"/> that declares no class and references the package, <paramref name="domains"/>
    /// and the registrations. Each domain project references the package and the domains before it, never the
    /// registrations: a domain project has no Entity Framework, so it is not meant to get them.
    /// </summary>
    private static GeneratorTestHost ProjectOf(string? module, string source, params (string Name, string Module, string Source)[] domains)
    {
        var host = GeneratorTestHost.Create(source, "Startup.cs")
            .WithReferencedAssembly(TemplateEntityTests.Package, "Sample.Tenancy");
        if (module is not null)
        {
            host = host.WithSource(ModuleAttribute(module), "Module.cs");
        }

        foreach (var (name, domainModule, domainSource) in domains)
        {
            host = host.WithReferencedProject(name, project => project
                .WithSource(ModuleAttribute(domainModule), "Module.cs")
                .WithSource(TemplateEntityTests.Ids.Replace("namespace Sample;", "namespace " + name + ";", StringComparison.Ordinal), "Ids.cs")
                .WithSource(domainSource.Replace("namespace Sample;", "namespace " + name + ";", StringComparison.Ordinal), "Classes.cs"));
        }

        return host.WithReferencedAssembly(Registrations, "Sample.Tenancy.Registrations");
    }

    /// <summary>The module's domain project: the three classes, and no registrations.</summary>
    private static (string, string, string) ShopDomain() => ("Shop.Domain", "Shop", TemplateEntityTests.Application);

    /// <summary>A domain project that declares a tenant and nothing else.</summary>
    private static (string, string, string) TenantOnly(string name, string module)
        => (name, module,
            """
            namespace Sample;

            [Sample.Tenancy.TenantAggregate<TenantId>]
            public sealed partial class OnlyTenant
            {
                public OnlyTenant(TenantId id, string name) : base(id, name) { }
            }
            """);

    [Fact]
    public void A_project_of_a_module_that_declares_none_is_closed_over_its_modules_classes()
    {
        var result = ProjectOf("Shop", Infrastructure, ShopDomain()).RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "TenancyRegistrations.AddTenancy.Registration.",
            "=> global::Sample.Tenancy.TenancyRegistrations.AddTenancy<global::Shop.Domain.ShopTenant, global::Shop.Domain.TenantId, global::Shop.Domain.ShopOrganization, global::Shop.Domain.OrganizationId, global::Shop.Domain.ShopUnit, global::Shop.Domain.UnitId>(registry);");
        result.ShouldContain(
            "TenancyRegistrations.AddTenancyDefaults.Registration.",
            "AddTenancyDefaults<global::Shop.Domain.ShopTenant>(registry",
            "every registration that takes one of the module's classes is written");
        result.OutputCompilation.Assembly.GetTypeByMetadataName("Sample.Tenancy.GeneratedTenancyRegistrations")
            .Should().NotBeNull("the wrapper is written into the project that calls it, not the one that declares the classes");
    }

    [Fact]
    public void A_project_of_another_module_gets_nothing_and_hears_nothing()
    {
        // Billing references the shop's domain and the registrations, and is told nothing: the classes are the shop's.
        var result = ProjectOf("Billing", NothingOfItsOwn, ShopDomain()).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("Registration");
        result.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_project_without_a_module_gets_nothing_from_its_references()
    {
        var result = ProjectOf(module: null, NothingOfItsOwn, ShopDomain()).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("Registration");
        result.ReportedDiagnostics.Should().BeEmpty("only a project of the module is meant to get the module's registrations");
    }

    [Fact]
    public void A_template_the_module_declares_nowhere_is_DDD00049_on_the_module_attribute()
    {
        // The module's domain declares its tenant only. AddTenancy needs the organization and its units too; the
        // registrations that take the tenant alone are still written.
        var result = ProjectOf("Shop", NothingOfItsOwn, TenantOnly("Shop.Domain", "Shop")).RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00049", at: "DDDToolkit.Abstractions.Attributes.Module(\"Shop\")");
        diagnostic.GetMessage().Should().Contain("A class declared with [Organization] is needed by 'TenancyRegistrations.AddTenancy'");
        diagnostic.Properties.Should().BeEmpty("the class belongs in the project that declares the others, so no fix declares it here");
        result.Count("DDD00049").Should().Be(2, "the organization and the unit are both missing");
        result.HintNames.Should().NotContain("TenancyRegistrations.AddTenancy.Registration");
        result.ShouldHaveGenerated("TenancyRegistrations.AddTenancyDefaults.Registration.");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_template_class_in_an_assembly_of_another_module_is_not_taken()
    {
        // Billing declares a tenant of its own. Looked for in every reference, the shop's infrastructure would find
        // two tenants and be told so; only the shop's own projects are looked in.
        var result = ProjectOf("Shop", Infrastructure, ShopDomain(), TenantOnly("Billing.Domain", "Billing")).RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00045");
        result.ShouldContain("TenancyRegistrations.AddTenancy.Registration.", "AddTenancy<global::Shop.Domain.ShopTenant, global::Shop.Domain.TenantId,");
        result.ShouldNotContain("TenancyRegistrations.AddTenancyDefaults.Registration.", "OnlyTenant");
    }

    [Fact]
    public void Two_projects_of_the_module_that_declare_one_template_are_DDD00045_on_the_module_attribute()
    {
        var result = ProjectOf("Shop", NothingOfItsOwn, ShopDomain(), TenantOnly("Shop.Legacy", "Shop")).RunCore();

        result.ShouldHaveDiagnostic("DDD00045", at: "DDDToolkit.Abstractions.Attributes.Module(\"Shop\")").GetMessage()
            .Should().Contain("'ShopTenant' and 'OnlyTenant'");
        result.HintNames.Should().NotContain("TenancyRegistrations.AddTenancy.Registration");
        result.CompilationErrors.Should().BeEmpty();
    }

    /// <summary>
    /// A module's API project, which composes the module: it declares no class, and calls the infrastructure
    /// project's own public registration, never the wrapper, which is internal to the project that holds it.
    /// </summary>
    private const string Composition =
        """
        using System.Collections.Generic;

        namespace Sample.Api;

        public static class Entry
        {
            public static List<string> Register() => Sample.Infrastructure.Startup.Register();
        }
        """;

    /// <summary>The shop's domain project, by its own name and module, as <see cref="ProjectOf"/> writes one.</summary>
    private static GeneratorTestHost ShopDomainProject(GeneratorTestHost project)
        => project
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .WithSource(TemplateEntityTests.Ids.Replace("namespace Sample;", "namespace Shop.Domain;", StringComparison.Ordinal), "Ids.cs")
            .WithSource(TemplateEntityTests.Application.Replace("namespace Sample;", "namespace Shop.Domain;", StringComparison.Ordinal), "Classes.cs");

    [Fact]
    public void A_project_above_the_one_that_holds_the_registrations_gets_none()
    {
        // The API project references the infrastructure project to compose the module, and through it sees the
        // domain project and the registrations: everything the infrastructure project was given its wrappers
        // for. A second copy here would make this project name the registrations' package in code nobody wrote.
        var result = GeneratorTestHost.Create(Composition, "Entry.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .WithReferencedAssembly(TemplateEntityTests.Package, "Sample.Tenancy")
            .WithReferencedProject("Shop.Domain", ShopDomainProject)
            .WithReferencedAssembly(Registrations, "Sample.Tenancy.Registrations")
            .WithReferencedProject("Shop.Infrastructure", project => project
                .WithSource(ModuleAttribute("Shop"), "Module.cs")
                .WithSource(Infrastructure, "Startup.cs"))
            .RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("Registration", "the wrappers are in the infrastructure project, which this one calls");
        result.ReportedDiagnostics.Should().BeEmpty();
        result.OutputCompilation.Assembly.GetTypeByMetadataName("Sample.Tenancy.GeneratedTenancyRegistrations").Should().BeNull();
    }

    [Fact]
    public void A_context_project_above_classes_that_see_the_registrations_keeps_its_own()
    {
        // Here the domain project itself references the registrations, and gets wrappers for the classes it
        // declares. They are internal to it, so the project that holds the context still needs its own: only a
        // project that declares no class stands in for the projects above it.
        var result = GeneratorTestHost.Create(Infrastructure, "Startup.cs")
            .WithSource(ModuleAttribute("Shop"), "Module.cs")
            .WithReferencedAssembly(TemplateEntityTests.Package, "Sample.Tenancy")
            .WithReferencedAssembly(Registrations, "Sample.Tenancy.Registrations")
            .WithReferencedProject("Shop.Domain", ShopDomainProject)
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "TenancyRegistrations.AddTenancy.Registration.",
            "=> global::Sample.Tenancy.TenancyRegistrations.AddTenancy<global::Shop.Domain.ShopTenant, global::Shop.Domain.TenantId, global::Shop.Domain.ShopOrganization, global::Shop.Domain.OrganizationId, global::Shop.Domain.ShopUnit, global::Shop.Domain.UnitId>(registry);");
    }

    // ------------------------------------------------------------------ what is reported

    [Fact]
    public void A_missing_template_class_is_DDD00049_on_a_declared_class()
    {
        // Nothing the project declares takes the organization for its parent, so the registration is what needs it.
        var result = Package()
            .WithSource(
                Usings +
                """
                [OrganizationUnit<UnitId>]
                public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
                {
                    private ShopUnit(UnitId id, string name) : base(id, name) { }

                    public static ShopUnit Create(UnitId id, string name) => new(id, name);
                }

                [TenantAggregate<TenantId>]
                public sealed partial class ShopTenant;
                """,
                "Application.cs")
            .RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00049", at: "ShopUnit");
        diagnostic.GetMessage().Should().Be(
            "A class declared with [Organization] is needed by 'TenancyRegistrations.AddTenancy', and neither this project nor a project it references declares one; declare one, once");
        diagnostic.Properties.Should().Contain("ClassName", "ShopOrganization", "ShopUnit is declared with [OrganizationUnit], and ends in its last word")
            .And.Contain("IdName", "OrganizationId");
        result.Count("DDD00049").Should().Be(1, "only AddTenancy takes the organization");
        result.HintNames.Should().NotContain("TenancyRegistrations.AddTenancy.Registration");
        result.ShouldNotCrash();
    }

    [Fact]
    public void A_missing_template_class_a_parent_takes_from_is_said_once_on_that_class_and_the_registrations_stand_back()
    {
        // The organization's parent takes the tenant's id, and so does every registration here: one class is
        // missing, and it is said once, where its fix declares it, rather than once more for each registration.
        var result = Package()
            .WithSource(
                Usings +
                """
                [OrganizationUnit<UnitId>]
                public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
                {
                    private ShopUnit(UnitId id, string name) : base(id, name) { }

                    public static ShopUnit Create(UnitId id, string name) => new(id, name);
                }

                [Organization<OrganizationId>]
                public sealed partial class ShopOrganization;
                """,
                "Application.cs")
            .RunCore();

        result.ReportedDiagnostics.Select(diagnostic => diagnostic.Id).Should().Equal(["DDD00044"]);
        result.ShouldHaveDiagnostic("DDD00044", at: "ShopOrganization").Properties.Should().Contain("ClassName", "ShopTenant");
        result.HintNames.Should().NotContain("Registration", "a registration that lacks a class is written for none");
        result.ShouldNotCrash();
    }

    /// <summary>The real Tenancy package's classes, as an application declares them, without the ones a test leaves out.</summary>
    private static GeneratorTestHost WithTheTenancyPackage(params string[] leftOut)
    {
        var classes = new (string Name, string Declaration)[]
        {
            ("Tenant", "[TenantAggregate<TenantId>] public sealed partial class ShopTenant;"),
            ("Organization", "[OrganizationAggregate<TenantId>] public sealed partial class ShopOrganization;"),
            ("Unit", "[OrganizationUnit<OrganizationUnitId>] public sealed partial class ShopUnit;"),
            ("Seat", "[SeatAggregate<SeatId>] public sealed partial class ShopSeat;"),
            ("Role", "[RoleAggregate<RoleId>] public sealed partial class ShopRole;"),
        };

        const string Ids =
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Supporting.Tenancy;

            namespace Shop;

            [EntityId<long>]
            public readonly partial record struct TenantId;

            [EntityId<Guid>]
            public readonly partial record struct SeatId;

            [EntityId<Guid>]
            public readonly partial record struct OrganizationUnitId;

            [EntityId<Guid>]
            public readonly partial record struct RoleId;
            """;

        var application = string.Join(Environment.NewLine, new[] { Ids }.Concat(classes.Where(each => !leftOut.Contains(each.Name)).Select(each => each.Declaration)));

        return GeneratorTestHost.Create(application, "Application.cs").WithTenancyOnEntityFramework();
    }

    [Fact]
    public void A_missing_role_class_of_the_tenancy_package_is_said_once_on_the_seat_that_takes_its_id()
    {
        // Every registration of the package's needs the role class as well. One class is missing, and it is said
        // once, on the seat, whose parent takes the role's id, with the fix that declares it.
        var result = WithTheTenancyPackage("Role").RunCore();

        result.ReportedDiagnostics.Select(diagnostic => diagnostic.Id).Should().Equal(["DDD00044"]);
        result.ShouldHaveDiagnostic("DDD00044", at: "ShopSeat").GetMessage().Should().Contain("from the class declared with [RoleAggregate]");
    }

    [Fact]
    public void A_class_several_registrations_need_and_no_parent_takes_from_is_DDD00049_once_naming_them_all()
    {
        // No parent of the package's takes the organization: the registrations are what need it, and each of them
        // does. They say so once, naming every one, on the first class declared with one of their templates.
        var result = WithTheTenancyPackage("Organization").RunCore();

        result.ReportedDiagnostics.Select(diagnostic => diagnostic.Id).Should().Equal(["DDD00049"]);
        var message = result.ShouldHaveDiagnostic("DDD00049", at: "ShopTenant").GetMessage();
        message.Should().StartWith("A class declared with [OrganizationAggregate] is needed by 'Tenancy")
            .And.Contain("' and 'Tenancy", "more than one registration needs it, and each is named");
        System.Text.RegularExpressions.Regex.Matches(message, @"'Tenancy[A-Za-z]+\.AddTenancy[A-Za-z]*'").Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void A_class_missing_a_constraint_of_the_method_is_DDD00050()
    {
        const string Audited =
            """
            using DDDToolkit.Abstractions.Attributes;

            [assembly: TemplateRegistrations(typeof(Sample.Tenancy.AuditRegistrations))]

            namespace Sample.Tenancy;

            public interface IAudited;

            public static class AuditRegistrations
            {
                [TemplateRegistration]
                public static Registry AddAudit<[TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant>(this Registry registry)
                    where TTenant : class, IAudited
                    => registry;
            }
            """;

        var result = Together().WithSource(Audited, "Audit.cs").RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "ShopTenant").GetMessage()
            .Should().Contain("'AuditRegistrations.AddAudit' takes 'ShopTenant' as 'TTenant', which requires 'IAudited'");
        result.HintNames.Should().NotContain("AuditRegistrations.AddAudit");
        result.HintNames.Should().Contain("TenancyRegistrations.AddTenancy.Registration", "the other registrations are not held up by this one");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");

        var fixedResult = Together()
            .WithSource(Audited, "Audit.cs")
            .WithSource("namespace Sample; public sealed partial class ShopTenant : Sample.Tenancy.IAudited;", "Audited.cs")
            .RunCore();
        fixedResult.ShouldCompile();
        fixedResult.ShouldHaveGenerated("AuditRegistrations.AddAudit.Registration.");
    }

    [Fact]
    public void An_id_that_is_a_class_where_the_method_needs_a_struct_is_DDD00050()
    {
        const string ByValue =
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Abstractions.Interfaces;

            [assembly: TemplateRegistrations(typeof(Sample.Tenancy.ByValueRegistrations))]

            namespace Sample.Tenancy;

            public static class ByValueRegistrations
            {
                [TemplateRegistration]
                public static Registry AddByValue<
                    [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
                    [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(this Registry registry)
                    where TTenant : TenantAggregate<TTenantId>
                    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
                    => registry;
            }
            """;

        // The package's parent takes any id; this registration only a struct, and the application's is a class.
        var classIds = TemplateEntityTests.Ids.Replace("public readonly partial record struct TenantId;", "public partial record TenantId;", StringComparison.Ordinal);
        classIds.Should().NotBe(TemplateEntityTests.Ids);

        var result = GeneratorTestHost.Create(TemplateEntityTests.Package, "Package.cs")
            .WithSource(Registrations, "Registrations.cs")
            .WithSource(classIds, "Ids.cs")
            .WithSource(TemplateEntityTests.Application, "Application.cs")
            .WithSource(ByValue, "ByValue.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "ShopTenant").GetMessage()
            .Should().Contain("'ByValueRegistrations.AddByValue' takes 'TenantId' as 'TTenantId', which requires a struct");
        result.HintNames.Should().NotContain("ByValueRegistrations.AddByValue");
        result.HintNames.Should().Contain("TenancyRegistrations.AddTenancy.Registration", "a registration that takes any id is written");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");

        // The struct ids of the other tests meet it.
        var structs = Together().WithSource(ByValue, "ByValue.cs").RunCore();
        structs.ShouldCompile();
        structs.ShouldHaveGenerated("ByValueRegistrations.AddByValue.Registration.");
    }

    [Fact]
    public void A_template_class_declared_twice_is_DDD00045_naming_both()
    {
        var result = Package()
            .WithSource(TemplateEntityTests.Application, "Application.cs")
            .WithSource(
                Usings +
                """
                [Organization<OrganizationId>]
                public sealed partial class OtherOrganization
                {
                    public OtherOrganization(OrganizationId id, TenantId tenantId) : base(id, tenantId) { }
                }
                """,
                "Other.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00045", at: "ShopTenant").GetMessage()
            .Should().Contain("'TenancyRegistrations.AddTenancy' is declared with [TemplateRegistration], which takes 'TOrganization'")
            .And.Contain("'ShopOrganization' and 'OtherOrganization'; keep one");
        result.HintNames.Should().NotContain("TenancyRegistrations.AddTenancy.Registration");
        result.ShouldHaveGenerated("TenancyRegistrations.AddTenancyWith.Registration.");
    }

    [Fact]
    public void A_class_that_cannot_be_generated_leaves_the_registration_out_without_a_second_report()
    {
        // ShopTenant is not partial: DDD00005 says so, and a registration taking it would only add errors in
        // generated code. Its id can still be taken, as a template takes it.
        var result = Package()
            .WithSource(
                Usings +
                """
                [TenantAggregate<TenantId>]
                public class ShopTenant;

                [OrganizationUnit<UnitId>]
                public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
                {
                    private ShopUnit(UnitId id, string name) : base(id, name) { }

                    public static ShopUnit Create(UnitId id, string name) => new(id, name);
                }

                [Organization<OrganizationId>]
                public sealed partial class ShopOrganization
                {
                    public ShopOrganization(OrganizationId id, TenantId tenantId) : base(id, tenantId) { }
                }
                """,
                "Application.cs")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00005");
        result.HintNames.Should().NotContain("Registration");
    }
}
