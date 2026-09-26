using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A row access rule's <c>Allows</c> method translated into the SQL template the Supabase export fills in:
/// each scenario the example shop and the tests use, and what cannot be translated.
/// </summary>
public class RowAccessGenerationTests
{
    private const string Shop =
        """
        using System;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct OrderId;

        [EntityId<Guid>]
        public readonly partial record struct CustomerId;

        public enum OrderStatus { Placed, Confirmed, Cancelled }

        [ValueObject]
        public partial record Money
        {
            public decimal Amount { get; protected init; }

            public string Currency { get; protected init; } = "EUR";
        }

        [AggregateRoot<OrderId>]
        public partial class Order
        {
            public Order(OrderId id) : base(id) { }

            public CustomerId? PlacedBy { get; private set; }

            public OrderStatus Status { get; private set; }

            public Money Total { get; private set; } = null!;

            public bool IsPublic { get; private set; }

            public string? Team { get; private set; }

            public int Level { get; private set; }
        }


        """;

    private static GeneratorRunOutcome Rule(string body, string operations = "RowOperations.Read", string declaration = "public static partial class")
        => GeneratorTestHost.Create(Shop +
            $$"""
            [RowAccess<Order>({{operations}})]
            {{declaration}} TheRule
            {
                {{body}}
            }
            """).RunCore();

    /// <summary>The SQL the generator wrote into the rule, as the string the export reads.</summary>
    private static string SqlOf(string expression)
    {
        var result = Rule($"public static bool Allows(Order order, Caller caller) => {expression};");
        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00040").ShouldCompile();

        var literal = CSharpSyntaxTree.ParseText(result.Source("TheRule.RowAccess")).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

    [Fact]
    public void A_customer_and_a_guest_is_ownership_with_csharps_nulls()
        => SqlOf("order.PlacedBy == null || order.PlacedBy?.Value == caller.UserId")
            .Should().Be("(({col:PlacedBy} IS NULL) OR ({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid}))");

    [Fact]
    public void Roles_and_departments_are_claims_and_an_amount_is_a_column_of_a_value_object()
        => SqlOf("caller.Claim(\"app_metadata.role\") == \"staff\" && (order.Total.Amount <= 500 || caller.Claim(\"app_metadata.department\") == \"finance\")")
            .Should().Be("(({caller:claim:app_metadata.role} IS NOT DISTINCT FROM 'staff') AND (coalesce({col:Total.Amount} <= 500, FALSE) OR ({caller:claim:app_metadata.department} IS NOT DISTINCT FROM 'finance')))");

    [Fact]
    public void An_enum_constant_is_left_for_the_column_to_say_how_it_is_stored()
        => SqlOf("order.Status != OrderStatus.Cancelled")
            .Should().Be("({col:Status} IS DISTINCT FROM {val:Status:2})");

    [Fact]
    public void Everybody_is_true()
        => SqlOf("true").Should().Be("TRUE");

    [Fact]
    public void A_flag_the_caller_and_a_negation()
        => SqlOf("caller.IsSignedIn && !order.IsPublic && caller.Role == \"authenticated\"")
            .Should().Be("(({caller:signedin} AND (NOT {col:IsPublic})) AND ({caller:role} IS NOT DISTINCT FROM 'authenticated'))");

    [Fact]
    public void A_team_from_the_token_against_a_column()
        => SqlOf("order.Team != null && order.Team == caller.Claim(\"app_metadata.team\") && order.Level < 3")
            .Should().Be("((({col:Team} IS NOT NULL) AND ({col:Team} IS NOT DISTINCT FROM {caller:claim:app_metadata.team})) AND coalesce({col:Level} < 3, FALSE))");

    [Fact]
    public void Braces_in_a_constant_are_doubled_so_they_are_not_filled_in()
        => SqlOf("order.Team == \"{north}\"")
            .Should().Be("({col:Team} IS NOT DISTINCT FROM '{{north}}')");

    [Fact]
    public void A_sql_function_is_called_with_its_arguments_translated_like_the_rest()
        => SqlOf("order.PlacedBy?.Value == caller.UserId || Sql.Call<bool>(\"private.is_support_agent\", caller.UserId, order.Level)")
            .Should().Be("(({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid}) OR private.is_support_agent({caller:uid}, {col:Level}))");

    [Fact]
    public void Raw_sql_goes_in_as_it_is_in_parentheses_with_its_braces_kept()
        => SqlOf("order.PlacedBy == null || Sql.Raw<bool>(\"\\\"Id\\\" IN (SELECT order_id FROM support.escalations WHERE note <> '{}')\")")
            .Should().Be("(({col:PlacedBy} IS NULL) OR (\"Id\" IN (SELECT order_id FROM support.escalations WHERE note <> '{{}}')))");

    [Theory]
    [InlineData("Sql.Call<bool>(\"private.check; DROP TABLE x\")")]
    [InlineData("Sql.Raw<bool>(order.Team!)")]
    public void Sql_that_is_not_a_function_name_or_not_written_in_the_rule_is_refused(string expression)
        => Rule($"public static bool Allows(Order order, Caller caller) => {expression};").Count("DDD00039").Should().Be(1);

    [Fact]
    public void A_body_that_returns_is_the_same_as_an_arrow()
    {
        var result = Rule("public static bool Allows(Order order, Caller caller) { return order.IsPublic; }");

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldContain("TheRule.RowAccess", "{col:IsPublic}");
    }

    // ------------------------------------------------------------------ DDD00039

    [Theory]
    [InlineData("order.Team!.StartsWith(\"n\")", "order.Team!.StartsWith(\"n\")")]
    [InlineData("DateTime.UtcNow.Year > 2000", "DateTime.UtcNow.Year")]
    [InlineData("caller.Claim(\"app_metadata.role\").Length > 0", "caller.Claim(\"app_metadata.role\").Length")]
    public void What_the_database_cannot_check_is_an_error_on_that_expression_and_nothing_is_generated(string expression, string reported)
    {
        var result = Rule($"public static bool Allows(Order order, Caller caller) => {expression};");

        result.Count("DDD00039").Should().Be(1);
        result.ReportedDiagnostics.Single(diagnostic => diagnostic.Id == "DDD00039").GetMessage().Should().Contain(reported);
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
    }

    // ------------------------------------------------------------------ DDD00038

    [Theory]
    [InlineData("public static bool Allows(Order order, Caller caller) => true;", "public static class")]
    [InlineData("public static bool Allows(Order order) => true;", "public static partial class")]
    [InlineData("public static bool Allows(Order order, Caller caller) { var yes = true; return yes; }", "public static partial class")]
    [InlineData("public static int Allows(Order order, Caller caller) => 1;", "public static partial class")]
    [InlineData("public static bool Check(Order order, Caller caller) => true;", "public static partial class")]
    public void A_rule_of_another_shape_is_an_error(string body, string declaration)
    {
        var result = Rule(body, declaration: declaration);

        result.Count("DDD00038").Should().BeGreaterThan(0);
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
    }

    // ------------------------------------------------------------------ DDD00040

    [Fact]
    public void A_rule_on_an_entity_that_is_not_a_root_is_an_error()
    {
        var result = GeneratorTestHost.Create(Shop +
            """
            [EntityId<Guid>]
            public readonly partial record struct LineId;

            [Entity<LineId>]
            public partial class Line
            {
                public Line(LineId id) : base(id) { }

                public bool IsPublic { get; private set; }
            }

            [RowAccess<Line>(RowOperations.Read)]
            public static partial class LinesAreEveryones
            {
                public static bool Allows(Line line, Caller caller) => line.IsPublic;
            }
            """).RunCore();

        result.Count("DDD00040").Should().Be(1);
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("LinesAreEveryones.RowAccess"));
    }
}
