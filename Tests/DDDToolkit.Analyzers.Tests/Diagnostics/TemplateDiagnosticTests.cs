using DDDToolkit.Analyzers.Tests.Generation;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00042 to DDD00047: a parent a package ships, and the templates that declare the application's
/// classes from it. Every one of them stops generation for the class it is reported on, and every test
/// checks that it stops without a crash and without a second report of the same mistake.
/// </summary>
public class TemplateDiagnosticTests
{
    private const string Usings =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using Sample.Tenancy;

        namespace Sample;


        """;

    private static GeneratorTestHost WithPackage(string application)
        => GeneratorTestHost.Create(TemplateEntityTests.Package, "Package.cs")
            .WithSource(TemplateEntityTests.Ids, "Ids.cs")
            .WithSource(Usings + application, "Application.cs");

    // ------------------------------------------------------------------ DDD00042

    [Fact]
    public void A_parent_that_is_not_abstract_reports_DDD00042()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [AggregateRootBase]
            public partial class Parent<TId> where TId : IEntityId, IEquatable<TId>;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00042", at: "Parent").GetMessage().Should().Contain("abstract");
        result.ShouldNotHaveGeneratedFor("Parent");
        result.ShouldNotCrash();
    }

    [Fact]
    public void A_parent_whose_id_is_not_constrained_reports_DDD00042_with_the_clause_to_write()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [EntityBase]
            public abstract partial class Parent<TId>;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00042", at: "Parent").GetMessage()
            .Should().Contain("where TId : IEntityId, IEquatable<TId>");
        result.ShouldNotHaveGeneratedFor("Parent");
    }

    [Fact]
    public void A_parent_without_type_parameters_reports_DDD00042()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [AggregateRootBase]
            public abstract partial class Parent;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00042", at: "Parent").GetMessage().Should().Contain("type parameters");
    }

    [Fact]
    public void A_parent_that_is_not_partial_reports_DDD00005_and_not_DDD00042()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [AggregateRootBase]
            public abstract class Parent<TId> where TId : IEntityId, IEquatable<TId>;
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00005");
    }

    // ------------------------------------------------------------------ DDD00043

    [Fact]
    public void A_template_over_a_raw_value_reports_DDD00043()
    {
        var result = WithPackage(
            """
            [TenantAggregate<Guid>]
            public partial class ShopTenant;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00043", at: "ShopTenant").GetMessage().Should().Contain("[EntityId<T>]");
        result.GeneratedSources.Should().NotContain(source => source.HintName.StartsWith("ShopTenant.", StringComparison.Ordinal));
        result.ShouldNotCrash();
    }

    // ------------------------------------------------------------------ DDD00044 and DDD00045

    private const string UnitAndOrganization =
        """
        [OrganizationUnit<UnitId>]
        public partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
        {
            private ShopUnit(UnitId id, string name) : base(id, name) { }

            public static ShopUnit Create(UnitId id, string name) => new(id, name);
        }

        [Organization<OrganizationId>]
        public partial class ShopOrganization;

        """;

    [Fact]
    public void A_template_whose_source_class_is_missing_reports_DDD00044_naming_the_attribute_to_use()
    {
        var result = WithPackage(UnitAndOrganization).RunCore();

        var message = result.ShouldHaveDiagnostic("DDD00044", at: "ShopOrganization").GetMessage();
        message.Should().Contain("[TenantAggregate]").And.Contain("'TTenantId'");
        result.GeneratedSources.Should().NotContain(source => source.HintName.StartsWith("ShopOrganization.", StringComparison.Ordinal));
        result.Count("DDD00044").Should().Be(1);
    }

    [Fact]
    public void A_template_whose_source_class_is_declared_twice_reports_DDD00045_naming_both()
    {
        var result = WithPackage(UnitAndOrganization +
            """
            [TenantAggregate<TenantId>]
            public partial class ShopTenant;

            [TenantAggregate<TenantId>]
            public partial class OtherTenant;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00045", at: "ShopOrganization").GetMessage()
            .Should().Contain("'ShopTenant'").And.Contain("'OtherTenant'");
        result.GeneratedSources.Should().NotContain(source => source.HintName.StartsWith("ShopOrganization.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_source_class_that_cannot_be_generated_still_provides_its_id()
    {
        // ShopTenant is not partial, so it reports DDD00005 and gets nothing. It is still the one tenant
        // class of the project, and saying it is missing would send the author looking for a mistake
        // they did not make.
        var result = WithPackage(UnitAndOrganization +
            """
            [TenantAggregate<TenantId>]
            public class ShopTenant;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00005", at: "ShopTenant");
        result.ShouldNotHaveDiagnostic("DDD00044");
        result.ShouldContain("ShopOrganization.", "global::Sample.TenantId");
    }

    // ------------------------------------------------------------------ DDD00046

    [Fact]
    public void A_template_that_leaves_a_parent_parameter_unfilled_reports_DDD00046()
    {
        var result = WithPackage(
            """
            [AggregateRootTemplate(typeof(OrganizationAggregate<,,,>))]
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class LooseOrganizationAttribute<TId> : Attribute;

            [LooseOrganization<OrganizationId>]
            public partial class ShopOrganization;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00046", at: "ShopOrganization").GetMessage()
            .Should().Contain("'TTenantId', 'TUnit' and 'TUnitId'");
        result.ShouldNotCrash();
    }

    [Fact]
    public void A_template_whose_parent_is_of_the_other_kind_reports_DDD00046()
    {
        var result = WithPackage(
            """
            [EntityTemplate(typeof(TenantAggregate<>))]
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ConfusedAttribute<TId> : Attribute;

            [Confused<TenantId>]
            public partial class ShopTenant;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00046", at: "ShopTenant").GetMessage().Should().Contain("[EntityBase]");
    }

    [Fact]
    public void A_template_that_fills_a_parameter_twice_reports_DDD00046()
    {
        var result = WithPackage(
            """
            [AggregateRootTemplate(typeof(TenantAggregate<>))]
            [TemplateArgument(0, typeof(TenantAggregateAttribute<>))]
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class TwiceAttribute<TId> : Attribute;

            [Twice<TenantId>]
            public partial class ShopTenant;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00046", at: "ShopTenant").GetMessage().Should().Contain("filled twice");
    }

    // ------------------------------------------------------------------ DDD00047

    [Fact]
    public void A_template_class_that_is_also_an_aggregate_root_reports_DDD00047_once_and_generates_nothing()
    {
        var result = WithPackage(
            """
            [AggregateRoot<TenantId>]
            [TenantAggregate<TenantId>]
            public partial class ShopTenant;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00047", at: "ShopTenant").GetMessage()
            .Should().Contain("[TenantAggregate]").And.Contain("[AggregateRoot]");
        result.Count("DDD00047").Should().Be(1, "the [AggregateRoot] path refuses silently and the template path reports");
        result.ShouldNotHaveDiagnostic("DDD00009");
        result.GeneratedSources.Should().NotContain(source => source.HintName.StartsWith("ShopTenant.", StringComparison.Ordinal));
        result.ShouldNotCrash();
    }

    [Fact]
    public void Two_templates_on_one_class_report_DDD00047_once()
    {
        var result = WithPackage(
            """
            [TenantAggregate<TenantId>]
            [Organization<OrganizationId>]
            public partial class Muddle;
            """).RunCore();

        result.Count("DDD00047").Should().Be(1);
        result.ShouldNotCrash();
    }

    [Fact]
    public void Both_parent_attributes_on_one_class_report_DDD00047_once()
    {
        var result = GeneratorTestHost.Create(Usings +
            """
            [AggregateRootBase]
            [EntityBase]
            public abstract partial class Parent<TId> where TId : IEntityId, IEquatable<TId>;
            """).RunCore();

        result.Count("DDD00047").Should().Be(1);
        result.ShouldNotHaveGeneratedFor("Parent");
        result.ShouldNotCrash();
    }
}
