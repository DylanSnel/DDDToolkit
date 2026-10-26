using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A package ships a parent, the application extends it. The package declares an abstract generic
/// parent with <c>[AggregateRootBase]</c> or <c>[EntityBase]</c> and an attribute marked
/// <c>[AggregateRootTemplate]</c> or <c>[EntityTemplate]</c>; the application declares its own class with
/// that attribute, <c>[TenantAggregate&lt;TenantId&gt;]</c>, and the generator writes the base class, closed
/// over the application's own ids.
/// <para>
/// What matters most is that the parent's rules cannot be skipped by the class that extends it: the
/// package states what a tenant must be, and an application that adds a property of its own still
/// answers for all of it.
/// </para>
/// </summary>
public class TemplateEntityTests
{
    /// <summary>What a package such as DDDToolkit.Supporting.Tenancy would ship: parents, templates, one of each kind.</summary>
    internal const string Package =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using DDDToolkit.Invariants;

        namespace Sample.Tenancy;

        [AggregateRootBase]
        public abstract partial class TenantAggregate<TTenantId>
            where TTenantId : IEntityId, IEquatable<TTenantId>
        {
            protected TenantAggregate(TTenantId id, string name) : base(id) => Name = name;

            public string Name { get; private set; } = "";

            public void Rename(string name) => Name = name;

            partial void CheckInvariants()
            {
                if (Name == "boom")
                {
                    throw InvariantViolation("The parent's seam refuses this name.");
                }
            }

            public sealed class NameIsRequired : IInvariant<TenantAggregate<TTenantId>>
            {
                public string Code => "tenant.name";

                public InvariantFailure? Check(TenantAggregate<TTenantId> entity) => entity.Name.Length > 0 ? null : "A tenant has a name.";
            }
        }

        [AggregateRootTemplate(typeof(TenantAggregate<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class TenantAggregateAttribute<TTenantId> : Attribute;

        [EntityBase]
        public abstract partial class OrganizationUnitEntity<TUnitId>
            where TUnitId : IEntityId, IEquatable<TUnitId>
        {
            protected OrganizationUnitEntity(TUnitId id, string name) : base(id) => Name = name;

            public string Name { get; private set; } = "";

            public sealed class NameIsRequired : IInvariant<OrganizationUnitEntity<TUnitId>>
            {
                public string Code => "unit.name";

                public InvariantFailure? Check(OrganizationUnitEntity<TUnitId> entity) => entity.Name.Length > 0 ? null : "A unit has a name.";
            }
        }

        [EntityTemplate(typeof(OrganizationUnitEntity<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class OrganizationUnitAttribute<TUnitId> : Attribute;

        /// <summary>How the package creates the application's own unit without knowing its class.</summary>
        public interface IOrganizationUnitFactory<TSelf, TUnitId>
            where TSelf : IOrganizationUnitFactory<TSelf, TUnitId>
        {
            static abstract TSelf Create(TUnitId id, string name);
        }

        [AggregateRootBase]
        public abstract partial class OrganizationAggregate<TOrganizationId, TTenantId, TUnit, TUnitId>
            where TOrganizationId : IEntityId, IEquatable<TOrganizationId>
            where TTenantId : IEntityId, IEquatable<TTenantId>
            where TUnit : OrganizationUnitEntity<TUnitId>, IOrganizationUnitFactory<TUnit, TUnitId>
            where TUnitId : IEntityId, IEquatable<TUnitId>
        {
            protected OrganizationAggregate(TOrganizationId id, TTenantId tenantId) : base(id) => TenantId = tenantId;

            public TTenantId TenantId { get; private set; }

            public partial IReadOnlyList<TUnit> Units { get; }

            public TUnit AddUnit(TUnitId id, string name)
            {
                var unit = TUnit.Create(id, name);
                _units.Add(unit);
                return unit;
            }
        }

        [AggregateRootTemplate(typeof(OrganizationAggregate<,,,>))]
        [TemplateArgument(1, typeof(TenantAggregateAttribute<>))]
        [TemplateArgument(2, typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)]
        [TemplateArgument(3, typeof(OrganizationUnitAttribute<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class OrganizationAttribute<TOrganizationId> : Attribute;
        """;

    /// <summary>The ids an application declares, in what would be its contracts project.</summary>
    internal const string Ids =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Sample;

        [EntityId<int>]
        public readonly partial record struct TenantId;

        [EntityId<int>]
        public readonly partial record struct OrganizationId;

        [EntityId<int>]
        public readonly partial record struct UnitId;
        """;

    /// <summary>What an application writes: its own classes, declared with the package's templates.</summary>
    internal const string Application =
        """
        using DDDToolkit.Invariants;
        using Sample.Tenancy;

        namespace Sample;

        [TenantAggregate<TenantId>]
        public sealed partial class ShopTenant
        {
            public ShopTenant(TenantId id, string name, string plan) : base(id, name) => Plan = plan;

            public string Plan { get; private set; }

            partial void CheckInvariants()
            {
                if (Plan == "boom")
                {
                    throw InvariantViolation("The shop's seam refuses this plan.");
                }
            }

            public sealed class PlanIsSold : IInvariant<ShopTenant>
            {
                public string Code => "shop.plan";

                public InvariantFailure? Check(ShopTenant entity) => entity.Plan is "free" or "paid" or "boom" ? null : "A shop is on a plan we sell.";
            }
        }

        [OrganizationUnit<UnitId>]
        public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
        {
            private ShopUnit(UnitId id, string name) : base(id, name) { }

            public string? CostCentre { get; private set; }

            public static ShopUnit Create(UnitId id, string name) => new(id, name);
        }

        [Organization<OrganizationId>]
        public sealed partial class ShopOrganization
        {
            public ShopOrganization(OrganizationId id, TenantId tenantId) : base(id, tenantId) { }
        }
        """;

    private static GeneratorRunOutcome RunTogether()
        => GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Application, "Application.cs")
            .RunCore();

    /// <summary>
    /// The fragment that finds a parent's generated part. Its hint name hashes the name with its type
    /// parameters, <c>TenantAggregate&lt;TTenantId&gt;</c>, and the name and dot are distinctive enough.
    /// </summary>
    private static string ParentHint(string name) => name + ".";

    private static IHasInvariants Tenant(EmittedAssembly emitted, string name, string plan)
        => (IHasInvariants)emitted.New("Sample.ShopTenant", emitted.New("Sample.TenantId", 1), name, plan);

    // ------------------------------------------------------------------ what is generated

    [Fact]
    public void The_parent_derives_from_the_toolkits_base_class_over_its_id_parameter()
    {
        var result = RunTogether();

        result.ShouldCompile();
        result.ShouldContain(
            ParentHint("TenantAggregate"),
            "partial class TenantAggregate<TTenantId> : global::DDDToolkit.BaseTypes.AggregateRoot<TTenantId>");
        result.ShouldContain(
            ParentHint("OrganizationUnitEntity"),
            "partial class OrganizationUnitEntity<TUnitId> : global::DDDToolkit.BaseTypes.Entity<TUnitId>");
    }

    [Fact]
    public void A_class_declared_with_a_template_derives_from_the_parent_closed_over_the_applications_id()
    {
        var result = RunTogether();

        result.ShouldCompile();
        result.ShouldContain(
            Hint.Of("Sample.ShopTenant"),
            "partial class ShopTenant : global::Sample.Tenancy.TenantAggregate<global::Sample.TenantId>");
        result.ShouldContain(
            Hint.Of("Sample.ShopUnit"),
            "partial class ShopUnit : global::Sample.Tenancy.OrganizationUnitEntity<global::Sample.UnitId>");
    }

    [Fact]
    public void A_template_argument_is_taken_from_the_class_declared_with_the_template_it_names()
    {
        var result = RunTogether();

        result.ShouldCompile();
        result.ShouldContain(
            Hint.Of("Sample.ShopOrganization"),
            "partial class ShopOrganization : global::Sample.Tenancy.OrganizationAggregate<global::Sample.OrganizationId, global::Sample.TenantId, global::Sample.ShopUnit, global::Sample.UnitId>",
            "the tenant's id and the unit class and its id come from the application's own template classes");
    }

    [Fact]
    public void The_parent_offers_its_rules_to_the_class_declared_with_its_template_and_names_that_class()
    {
        var result = RunTogether();

        result.ShouldCompile();

        var parent = ParentHint("TenantAggregate");
        result.ShouldContain(parent, "protected void CollectBaseInvariantViolations(");
        result.ShouldContain(parent, "protected void CollectBaseChildInvariantViolations(");
        result.ShouldContain(parent, "global::System.Type entityType)", "the class that calls the parent says which type reports");

        result.ShouldContain(
            Hint.Of("Sample.ShopTenant"),
            "CollectBaseInvariantViolations(ref violations, ref seamFailure, typeof(global::Sample.ShopTenant));");
    }

    [Fact]
    public void Generated_code_adds_no_warnings()
    {
        var result = RunTogether();

        result.ShouldCompile();
        result.CompilationDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("a host building with TreatWarningsAsErrors must be able to use a package's parents");
    }

    // ------------------------------------------------------------------ the parent's rules run

    [Fact]
    public void The_parents_rule_runs_for_the_class_that_extends_it_and_names_that_class()
    {
        var emitted = RunTogether().Emit();

        Tenant(emitted, "Acme", "free").GetInvariantViolations().Should().BeEmpty();

        var violation = Tenant(emitted, "", "free").GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be("tenant.name", "the package states that a tenant has a name");
        violation.EntityType!.FullName.Should().Be("Sample.ShopTenant", "the object that is wrong is the application's, not the package's parent");
        violation.EntityId.Should().Be(emitted.New("Sample.TenantId", 1));
    }

    [Fact]
    public void The_parents_rules_come_before_the_classs_own_and_both_come_back_from_one_call()
    {
        var emitted = RunTogether().Emit();

        var violations = Tenant(emitted, "", "gold").GetInvariantViolations();

        violations.Select(violation => violation.Code).Should().Equal("tenant.name", "shop.plan");
    }

    [Fact]
    public void The_save_path_runs_the_parents_rules_too()
    {
        var emitted = RunTogether().Emit();

        var exception = Tenant(emitted, "", "free").Invoking(tenant => tenant.EnsureOwnInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.AggregateType!.FullName.Should().Be("Sample.ShopTenant");
        exception.Violations.Should().ContainSingle().Which.Should().Contain("A tenant has a name.");
    }

    [Fact]
    public void Both_seams_run_and_the_first_that_throws_is_kept_as_the_inner_exception()
    {
        var emitted = RunTogether().Emit();

        var violations = Tenant(emitted, "boom", "boom").GetInvariantViolations();
        violations.Should().HaveCount(2, "the parent's seam and the class's seam both refuse");
        violations.Select(violation => violation.Code).Should().OnlyContain(code => code == "CheckInvariants");

        var exception = Tenant(emitted, "boom", "boom").Invoking(tenant => tenant.EnsureInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.InnerException.Should().NotBeNull();
        exception.InnerException!.Message.Should().Contain("parent's seam", "the parent ran first, so its failure is the one kept");
    }

    [Fact]
    public void The_parent_walks_its_children_of_the_applications_own_class()
    {
        var emitted = RunTogether().Emit();

        var organization = emitted.New("Sample.ShopOrganization", emitted.New("Sample.OrganizationId", 1), emitted.New("Sample.TenantId", 1));
        emitted.Call(organization, "AddUnit", emitted.New("Sample.UnitId", 1), "Head office");
        emitted.Call(organization, "AddUnit", emitted.New("Sample.UnitId", 2), "");

        var violation = ((IHasInvariants)organization).GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be("unit.name", "the unit's parent states that a unit has a name");
        violation.EntityType!.FullName.Should().Be("Sample.ShopUnit");
        violation.EntityId.Should().Be(emitted.New("Sample.UnitId", 2));
    }

    [Fact]
    public void A_rule_of_the_application_about_the_parent_runs_like_its_own()
    {
        // The generator cannot see ShopTenant's base class, which it writes itself, so it takes the
        // template's word for what the parent will be.
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(
                """
                using DDDToolkit.Invariants;
                using Sample.Tenancy;

                namespace Sample;

                [TenantAggregate<TenantId>]
                public partial class ShopTenant
                {
                    public ShopTenant(TenantId id, string name) : base(id, name) { }

                    public sealed class NameIsShort : IInvariant<TenantAggregate<TenantId>>
                    {
                        public string Code => "shop.name";

                        public InvariantFailure? Check(TenantAggregate<TenantId> entity) => entity.Name.Length <= 5 ? null : "A shop's name is short.";
                    }
                }
                """,
                "Application.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00025");

        var emitted = result.Emit();
        var tenant = (IHasInvariants)emitted.New("Sample.ShopTenant", emitted.New("Sample.TenantId", 1), "Much too long");

        tenant.GetInvariantViolations().Should().ContainSingle().Which.Code.Should().Be("shop.name");
    }

    [Fact]
    public void A_rule_about_the_parent_over_another_id_is_about_another_type()
    {
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(
                """
                using DDDToolkit.Invariants;
                using Sample.Tenancy;

                namespace Sample;

                [TenantAggregate<TenantId>]
                public partial class ShopTenant
                {
                    public ShopTenant(TenantId id, string name) : base(id, name) { }

                    public sealed class Elsewhere : IInvariant<TenantAggregate<UnitId>>
                    {
                        public string Code => "shop.elsewhere";

                        public InvariantFailure? Check(TenantAggregate<UnitId> entity) => null;
                    }
                }
                """,
                "Application.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00025", at: "Elsewhere");
    }

    // ------------------------------------------------------------------ across an assembly boundary

    [Fact]
    public void The_package_can_be_a_referenced_assembly()
    {
        var result = GeneratorTestHost.Create(Ids, "Ids.cs")
            .WithReferencedAssembly(Package, "Sample.Tenancy")
            .WithSource(Application, "Application.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.ShopTenant"), "partial class ShopTenant : global::Sample.Tenancy.TenantAggregate<global::Sample.TenantId>");
        result.ShouldContain(
            Hint.Of("Sample.ShopOrganization"),
            "global::Sample.Tenancy.OrganizationAggregate<global::Sample.OrganizationId, global::Sample.TenantId, global::Sample.ShopUnit, global::Sample.UnitId>");
        result.GeneratedSources.Should().NotContain(
            source => source.HintName.StartsWith("TenantAggregate.", StringComparison.Ordinal),
            "the parent was generated when the package was built, and generating it again would declare its members twice");
    }

    // ------------------------------------------------------------------ Entity Framework

    [Fact]
    public void A_child_entity_declared_with_a_template_is_owned_and_nothing_else_is()
    {
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Application, "Application.cs")
            .WithEntityFramework()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.ShopUnit", ".EntityFramework"), "[global::Microsoft.EntityFrameworkCore.Owned]");
        result.ShouldContain(ParentHint("OrganizationAggregate"), "[global::Microsoft.EntityFrameworkCore.BackingField(nameof(_units))]");

        var emitted = result.Emit();
        emitted.Type("Sample.ShopUnit")
            .GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.OwnedAttribute), inherit: false)
            .Should().HaveCount(1);

        foreach (var notOwned in new[] { "Sample.ShopTenant", "Sample.ShopOrganization", "Sample.Tenancy.OrganizationUnitEntity`1" })
        {
            emitted.Type(notOwned)
                .GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.OwnedAttribute), inherit: true)
                .Should().BeEmpty(notOwned + " is a root or an open parent, which Entity Framework never maps as owned");
        }
    }

    // ------------------------------------------------------------------ everything else still recognises them

    [Fact]
    public void A_rule_nested_in_a_template_class_or_a_parent_is_where_it_belongs()
    {
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Application, "Application.cs")
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00024");
    }

    [Fact]
    public void A_row_access_rule_guards_a_template_aggregate_and_reads_the_parents_properties_as_columns()
    {
        // The rule generator cannot see the generated base class, so Name is not a member of ShopTenant as
        // far as it knows. It translates the member access as written, which is the column all the same.
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Application, "Application.cs")
            .WithSource(
                """
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;

                namespace Sample;

                [RowAccess<ShopTenant>(RowOperations.Read)]
                public static partial class NamedPaidShops
                {
                    public static bool Allows(ShopTenant tenant, Caller caller) => tenant.Name != "" && tenant.Plan == "paid";
                }
                """,
                "Rule.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00040");
        result.ShouldContain("NamedPaidShops.RowAccess", "{col:Name}");
        result.ShouldContain("NamedPaidShops.RowAccess", "{col:Plan}");
    }

    [Fact]
    public void A_template_class_holding_another_aggregate_is_told_to_hold_its_id()
    {
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Ids, "Ids.cs")
            .WithSource(Application, "Application.cs")
            .WithSource(
                """
                using DDDToolkit.Abstractions.Attributes;

                namespace Sample;

                [EntityId<int>]
                public readonly partial record struct ShopId;

                [AggregateRoot<ShopId>]
                public partial class Shop
                {
                    public ShopTenant? Tenant { get; private set; }
                }
                """,
                "Shop.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00021", at: "Tenant").GetMessage().Should().Contain("TenantId");
    }
}
