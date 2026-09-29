using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The corners of parents and templates: a parent that is refused, a class that cannot be taken, a class
/// in another project, key parts on both sides, a class derived by hand, and what the other generators
/// make of a template class whose base class they cannot see.
/// <para>
/// Most of these once ended in compile errors inside generated code, or in a rule quietly left out.
/// The shared promise is that neither happens: a mistake is one diagnostic on the author's own code, and
/// a rule of the package runs wherever its parent is used.
/// </para>
/// </summary>
public class TemplateEdgeCaseTests
{
    private const string Usings =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using DDDToolkit.Invariants;
        using Sample.Tenancy;

        namespace Sample;


        """;

    /// <summary>The test application without its usings and namespace, to add to <see cref="Usings"/>.</summary>
    private static readonly string ApplicationBody = string.Join(
        "\n",
        TemplateEntityTests.Application.Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .Where(static line => !line.StartsWith("using ", StringComparison.Ordinal) && !line.StartsWith("namespace ", StringComparison.Ordinal)));

    private static GeneratorTestHost WithPackage(string application, string package = TemplateEntityTests.Package)
        => GeneratorTestHost.Create(package, "Package.cs")
            .WithSource(TemplateEntityTests.Ids, "Ids.cs")
            .WithSource(Usings + application, "Application.cs");

    private static void ShouldHaveNoErrorsInGeneratedCode(GeneratorRunOutcome result)
        => result.CompilationErrors
            .Where(diagnostic => diagnostic.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true)
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("a mistake is reported on the author's code, never as an error in code nobody can edit");

    private static bool Generated(GeneratorRunOutcome result, string typeName)
        => result.GeneratedSources.Any(source => source.HintName.StartsWith(typeName + ".", StringComparison.Ordinal));

    // ------------------------------------------------------------------ a refused parent

    [Theory]
    [InlineData("public partial class Plain<TId> where TId : IEntityId, IEquatable<TId>;", "DDD00042")]
    [InlineData("public abstract class Plain<TId> where TId : IEntityId, IEquatable<TId>;", "DDD00005")]
    public void A_class_derived_from_a_parent_its_own_path_refuses_gets_nothing_and_no_errors(string parent, string reported)
    {
        var result = GeneratorTestHost.Create(
            Usings.Replace("using Sample.Tenancy;\n", string.Empty) +
            $$"""
            [EntityId<int>]
            public readonly partial record struct PlainId;

            [AggregateRootBase]
            {{parent}}

            [AggregateRootTemplate(typeof(Plain<>))]
            public sealed class PlainAttribute<TId> : Attribute;

            [Plain<PlainId>]
            public partial class Thing;
            """).RunCore();

        result.ShouldHaveDiagnostic(reported, at: "Plain");
        Generated(result, "Thing").Should().BeFalse("the parent was refused, so there is nothing to derive from");
        ShouldHaveNoErrorsInGeneratedCode(result);
        result.ShouldNotCrash();
    }

    // ------------------------------------------------------------------ classes a template takes

    [Fact]
    public void A_class_a_template_takes_by_type_that_cannot_be_generated_refuses_the_dependent_silently()
    {
        var result = WithPackage(
            """
            [TenantAggregate<TenantId>]
            public partial class ShopTenant { public ShopTenant(TenantId id) : base(id, "shop") { } }

            [OrganizationUnit<UnitId>]
            public sealed class ShopUnit;

            [Organization<OrganizationId>]
            public partial class ShopOrganization;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00005", at: "ShopUnit");
        result.ShouldNotHaveDiagnostic("DDD00044");
        Generated(result, "ShopOrganization").Should().BeFalse();
        ShouldHaveNoErrorsInGeneratedCode(result);
    }

    [Fact]
    public void A_class_a_template_takes_an_id_from_whose_id_is_not_one_refuses_the_dependent_silently()
    {
        var result = WithPackage(ApplicationBody
            .Replace("[TenantAggregate<TenantId>]", "[TenantAggregate<Guid>]")).RunCore();

        result.ShouldHaveDiagnostic("DDD00043", at: "ShopTenant");
        result.ShouldNotHaveDiagnostic("DDD00044");
        Generated(result, "ShopOrganization").Should().BeFalse();
        ShouldHaveNoErrorsInGeneratedCode(result);
    }

    [Fact]
    public void A_class_whose_own_template_is_broken_still_counts_as_declared()
    {
        var broken = TemplateEntityTests.Package.Replace(
            "[AggregateRootTemplate(typeof(TenantAggregate<>))]",
            "[AggregateRootTemplate(typeof(OrganizationUnitEntity<>))]");

        var result = WithPackage(
            """
            [TenantAggregate<TenantId>]
            public partial class ShopTenant;

            [OrganizationUnit<UnitId>]
            public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
            {
                private ShopUnit(UnitId id, string name) : base(id, name) { }

                public static ShopUnit Create(UnitId id, string name) => new(id, name);
            }

            [Organization<OrganizationId>]
            public partial class ShopOrganization;
            """,
            broken).RunCore();

        result.ShouldHaveDiagnostic("DDD00046", at: "ShopTenant");
        result.ShouldNotHaveDiagnostic("DDD00044");
        result.ShouldContain("ShopOrganization.", "global::Sample.TenantId", "the tenant class is there, and its id is a good one");
    }

    [Fact]
    public void A_class_a_template_takes_that_misses_a_constraint_of_the_parent_reports_DDD00048()
    {
        var result = WithPackage(
            """
            [TenantAggregate<TenantId>]
            public partial class ShopTenant { public ShopTenant(TenantId id) : base(id, "shop") { } }

            [OrganizationUnit<UnitId>]
            public sealed partial class ShopUnit
            {
                private ShopUnit(UnitId id, string name) : base(id, name) { }
            }

            [Organization<OrganizationId>]
            public partial class ShopOrganization;
            """).RunCore();

        result.ShouldHaveDiagnostic("DDD00048", at: "ShopOrganization").GetMessage()
            .Should().Contain("'ShopUnit'").And.Contain("'TUnit'").And.Contain("IOrganizationUnitFactory<ShopUnit, UnitId>");
        Generated(result, "ShopOrganization").Should().BeFalse();
        ShouldHaveNoErrorsInGeneratedCode(result);
    }

    [Fact]
    public void A_class_a_template_takes_can_be_in_a_referenced_project()
    {
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
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint.Of("Sample.ShopOrganization"),
            "global::Sample.Tenancy.OrganizationAggregate<global::Sample.OrganizationId, global::Sample.TenantId, global::Sample.ShopUnit, global::Sample.UnitId>",
            "the tenant class lives in the tenancy module this one references");
    }

    // ------------------------------------------------------------------ key parts

    private const string Scoped =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        namespace Sample.Tenancy;

        [AggregateRootBase]
        public abstract partial class ScopedAggregate<TId>
            where TId : IEntityId, IEquatable<TId>
        {
            protected ScopedAggregate(TId id, string tenant) : base(id) => Tenant = tenant;

            [KeyPart]
            public string Tenant { get; private set; } = "";
        }

        [AggregateRootTemplate(typeof(ScopedAggregate<>))]
        public sealed class ScopedAttribute<TId> : Attribute;
        """;

    private const string ScopedApplication =
        """
        [EntityId<int>]
        public readonly partial record struct ProjectId;

        [EntityId<int>]
        public readonly partial record struct BoardId;

        [Scoped<ProjectId>]
        public sealed partial class Project
        {
            public Project(ProjectId id) : base(id, "acme") { }

            [KeyPart]
            public string Region { get; private set; } = "eu";
        }

        [Scoped<BoardId>]
        public sealed partial class Board
        {
            public Board(BoardId id) : base(id, "acme") { }
        }

        public static class KeyPartsOf
        {
            public static IReadOnlyList<string> Get<T>() where T : DDDToolkit.Interfaces.IHasKeyParts => T.KeyParts;
        }
        """;

    private static IReadOnlyList<string> KeyPartsOf(EmittedAssembly emitted, string typeName)
        => (IReadOnlyList<string>)emitted.Type("Sample.KeyPartsOf").GetMethod("Get")!
            .MakeGenericMethod(emitted.Type(typeName)).Invoke(null, null)!;

    [Fact]
    public void The_parents_key_parts_stay_in_front_of_the_classs_own()
    {
        var result = GeneratorTestHost.Create(Scoped, "Package.cs").WithSource(Usings + ScopedApplication, "Application.cs").RunCore();

        result.ShouldCompile();
        var emitted = result.Emit();

        KeyPartsOf(emitted, "Sample.Project").Should().Equal(["Tenant", "Region"], "the tenant stays in the key when the class adds a part of its own");
        KeyPartsOf(emitted, "Sample.Board").Should().Equal(["Tenant"], "a class without key parts of its own has the parent's");
    }

    [Fact]
    public void The_parents_key_parts_stay_in_front_when_the_parent_is_a_referenced_assembly()
    {
        var result = GeneratorTestHost.Create(Usings + ScopedApplication, "Application.cs")
            .WithReferencedAssembly(Scoped, "Sample.Tenancy")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Project"), "global::System.Linq.Enumerable.Concat(__BaseKeyParts, new string[] { nameof(Region) })");
    }

    [Fact]
    public void An_access_function_about_a_class_whose_parent_has_key_parts_gets_no_by_id_overload()
    {
        var result = GeneratorTestHost.Create(Scoped, "Package.cs")
            .WithSource(Usings + ScopedApplication, "Application.cs")
            .WithSource(
                Usings +
                """
                [AccessFunction<Project>("projects.is_visible")]
                public static partial class ProjectVisible
                {
                    public static bool Allows(Project project, Caller caller) => project.Region == "eu";
                }
                """,
                "Rule.cs")
            .RunCore();

        result.ShouldCompile();
        result.AllSources.Should().NotContain("Allows(global::Sample.ProjectId", "the function's key is the tenant and the id, not the id alone");
    }

    // ------------------------------------------------------------------ the parent's rules are not skipped

    [Fact]
    public void A_class_derived_from_a_parent_by_hand_still_runs_the_parents_rules()
    {
        var result = GeneratorTestHost.Create(TemplateEntityTests.Package, "Package.cs")
            .WithSource(TemplateEntityTests.Ids, "Ids.cs")
            .WithSource(
                Usings +
                """
                public sealed class RogueTenant : TenantAggregate<TenantId>
                {
                    public RogueTenant(TenantId id) : base(id, "") { }
                }
                """,
                "Rogue.cs")
            .RunCore();

        result.ShouldCompile();
        var emitted = result.Emit();
        var rogue = (IHasInvariants)emitted.New("Sample.RogueTenant", emitted.New("Sample.TenantId", 1));

        var violation = rogue.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("tenant.name");
        violation.EntityType!.FullName.Should().Be("Sample.RogueTenant");
        rogue.Invoking(tenant => tenant.EnsureOwnInvariants()).Should().Throw<InvariantViolationException>();
    }

    [Fact]
    public void A_class_derived_from_a_template_class_is_reported_as_one_object()
    {
        var result = GeneratorTestHost.Create(TemplateEntityTests.Package, "Package.cs")
            .WithSource(TemplateEntityTests.Ids, "Ids.cs")
            .WithSource(
                Usings +
                """
                [TenantAggregate<TenantId>]
                public abstract partial class Tree
                {
                    protected Tree(TenantId id) : base(id, "") { }

                    public sealed class Own : IInvariant<Tree>
                    {
                        public string Code => "tree.own";

                        public InvariantFailure? Check(Tree entity) => "Always wrong.";
                    }
                }

                public sealed class Oak : Tree
                {
                    public Oak(TenantId id) : base(id) { }
                }
                """,
                "Tree.cs")
            .RunCore();

        result.ShouldCompile();
        var emitted = result.Emit();
        var oak = (IHasInvariants)emitted.New("Sample.Oak", emitted.New("Sample.TenantId", 1));

        oak.GetInvariantViolations().Select(violation => violation.EntityType!.FullName)
            .Should().Equal(["Sample.Tree", "Sample.Tree"], "the parent's violation and the class's own are about one object, and name it the same way");
    }

    [Fact]
    public void A_rule_about_an_interface_the_parent_implements_runs_like_the_classs_own()
    {
        var result = GeneratorTestHost.Create(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Abstractions.Interfaces;

                namespace Sample.Tenancy;

                public interface INamed
                {
                    string Name { get; }
                }

                [AggregateRootBase]
                public abstract partial class NamedAggregate<TId> : INamed
                    where TId : IEntityId, IEquatable<TId>
                {
                    protected NamedAggregate(TId id, string name) : base(id) => Name = name;

                    public string Name { get; private set; } = "";
                }

                [AggregateRootTemplate(typeof(NamedAggregate<>))]
                public sealed class NamedAttribute<TId> : Attribute;
                """,
                "Package.cs")
            .WithSource(
                Usings +
                """
                [EntityId<int>]
                public readonly partial record struct ClubId;

                [Named<ClubId>]
                public sealed partial class Club
                {
                    public Club(ClubId id, string name) : base(id, name) { }

                    public sealed class HasAName : IInvariant<INamed>
                    {
                        public string Code => "club.name";

                        public InvariantFailure? Check(INamed entity) => entity.Name.Length > 0 ? null : "A club has a name.";
                    }
                }
                """,
                "Application.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00025");

        var emitted = result.Emit();
        var club = (IHasInvariants)emitted.New("Sample.Club", emitted.New("Sample.ClubId", 1), "");
        club.GetInvariantViolations().Should().ContainSingle().Which.Code.Should().Be("club.name");
    }

    // ------------------------------------------------------------------ the aggregate boundary

    [Fact]
    public void A_child_pointing_back_at_the_template_root_that_holds_it_is_allowed_and_another_root_is_not()
    {
        var result = WithPackage(
            """
            [TenantAggregate<TenantId>]
            public partial class ShopTenant { public ShopTenant(TenantId id) : base(id, "shop") { } }

            [OrganizationUnit<UnitId>]
            public sealed partial class ShopUnit : IOrganizationUnitFactory<ShopUnit, UnitId>
            {
                private ShopUnit(UnitId id, string name) : base(id, name) { }

                public ShopOrganization? Organization { get; private set; }

                public ShopTenant? Tenant { get; private set; }

                public static ShopUnit Create(UnitId id, string name) => new(id, name);
            }

            [Organization<OrganizationId>]
            public partial class ShopOrganization;
            """).RunCore();

        result.Count("DDD00021").Should().Be(1, "only the tenant is another aggregate; the organization holds this unit");
        result.ShouldHaveDiagnostic("DDD00021", at: "Tenant");
    }

    // ------------------------------------------------------------------ row access rules

    [Fact]
    public void A_row_access_rule_about_a_parent_is_refused_because_the_export_could_not_find_its_table()
    {
        var result = WithPackage(
            ApplicationBody +
            """

            [RowAccess<TenantAggregate<TenantId>>(RowOperations.Read)]
            public static partial class NamedTenants
            {
                public static bool Allows(TenantAggregate<TenantId> tenant, Caller caller) => tenant.Name != "";
            }
            """).RunCore();

        result.Count("DDD00040").Should().Be(1);
    }

    [Fact]
    public void An_access_function_can_ask_about_a_collection_the_parent_holds()
    {
        var result = WithPackage(
            ApplicationBody +
            """

            [AccessFunction<ShopOrganization>("org.has_named_unit")]
            public static partial class HasNamedUnit
            {
                public static bool Allows(ShopOrganization organization, Caller caller) => organization.Units.Any(unit => unit.Name == "x");
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00039");
        result.AllSources.Should().Contain("{exists:Units:e1}").And.Contain("{col:e1:Name}");
    }

    [Fact]
    public void Value_of_a_value_object_the_parent_holds_is_a_column_of_its_own()
    {
        var result = GeneratorTestHost.Create(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Abstractions.Interfaces;

                namespace Sample.Tenancy;

                [ValueObject]
                public partial record Quota
                {
                    public int Value { get; protected init; }

                    public string Unit { get; protected init; } = "";
                }

                [AggregateRootBase]
                public abstract partial class LimitedAggregate<TId>
                    where TId : IEntityId, IEquatable<TId>
                {
                    public Quota Limit { get; private set; } = null!;
                }

                [AggregateRootTemplate(typeof(LimitedAggregate<>))]
                public sealed class LimitedAttribute<TId> : Attribute;
                """,
                "Package.cs")
            .WithSource(
                Usings +
                """
                [EntityId<int>]
                public readonly partial record struct ClubId;

                [Limited<ClubId>]
                public sealed partial class Club;

                [RowAccess<Club>(RowOperations.Read)]
                public static partial class BigClubs
                {
                    public static bool Allows(Club club, Caller caller) => club.Limit.Value > 10 && club.Id.Value > 0;
                }
                """,
                "Application.cs")
            .RunCore();

        result.ShouldCompile();
        result.AllSources.Should().Contain("{col:Limit.Value}", "Value is a property of the quota, not the quota's own value")
            .And.Contain("{col:Id}", "an id keeps its value in its own column");
    }
}
