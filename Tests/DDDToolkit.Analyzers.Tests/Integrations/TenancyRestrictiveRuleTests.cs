using DDDToolkit.Supporting.Tenancy.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// A restrictive rule of the application's on Tenancy's tables, reading a field of the caller's own seat: what the
/// generator writes for it, where it may stand (DDD00100), and which class the caller's seat is asked as (DDD00101).
/// The package is the real one, seen through metadata the way an application sees it.
/// </summary>
public class TenancyRestrictiveRuleTests
{
    /// <summary>The application's own field on its seat class: whether the seat is an employee's.</summary>
    private const string SeatKind =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Tenancy;

        namespace Campus.Tenants;

        public enum SeatKind
        {
            Employee,
            External,
        }

        public sealed partial class Seat
        {
            public SeatKind Kind { get; private set; }
        }

        [EntityId<System.Guid>]
        public readonly partial record struct InvitationId;

        [InvitationAggregate<InvitationId>]
        public sealed partial class Invitation;
        """;

    private static GeneratorRunOutcome Run(string rule)
        => GeneratorTestHost.Create(MembershipWithTenancyGeneratorTests.TenancyIds, "TenancyIds.cs")
            .WithSource(MembershipWithTenancyGeneratorTests.TenancyClasses, "Tenancy.cs")
            .WithSource(SeatKind, "SeatKind.cs")
            .WithSource(
                $$"""
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Tenancy;
                using DDDToolkit.Supporting.Tenancy.Access;

                namespace Campus.Tenants;

                {{rule}}
                """,
                "TheRule.cs")
            .WithTenancy()
            .WithAnalyzers(new TenancyRowAccessAnalyzer())
            .RunCore();

    private static string Rule(string aggregate, string expression, string operations = "RowOperations.Read")
        => $$"""
             [RowAccess<{{aggregate}}>({{operations}}, Restrictive = true)]
             public static partial class TheRule
             {
                 public static bool Allows({{aggregate}} row, IRowUser user) => {{expression}};
             }
             """;

    private static string SqlIn(GeneratorRunOutcome result)
    {
        var literal = CSharpSyntaxTree.ParseText(result.Source("TheRule.RowAccess")).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

    [Fact]
    public void Only_employees_read_the_organization_is_a_field_of_the_callers_own_seat_asked_once_per_statement()
    {
        var result = Run(Rule("Organization", "user.CurrentSeat<Seat>().Kind == SeatKind.Employee"));

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00100").ShouldNotHaveDiagnostic("DDD00101").ShouldCompile();
        SqlIn(result).Should().Be("{restrictive}({row:tenancy/current_seat_row:0:Campus.Tenants.Seat:q1}{col:q1:Kind}{/row} = {rowval:Campus.Tenants.Seat:Kind:0})");
    }

    [Theory]
    [InlineData("Organization", "RowOperations.Read")]
    [InlineData("Seat", "RowOperations.Read | RowOperations.Change")]
    [InlineData("Role", "RowOperations.All")]
    public void A_restrictive_rule_on_the_organization_the_seats_or_the_roles_is_no_diagnostic(string aggregate, string operations)
        => Run(Rule(aggregate, "user.CurrentSeat<Seat>().Kind != SeatKind.External", operations))
            .ShouldNotHaveDiagnostic("DDD00100").ShouldNotHaveDiagnostic("DDD00101").ShouldCompile();

    [Theory]
    [InlineData("Tenant", "a tenant")]
    [InlineData("Invitation", "an invitation")]
    public void A_restrictive_rule_on_a_tenant_or_an_invitation_is_DDD00100(string aggregate, string described)
    {
        var reported = Run(Rule(aggregate, "user.HoldsPermission(\"tenancy.seats.manage\")")).ShouldHaveDiagnostic("DDD00100", at: "RowAccess<" + aggregate + ">(RowOperations.Read, Restrictive = true)");

        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Contain("'TheRule' is a restrictive rule on '" + aggregate + "', " + described + " of Tenancy's");
    }

    [Fact]
    public void A_rule_that_allows_on_a_tenant_is_no_restrictive_rule_and_no_DDD00100()
        => Run(Rule("Tenant", "user.HoldsPermission(\"tenancy.seats.manage\")").Replace(", Restrictive = true", "", StringComparison.Ordinal))
            .ShouldNotHaveDiagnostic("DDD00100");

    [Fact]
    public void The_callers_seat_asked_as_another_class_is_DDD00101()
    {
        var reported = Run(Rule("Organization", "user.CurrentSeat<Role>().Name == \"Employee\"")).ShouldHaveDiagnostic("DDD00101", at: "user.CurrentSeat<Role>()");

        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Contain("CurrentSeat<Role>() asks for a seat of 'Role', which is not the application's seat class");
    }
}
