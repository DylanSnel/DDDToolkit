using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// Tenancy's questions as a rule asks them of its user, closed over the module's ids: <c>user.CurrentSeatId()</c> for
/// <c>user.CurrentSeatId&lt;SeatId&gt;()</c>. A wrapper is another generator's output, so the generator that translates a
/// rule sees none in the rule's own project and reads the generic question it closes. The package is the real one, seen
/// through metadata the way an application sees it.
/// </summary>
public class TenancyQuestionsGeneratorTests
{
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

    /// <summary>An aggregate of a module that sees Tenancy's ids, with something to compare every question with.</summary>
    private const string Report =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using Shop.Contracts;

        namespace Shop.Reports;

        [EntityId<Guid>]
        public readonly partial record struct ReportId;

        [AggregateRoot<ReportId>]
        public partial class Report
        {
            public Report(ReportId id) : base(id) { }

            public TenantId TenantId { get; private set; }

            public SeatId? WrittenBy { get; private set; }

            public OrganizationUnitId UnitId { get; private set; }

            public RoleId RoleId { get; private set; }
        }
        """;

    private const string TheLine = "[assembly: DDDToolkit.Supporting.Tenancy.TenancyIds<Shop.Contracts.TenantId, Shop.Contracts.SeatId, Shop.Contracts.OrganizationUnitId, Shop.Contracts.RoleId>]";

    /// <summary>A rule about a report whose <c>Allows</c> is <paramref name="expression"/>.</summary>
    private static string Rule(string expression)
        => $$"""
             using DDDToolkit.Abstractions.Access;
             using DDDToolkit.Abstractions.Attributes;
             using Shop.Contracts;

             namespace Shop.Reports;

             [RowAccess<Report>(RowOperations.Read, To = [RowAccessRoles.User])]
             public static partial class TheRule
             {
                 public static bool Allows(Report report, IRowUser user) => {{expression}};
             }
             """;

    /// <summary>The questions asked without type arguments, and with their arguments where they have any.</summary>
    public static TheoryData<string, string> Questions => new()
    {
        { "user.UnitsWhereIHold(\"reports.read\").Contains(report.UnitId)", "({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/units_where_i_hold_permission}('reports.read'))))" },
        { "user.ReadableUnits().Contains(report.UnitId)", "({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/readable_units}())))" },
        { "user.SeatsInMyUnits().Contains(report.WrittenBy!.Value)", "({col:WrittenBy} = ANY (ARRAY(SELECT {fn:tenancy/seats_in_my_units}())))" },
        { "user.RolesWithPermission(\"reports.read\").Contains(report.RoleId)", "({col:RoleId} = ANY (ARRAY(SELECT {fn:tenancy/roles_with_permission}('reports.read'))))" },
        { "report.WrittenBy == user.CurrentSeatId()", "({col:WrittenBy} = (SELECT {fn:tenancy/current_seat}()))" },
        { "user.CurrentSeatId() != report.WrittenBy", "((SELECT {fn:tenancy/current_seat}()) <> {col:WrittenBy})" },
        { "report.TenantId == user.CurrentTenant()", "({col:TenantId} = (SELECT {fn:tenancy/current_tenant}()))" },
        { "user.HoldsPermission(\"reports.read\")", "(SELECT {fn:tenancy/holds_permission}('reports.read'))" },
        { "user.HoldsAtTenant(\"reports.read\")", "(SELECT {fn:tenancy/holds_at_tenant}('reports.read'))" },
        { "report.WrittenBy == user.SeatInTenant(report.TenantId)", "({col:WrittenBy} = {fn:tenancy/seat_in_tenant}({col:TenantId}))" },
        { "user.SeatedInTenant(report.TenantId)", "{fn:tenancy/seated_in_tenant}({col:TenantId})" },
        { "user.HoldsPermissionInTenant(report.TenantId, \"reports.read\")", "{fn:tenancy/holds_permission_in_tenant}({col:TenantId}, 'reports.read')" },
    };

    [Theory]
    [MemberData(nameof(Questions))]
    public void With_the_line_and_the_rule_in_one_project_the_generator_reads_the_generic_question_behind_the_wrapper(string expression, string sql)
    {
        var result = GeneratorTestHost.Create(Ids, "Ids.cs")
            .WithSource(TheLine, "Line.cs")
            .WithSource(Report, "Report.cs")
            .WithSource(Rule(expression), "TheRule.cs")
            .WithTenancy()
            .WithModuleFromTheBuild("Reports")
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        SqlOf(result).Should().Be(sql);
    }

    [Theory]
    [MemberData(nameof(Questions))]
    public void With_the_line_below_the_rule_the_rules_project_has_its_own_wrappers(string expression, string sql)
    {
        // The line in the application project, the rule in the infrastructure project above it: a wrapper is internal,
        // so what the project below holds does not reach the rule.
        var result = RuleAboveTheLine(expression);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        SqlOf(result).Should().Be(sql);
    }

    [Fact]
    public void The_generic_form_keeps_working_and_writes_the_same_sql()
    {
        var result = RuleAboveTheLine("user.UnitsWhereIHold<OrganizationUnitId>(\"reports.read\").Contains(report.UnitId) && report.WrittenBy == user.CurrentSeatId<SeatId>()");

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        SqlOf(result).Should().Be("(({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/units_where_i_hold_permission}('reports.read')))) AND ({col:WrittenBy} = (SELECT {fn:tenancy/current_seat}())))");
    }

    [Fact]
    public void The_static_form_carries_the_user_as_an_argument_which_adds_nothing_to_the_sql()
    {
        var result = RuleAboveTheLine(
            "TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(user, \"reports.read\").Contains(report.UnitId)"
            + " && report.WrittenBy == TenancyRowAccess.CurrentSeatId<SeatId>(user)"
            + " && TenancyRowAccess.HoldsAtTenant(user, \"reports.read\")");

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        SqlOf(result).Should().Be(
            "((({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/units_where_i_hold_permission}('reports.read')))) AND ({col:WrittenBy} = (SELECT {fn:tenancy/current_seat}()))) AND (SELECT {fn:tenancy/holds_at_tenant}('reports.read')))");
    }

    [Theory]
    [InlineData("RowUser.From(CurrentUserSnapshot.Anonymous).CurrentSeatId() == report.WrittenBy", "RowUser.From(CurrentUserSnapshot.Anonymous).CurrentSeatId()")]
    [InlineData("TenancyRowAccess.HoldsAtTenant(RowUser.From(CurrentUserSnapshot.Anonymous), \"reports.read\")", "TenancyRowAccess.HoldsAtTenant(RowUser.From(CurrentUserSnapshot.Anonymous), \"reports.read\")")]
    public void A_question_asked_of_anything_but_the_rules_user_is_DDD00039(string expression, string at)
        => RuleAboveTheLine(expression).ShouldHaveDiagnostic("DDD00039", at: at);

    private static GeneratorRunOutcome RuleAboveTheLine(string expression)
        => GeneratorTestHost.Create(Report, "Report.cs")
            .WithSource(Rule(expression), "TheRule.cs")
            .WithAssemblyName("Shop.Reports.Infrastructure")
            .WithTenancy()
            .WithModuleFromTheBuild("Reports")
            .WithReferencedProject("Shop.Contracts", project => project.WithSource(Ids, "Ids.cs").WithModuleFromTheBuild("Tenants"))
            .WithReferencedProject("Shop.Reports.Application", project => project.WithSource(TheLine, "Line.cs").WithModuleFromTheBuild("Reports"))
            .RunCore();

    [Fact]
    public void A_module_that_declares_the_classes_gives_its_project_above_them_the_wrappers_as_well()
    {
        var result = GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;
                using Shop.Contracts;
                using Shop.Domain;

                namespace Shop.Infrastructure;

                [RowAccess<Seat>(RowOperations.Read, To = [RowAccessRoles.User])]
                public static partial class TheRule
                {
                    public static bool Allows(Seat seat, IRowUser user)
                        => seat.Id == user.CurrentSeatId() || seat.TenantId == user.CurrentTenant() || user.SeatsInMyUnits().Contains(seat.Id);
                }
                """,
                "TheRule.cs")
            .WithAssemblyName("Shop.Infrastructure")
            .WithTenancy()
            .WithModuleFromTheBuild("Tenants")
            .WithReferencedProject(
                "Shop.Domain",
                project => project.WithSource(Ids, "Ids.cs").WithSource(Classes, "Classes.cs").WithModuleFromTheBuild("Tenants"))
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        SqlOf(result).Should().Be(
            "((({col:Id} = (SELECT {fn:tenancy/current_seat}())) OR ({col:TenantId} = (SELECT {fn:tenancy/current_tenant}()))) OR ({col:Id} = ANY (ARRAY(SELECT {fn:tenancy/seats_in_my_units}()))))");
        result.ShouldContain("TenancyRowAccess.CurrentSeatId.Registration", "public static global::Shop.Contracts.SeatId CurrentSeatId(this global::DDDToolkit.Abstractions.Access.IRowUser user)");
    }

    [Fact]
    public void Every_project_of_a_module_keeps_its_own_wrappers_where_a_registration_leaves_them_to_the_lowest()
    {
        // The project above the one that holds the wrappers: it is the rule's project that needs them.
        var result = GeneratorTestHost.Create("namespace Shop.Api;\n\npublic static class Nothing;", "Nothing.cs")
            .WithAssemblyName("Shop.Api")
            .WithTenancy()
            .WithModuleFromTheBuild("Tenants")
            .WithReferencedProject("Shop.Domain", project => project.WithSource(Ids, "Ids.cs").WithSource(Classes, "Classes.cs").WithModuleFromTheBuild("Tenants"))
            .WithReferencedProject("Shop.Infrastructure", project => project.WithSource("namespace Shop.Infrastructure;\n\npublic static class AlsoNothing;", "AlsoNothing.cs").WithModuleFromTheBuild("Tenants"))
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveGenerated("TenancyRowAccess.CurrentSeatId.Registration");
    }

    [Fact]
    public void A_project_of_another_module_without_a_line_gets_none_and_writes_the_type_arguments()
    {
        var result = GeneratorTestHost.Create(Report, "Report.cs")
            .WithSource(Rule("report.WrittenBy == user.CurrentSeatId<SeatId>()"), "TheRule.cs")
            .WithTenancy()
            .WithModuleFromTheBuild("Reports")
            .WithReferencedProject("Shop.Contracts", project => project.WithSource(Ids, "Ids.cs").WithModuleFromTheBuild("Tenants"))
            .RunCore();

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TenancyRowAccess."));
        SqlOf(result).Should().Be("({col:WrittenBy} = (SELECT {fn:tenancy/current_seat}()))");
    }

    [Fact]
    public void A_project_that_declares_only_some_of_the_classes_hears_nothing_of_a_wrapper_it_cannot_have()
    {
        // The seat class and no tenant class: CurrentSeatId is closed, SeatInTenant, over the two, is not, and says nothing.
        var result = GeneratorTestHost.Create(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Tenancy;

                namespace Shop;

                [EntityId<Guid>]
                public readonly partial record struct SeatId;

                [SeatAggregate<SeatId>]
                public sealed partial class Seat;

                public static class Asking
                {
                    public static SeatId Ask(DDDToolkit.Abstractions.Access.IRowUser user) => DDDToolkit.Abstractions.Access.GeneratedTenancyRowAccess.CurrentSeatId(user);
                }
                """)
            .WithTenancy()
            .RunCore();

        result.ShouldHaveGenerated("TenancyRowAccess.CurrentSeatId.Registration");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TenancyRowAccess.SeatInTenant"));
        result.ReportedDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "DDD00049" && diagnostic.GetMessage().Contains("TenancyRowAccess"));
        result.ReportedDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "DDD00045" || diagnostic.Id == "DDD00050");
    }

    [Fact]
    public void A_shorthand_is_no_registration_so_what_a_class_of_the_package_needs_is_still_said()
    {
        // A project that sees Tenancy and no registration of Entity Framework: the classes the package asks for are missing
        // the others, and no registration says so in their place, whatever shorthands the project could call.
        var result = GeneratorTestHost.Create(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Tenancy;

                namespace Shop;

                [EntityId<Guid>]
                public readonly partial record struct TenantId;

                [TenantAggregate<TenantId>]
                public sealed partial class Tenant;
                """)
            .WithTenancy()
            .RunCore();

        result.Count("DDD00065").Should().BeGreaterThan(0, "the shorthands do not stand in for what the classes the package asks for say");
    }

    /// <summary>The SQL the generator wrote into the rule <c>TheRule</c>.</summary>
    private static string SqlOf(GeneratorRunOutcome result)
    {
        var literal = CSharpSyntaxTree.ParseText(result.Source("TheRule.RowAccess")).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }
}
