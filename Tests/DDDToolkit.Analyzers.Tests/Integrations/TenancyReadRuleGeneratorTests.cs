using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// A read rule of the application's on its own seat or invitation class, which takes the place of Tenancy's default
/// read of that table, written with the questions Tenancy offers as building blocks. Each building block becomes the
/// SQL a policy asks once per statement, never once per row: the caller's tenant and seat as a scalar question, and
/// where it holds a key or which people share its units as a set. The package is the real one, seen through
/// metadata the way an application sees it.
/// </summary>
public class TenancyReadRuleGeneratorTests
{
    /// <summary>The application's invitation, beside the organization's classes: the other class whose read is a default.</summary>
    private const string Invitations =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Supporting.Tenancy;

        namespace Campus.Tenants;

        [EntityId<Guid>]
        public readonly partial record struct InvitationId;

        [InvitationAggregate<InvitationId>]
        public sealed partial class Invitation;
        """;

    [Theory]
    [InlineData("seat.TenantId == TenancyRowAccess.CallerTenant<TenantId>()", "({col:TenantId} = (SELECT {fn:tenancy/caller_tenant}()))")]
    [InlineData("seat.Id == TenancyRowAccess.CallerSeat<SeatId>()", "({col:Id} = (SELECT {fn:tenancy/caller_seat}()))")]
    [InlineData("TenancyRowAccess.SeatsInMyUnits<SeatId>().Contains(seat.Id)", "({col:Id} = ANY (ARRAY(SELECT {fn:tenancy/seats_in_my_units}())))")]
    [InlineData("TenancyRowAccess.HoldsKey(TenancyKeys.SeatsManage)", "(SELECT {fn:tenancy/holds_key}('tenancy.seats.manage'))")]
    [InlineData("TenancyRowAccess.HoldsTenantWide(TenancyKeys.RolesManage)", "(SELECT {fn:tenancy/holds_tenant_wide}('tenancy.roles.manage'))")]
    [InlineData("seat.Identity == caller.UserId", "({col:Identity} IS NOT DISTINCT FROM {caller:uid})")]
    public void A_building_block_in_a_read_rule_on_the_seat_class_is_asked_once_per_statement(string expression, string sql)
        => RuleSqlOf("Seat", "seat", expression).Should().Be(sql);

    [Fact]
    public void Where_the_caller_holds_a_key_is_a_set_an_invitation_rule_asks_by_the_invitations_unit()
        => RuleSqlOf("Invitation", "invitation", "TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(TenancyKeys.GrantsManage).Contains(invitation.UnitId)")
            .Should().Be("({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/units_where_i_hold}('tenancy.grants.manage'))))");

    [Fact]
    public void The_building_blocks_combine_as_any_conditions_do()
        => RuleSqlOf("Seat", "seat", "seat.Id == TenancyRowAccess.CallerSeat<SeatId>() || TenancyRowAccess.SeatsInMyUnits<SeatId>().Contains(seat.Id)")
            .Should().Be("(({col:Id} = (SELECT {fn:tenancy/caller_seat}())) OR ({col:Id} = ANY (ARRAY(SELECT {fn:tenancy/seats_in_my_units}()))))");

    /// <summary>
    /// The SQL the generator writes for a read rule on <paramref name="aggregate"/>, the application's class, whose
    /// <c>Allows</c> is <paramref name="expression"/> over <paramref name="parameter"/>; the project compiles.
    /// </summary>
    private static string RuleSqlOf(string aggregate, string parameter, string expression)
    {
        var result = GeneratorTestHost.Create(MembershipWithTenancyGeneratorTests.TenancyIds, "TenancyIds.cs")
            .WithSource(MembershipWithTenancyGeneratorTests.TenancyClasses, "Tenancy.cs")
            .WithSource(Invitations, "Invitations.cs")
            .WithSource(
                $$"""
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Supporting.Tenancy;
                using DDDToolkit.Supporting.Tenancy.Access;
                using DDDToolkit.Supporting.Tenancy.Catalogue;

                namespace Campus.Tenants;

                [RowAccess<{{aggregate}}>(RowOperations.Read, To = [RowAccessRoles.User])]
                public static partial class TheRule
                {
                    public static bool Allows({{aggregate}} {{parameter}}, Caller caller) => {{expression}};
                }
                """,
                "TheRule.cs")
            .WithTenancy()
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00051").ShouldCompile();
        var literal = CSharpSyntaxTree.ParseText(result.Source("TheRule.RowAccess")).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }
}
