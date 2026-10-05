using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A column rule, a row access rule with <c>Columns</c>: translated as every rule is, and refused where it cannot
/// be one, with DDD00038 on the argument that names the columns.
/// </summary>
public class ColumnRuleGenerationTests
{
    private const string Shop =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct OrderId;

        [EntityId<Guid>]
        public readonly partial record struct LineId;

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

            public Guid? PlacedBy { get; private set; }

            public OrderStatus Status { get; private set; }

            public Money Total { get; private set; } = null!;

            public DateTimeOffset? Due { get; private set; }

            public string Note { get; private set; } = "";

            public partial IReadOnlyList<Line> Lines { get; }

            public IReadOnlyList<string> Tags { get; private set; } = [];

            public byte[] Stamp { get; private set; } = [];
        }

        [Entity<LineId>]
        public partial class Line
        {
            public Line(LineId id) : base(id) { }

            public int Quantity { get; private set; }
        }


        """;

    private static GeneratorRunOutcome Rule(string attribute, string body = "public static bool Allows(Order order, Caller caller) => order.PlacedBy == caller.UserId;")
        => GeneratorTestHost.Create(Shop +
            $$"""
            [{{attribute}}]
            public static partial class TheRule
            {
                {{body}}
            }
            """).RunCore();

    private static string SqlOf(GeneratorRunOutcome result)
    {
        var literal = CSharpSyntaxTree.ParseText(result.Source("TheRule.RowAccess")).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

    [Theory]
    [InlineData("Columns = [nameof(Order.Status)]")]
    [InlineData("Columns = [nameof(Order.Status), nameof(Order.Due)]")]
    [InlineData("Columns = [nameof(Order.Total)]")]
    [InlineData("Columns = [\"Total.Amount\"]")]
    [InlineData("To = [RowAccessRoles.User], Columns = [\"Note\"]")]
    [InlineData("Columns = [nameof(Order.Tags)]")]
    [InlineData("Columns = [nameof(Order.Stamp)]")]
    public void A_column_rule_is_translated_as_every_rule_is_and_says_it_is_one_first(string columns)
    {
        var result = Rule($"RowAccess<Order>(RowOperations.Change, {columns})");

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        SqlOf(result).Should().Be(
            "{columns}({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})",
            "the columns say where the rule holds, not what it asks; and an export that knows no column rules stops at the placeholder rather than write a policy for the whole row");
    }

    [Fact]
    public void A_rule_about_whole_rows_says_nothing_of_columns()
        => SqlOf(Rule("RowAccess<Order>(RowOperations.Change)")).Should().Be("({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})");

    [Theory]
    [InlineData("RowOperations.Read")]
    [InlineData("RowOperations.Create")]
    [InlineData("RowOperations.Remove")]
    [InlineData("RowOperations.Read | RowOperations.Change")]
    [InlineData("RowOperations.All")]
    public void Columns_with_an_operation_besides_Change_is_DDD00038_on_the_columns(string operations)
    {
        var result = Rule($"RowAccess<Order>({operations}, Columns = [nameof(Order.Status)])");

        result.ShouldHaveDiagnostic("DDD00038", "Columns = [nameof(Order.Status)]")
            .GetMessage().Should().Contain("RowOperations.Change and no other operation with Columns");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"), "a rule without SQL never reaches the database");
    }

    [Theory]
    [InlineData("\"Stat\"", "'Stat' is none, since Order has no property 'Stat'")]
    [InlineData("\"Total.Amonut\"", "'Total.Amonut' is none, since Money has no property 'Amonut'")]
    [InlineData("\"Status.Value\"", "'Status.Value' is none, since OrderStatus has no property 'Value'")]
    [InlineData("\"\"", "an empty name is none")]
    [InlineData("nameof(Order.Lines)", "'Lines' is a collection of entities, whose rows are in a table of their own")]
    [InlineData("\"Tags.Count\"", "'Tags.Count' goes into Tags, a collection, which is stored whole")]
    public void A_column_that_is_no_property_stored_in_the_row_is_DDD00038_on_the_columns(string column, string says)
    {
        var result = Rule($"RowAccess<Order>(RowOperations.Change, Columns = [nameof(Order.Status), {column}])");

        result.ShouldHaveDiagnostic("DDD00038", $"Columns = [nameof(Order.Status), {column}]").GetMessage().Should().Contain(says);
        result.Count("DDD00038").Should().Be(1, "the property that is there is no mistake");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
    }

    [Fact]
    public void What_DDD00038_shows_a_column_rule_is_written_with_the_aggregates_own_properties()
    {
        var result = Rule("RowAccess<Order>(RowOperations.Change, Columns = [\"Stat\"])");

        result.ShouldHaveDiagnostic("DDD00038", "Columns = [\"Stat\"]").GetMessage().Should()
            .Contain("such as nameof(Order.PlacedBy), or a property of a value object it holds, written with a dot, such as \"Total.Amount\"", "an example names what the developer's own aggregate has");
    }

    [Fact]
    public void A_function_a_column_rule_calls_by_name_carries_its_schema_since_its_trigger_has_an_empty_search_path()
    {
        const string calls = "public static bool Allows(Order order, Caller caller) => Sql.Call<bool>(\"{0}\", caller.UserId);";

        var unqualified = Rule("RowAccess<Order>(RowOperations.Change, Columns = [nameof(Order.Status)])", string.Format(calls, "is_agent"));
        var qualified = Rule("RowAccess<Order>(RowOperations.Change, Columns = [nameof(Order.Status)])", string.Format(calls, "public.is_agent"));
        var policy = Rule("RowAccess<Order>(RowOperations.Change)", string.Format(calls, "is_agent"));

        unqualified.ShouldHaveDiagnostic("DDD00038", "\"is_agent\"").GetMessage().Should()
            .Contain("a Sql.Call that names its function with its schema, such as \"public.is_agent\", or \"pg_catalog.is_agent\" for one of Postgres's own");
        unqualified.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
        SqlOf(qualified.ShouldNotHaveDiagnostic("DDD00038")).Should().Be("{columns}public.is_agent({caller:uid})");
        policy.ShouldNotHaveDiagnostic("DDD00038").ShouldHaveGenerated("TheRule.RowAccess");
    }

    [Fact]
    public void An_empty_list_of_columns_is_a_rule_about_whole_rows()
    {
        var result = Rule("RowAccess<Order>(RowOperations.Read, Columns = [])");

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldCompile().ShouldHaveGenerated("TheRule.RowAccess");
    }

    [Fact]
    public void A_column_rule_says_what_it_cannot_translate_as_every_rule_does()
    {
        var result = Rule(
            "RowAccess<Order>(RowOperations.Change, Columns = [nameof(Order.Status)])",
            "public static bool Allows(Order order, Caller caller) => order.Note.StartsWith(\"x\");");

        result.Count("DDD00039").Should().Be(1);
        result.ShouldNotHaveDiagnostic("DDD00038");
    }
}
