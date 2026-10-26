using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A rule that narrows, <c>Restrictive = true</c>, and a question that answers with a row, <c>[AccessRow]</c>, as the
/// generator writes them: the restrictive rule's SQL starts with <c>{restrictive}</c>, so an export that does not know
/// it stops rather than write it as one that allows; a property of a row question's answer is a column of the row's
/// table, which the export finds in the model, asked once per statement.
/// </summary>
public class RestrictiveRuleGenerationTests
{
    private const string Shop =
        """
        using System;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Shop")]

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct OrderId;

        [AggregateRoot<OrderId>]
        public partial class Order
        {
            public Order(OrderId id) : base(id) { }

            public string? Team { get; private set; }
        }

        public enum PersonKind
        {
            Visitor,
            Staff,
        }

        [EntityId<Guid>]
        public readonly partial record struct PersonId;

        [AggregateRoot<PersonId>]
        public partial class Person
        {
            public Person(PersonId id) : base(id) { }

            public PersonKind Kind { get; private set; }

            public string? Team { get; private set; }
        }

        [AccessFunctions]
        public static partial class ShopQuestions
        {
            [AccessRow("caller_person")]
            public static partial TPerson CallerPerson<TPerson>() where TPerson : class;
        }


        """;

    private static GeneratorRunOutcome Run(string source) => GeneratorTestHost.Create(Shop + source).RunCore();

    private static string SqlIn(GeneratorRunOutcome result, string hint)
    {
        var literal = CSharpSyntaxTree.ParseText(result.Source(hint)).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

    private static string RuleSqlOf(string expression, string attribute = "[RowAccess<Order>(RowOperations.Read, Restrictive = true)]")
    {
        var result = Run(
            $$"""
            {{attribute}}
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => {{expression}};
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        return SqlIn(result, "TheRule.RowAccess");
    }

    [Fact]
    public void A_restrictive_rule_says_so_first_and_is_translated_as_any_rule()
        => RuleSqlOf("order.Team != null")
            .Should().Be("{restrictive}({col:Team} IS NOT NULL)");

    [Fact]
    public void A_rule_that_allows_says_nothing_of_the_kind()
        => RuleSqlOf("order.Team != null", "[RowAccess<Order>(RowOperations.Read)]")
            .Should().Be("({col:Team} IS NOT NULL)");

    [Fact]
    public void Restrictive_with_columns_is_DDD00038()
    {
        var result = Run(
            """
            [RowAccess<Order>(RowOperations.Change, Columns = [nameof(Order.Team)], Restrictive = true)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => order.Team != null;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00038", at: "TheRule").GetMessage().Should().Contain("Restrictive or Columns, not both");
    }

    [Fact]
    public void A_property_of_a_row_question_is_a_column_of_its_row_asked_once_per_statement()
        => RuleSqlOf("ShopQuestions.CallerPerson<Person>().Kind == PersonKind.Staff")
            .Should().Be("{restrictive}({row:shop/caller_person:0:Shop.Person:q1}{col:q1:Kind}{/row} = {rowval:Shop.Person:Kind:1})");

    [Fact]
    public void An_enum_constant_on_the_left_is_written_as_the_rows_column_stores_it_too()
        => RuleSqlOf("PersonKind.Staff != ShopQuestions.CallerPerson<Person>().Kind")
            .Should().Be("{restrictive}({rowval:Shop.Person:Kind:1} <> {row:shop/caller_person:0:Shop.Person:q1}{col:q1:Kind}{/row})");

    [Fact]
    public void Each_property_read_is_an_answer_of_its_own_and_a_comparison_with_the_rule_row_is_sqls_own()
        => RuleSqlOf("ShopQuestions.CallerPerson<Person>().Team == order.Team && ShopQuestions.CallerPerson<Person>().Kind == PersonKind.Staff")
            .Should().Be(
                "{restrictive}(({row:shop/caller_person:0:Shop.Person:q1}{col:q1:Team}{/row} = {col:Team}) AND ({row:shop/caller_person:0:Shop.Person:q2}{col:q2:Kind}{/row} = {rowval:Shop.Person:Kind:1}))");

    [Fact]
    public void A_row_question_gets_a_body_that_throws()
    {
        var result = Run("");

        result.ShouldCompile();
        result.ShouldContain("ShopQuestions.AccessFunctions", "public static partial TPerson CallerPerson<TPerson>() where TPerson : class => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"shop/caller_person\");");
    }

    [Theory]
    [InlineData("public static partial int CallerNumber();", "to return the class a table is mapped to")]
    [InlineData("public static partial Person CallerAt(string place);", "no parameters")]
    public void A_row_question_of_another_shape_is_DDD00038(string declared, string expected)
    {
        var result = GeneratorTestHost.Create(Shop.Replace("public static partial TPerson CallerPerson<TPerson>() where TPerson : class;", declared, StringComparison.Ordinal)).RunCore();

        result.ShouldHaveDiagnostic("DDD00038", at: declared.Contains("CallerNumber", StringComparison.Ordinal) ? "CallerNumber" : "CallerAt")
            .GetMessage().Should().Contain(expected);
    }
}
