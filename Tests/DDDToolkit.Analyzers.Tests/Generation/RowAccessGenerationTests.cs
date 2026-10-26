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
        using System.Linq;
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

            public DateTimeOffset? Due { get; private set; }

            public DateTime Placed { get; private set; }

            public partial System.Collections.Generic.IReadOnlyList<Member> Members { get; }
        }

        [EntityId<Guid>]
        public readonly partial record struct MemberId;

        [Entity<MemberId>]
        public partial class Member
        {
            public Member(MemberId id) : base(id) { }

            public Guid UserId { get; private set; }

            public bool IsAdmin { get; private set; }
        }


        """;

    private const string Membership =
        """
        [AccessFunction<Order>("shop.is_member")]
        public static partial class Membership
        {
            public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.UserId == caller.UserId);
        }


        """;

    private static GeneratorRunOutcome Function(string body, string name = "shop.is_admin")
        => GeneratorTestHost.Create(Shop +
            $$"""
            [AccessFunction<Order>("{{name}}")]
            public static partial class TheFunction
            {
                {{body}}
            }
            """).RunCore();

    /// <summary>The SQL the generator wrote into an access function.</summary>
    private static string FunctionSqlOf(string expression)
    {
        var result = Function($"public static bool Allows(Order order, Caller caller) => {expression};");
        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00041").ShouldCompile();
        return ConstantIn(result.Source("TheFunction.AccessFunction"));
    }

    private static string ConstantIn(string source)
    {
        var literal = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

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
            .Should().Be("({col:Status} <> {val:Status:2})");

    [Fact]
    public void Equality_of_non_nullable_values_is_equals_and_of_nullable_ones_is_not_distinct()
    {
        SqlOf("order.Status == OrderStatus.Placed && order.IsPublic == true && order.Level != 3")
            .Should().Be("((({col:Status} = {val:Status:0}) AND ({col:IsPublic} = TRUE)) AND ({col:Level} <> 3))", "none of them can be null in C#, so SQL's = means ==, and an index can answer it");
        SqlOf("order.PlacedBy?.Value == caller.UserId")
            .Should().Be("({col:PlacedBy} IS NOT DISTINCT FROM {caller:uid})", "a customer id that may be null equals a caller without one in C#");
        SqlOf("order.Team == \"north\"")
            .Should().Be("({col:Team} IS NOT DISTINCT FROM 'north')", "a string may be null");
        SqlOf("order.Id == order.Id")
            .Should().Be("({col:Id} = {col:Id})", "an id that is a struct is never null");
    }

    [Fact]
    public void UtcNow_is_now()
    {
        SqlOf("order.Due > DateTimeOffset.UtcNow").Should().Be("coalesce({col:Due} > now(), FALSE)");
        SqlOf("order.Due == null || DateTime.UtcNow < order.Placed").Should().Be("(({col:Due} IS NULL) OR coalesce(now() < {col:Placed}, FALSE))");
    }

    [Fact]
    public void Comparing_with_a_scalar_question_is_false_when_it_answers_null()
    {
        const string Questions =
            """
            [AccessFunctions(Owner = "shop")]
            public static partial class ShopQuestions
            {
                [AccessScalar("caller_customer")]
                public static partial CustomerId CallerCustomer();

                [AccessScalar("caller_level")]
                public static partial int CallerLevel();
            }


            """;

        string Of(string expression)
        {
            var result = GeneratorTestHost.Create(Shop + Questions +
                $$"""
                [RowAccess<Order>(RowOperations.Read)]
                public static partial class TheRule
                {
                    public static bool Allows(Order order, Caller caller) => {{expression}};
                }
                """).RunCore();

            result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
            return ConstantIn(result.Source("TheRule.RowAccess"));
        }

        Of("order.PlacedBy == ShopQuestions.CallerCustomer()")
            .Should().Be("({col:PlacedBy} = (SELECT {fn:shop/caller_customer}()))", "a caller the question does not know answers null, and a null customer must not match it: SQL's = is null then, which a policy counts as no");
        Of("order.PlacedBy != ShopQuestions.CallerCustomer()")
            .Should().Be("({col:PlacedBy} <> (SELECT {fn:shop/caller_customer}()))", "IS DISTINCT FROM a null would pass every order that has a customer");
        Of("ShopQuestions.CallerLevel() == order.Level")
            .Should().Be("((SELECT {fn:shop/caller_level}()) = {col:Level})", "the question need not be on the right");
    }

    [Fact]
    public void A_negated_comparison_with_a_scalar_question_stays_unknown_for_a_caller_it_does_not_know()
    {
        const string Questions =
            """
            [AccessFunctions(Owner = "shop")]
            public static partial class ShopQuestions
            {
                [AccessScalar("caller_customer")]
                public static partial CustomerId CallerCustomer();

                [AccessScalar("caller_level")]
                public static partial int CallerLevel();
            }


            """;

        string Of(string expression)
        {
            var result = GeneratorTestHost.Create(Shop + Questions +
                $$"""
                [RowAccess<Order>(RowOperations.Read)]
                public static partial class TheRule
                {
                    public static bool Allows(Order order, Caller caller) => {{expression}};
                }
                """).RunCore();

            result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
            return ConstantIn(result.Source("TheRule.RowAccess"));
        }

        // coalesce(..., FALSE) would be FALSE for such a caller, and NOT would make that a yes: SQL's own
        // comparison is null, and NOT null is null, which a policy counts as no.
        Of("!(order.PlacedBy == ShopQuestions.CallerCustomer())")
            .Should().Be("(NOT ({col:PlacedBy} = (SELECT {fn:shop/caller_customer}())))");
        Of("!(ShopQuestions.CallerLevel() < order.Level)")
            .Should().Be("(NOT ((SELECT {fn:shop/caller_level}()) < {col:Level}))");
        Of("(order.PlacedBy == ShopQuestions.CallerCustomer()) == false")
            .Should().Be("(({col:PlacedBy} = (SELECT {fn:shop/caller_customer}())) = FALSE)", "a comparison of the comparison stays SQL's own too");
        Of("!(order.PlacedBy == ShopQuestions.CallerCustomer() && order.IsPublic)")
            .Should().Be("(NOT (({col:PlacedBy} = (SELECT {fn:shop/caller_customer}())) AND {col:IsPublic}))");
    }

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

    // ------------------------------------------------------------------ access functions

    [Fact]
    public void An_access_function_asks_the_aggregates_entities_with_an_exists()
        => FunctionSqlOf("order.Members.Any(member => member.UserId == caller.UserId && member.IsAdmin)")
            .Should().Be("{exists:Members:e1}(({col:e1:UserId} IS NOT DISTINCT FROM {caller:uid}) AND {col:e1:IsAdmin}){/exists}");

    [Fact]
    public void Any_without_a_condition_is_whether_there_is_one()
        => FunctionSqlOf("order.Members.Any()").Should().Be("{exists:Members:e1}TRUE{/exists}");

    // ------------------------------------------------------------------ entities of an entity

    /// <summary>An order whose members each hold duties: entities of an entity, two collections from the root.</summary>
    private const string EntitiesOfEntities =
        """
        using System;
        using System.Linq;
        using DDDToolkit.Abstractions.Access;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop;

        [EntityId<Guid>]
        public readonly partial record struct OrderId;

        [AggregateRoot<OrderId>]
        public partial class Order
        {
            public Order(OrderId id) : base(id) { }

            public bool IsPublic { get; private set; }

            public partial System.Collections.Generic.IReadOnlyList<Member> Members { get; }

            public partial System.Collections.Generic.IReadOnlyList<Note> Notes { get; }
        }

        [EntityId<Guid>]
        public readonly partial record struct MemberId;

        [Entity<MemberId>]
        public partial class Member
        {
            public Member(MemberId id) : base(id) { }

            public Guid UserId { get; private set; }

            public DateTimeOffset? EndsAt { get; private set; }

            public partial System.Collections.Generic.IReadOnlyList<Duty> Duties { get; }
        }

        [EntityId<Guid>]
        public readonly partial record struct DutyId;

        [Entity<DutyId>]
        public partial class Duty
        {
            public Duty(DutyId id) : base(id) { }

            public string Kind { get; private set; } = "";

            public DateTimeOffset? EndsAt { get; private set; }

            public partial System.Collections.Generic.IReadOnlyList<Shift> Shifts { get; }
        }

        [EntityId<Guid>]
        public readonly partial record struct ShiftId;

        [Entity<ShiftId>]
        public partial class Shift
        {
            public Shift(ShiftId id) : base(id) { }

            public int Day { get; private set; }
        }

        [EntityId<Guid>]
        public readonly partial record struct NoteId;

        [Entity<NoteId>]
        public partial class Note
        {
            public Note(NoteId id) : base(id) { }

            public bool Pinned { get; private set; }
        }


        """;

    /// <summary>The SQL the generator wrote into an access function over <see cref="EntitiesOfEntities"/>, which takes a kind after the caller.</summary>
    private static GeneratorRunOutcome EntitiesOfEntitiesFunction(string expression)
        => GeneratorTestHost.Create(EntitiesOfEntities +
            $$"""
            [AccessFunction<Order>("shop.orders_on_duty", Shape = AccessFunctionShape.Set)]
            public static partial class TheFunction
            {
                public static bool Allows(Order order, Caller caller, string kind) => {{expression}};
            }
            """).RunCore();

    private static string EntitiesOfEntitiesSqlOf(string expression)
    {
        var result = EntitiesOfEntitiesFunction(expression);
        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00041").ShouldCompile();
        return ConstantIn(result.Source("TheFunction.AccessFunction"));
    }

    [Fact]
    public void An_entity_of_an_entity_is_asked_with_an_exists_inside_the_exists()
        => EntitiesOfEntitiesSqlOf(
                "order.Members.Any(member => member.UserId == caller.UserId"
                + " && (member.EndsAt == null || member.EndsAt > DateTimeOffset.UtcNow)"
                + " && member.Duties.Any(duty => duty.Kind == kind && (duty.EndsAt == null || duty.EndsAt > DateTimeOffset.UtcNow)))")
            .Should().Be(
                "{exists:Members:e1}((({col:e1:UserId} IS NOT DISTINCT FROM {caller:uid})"
                + " AND (({col:e1:EndsAt} IS NULL) OR coalesce({col:e1:EndsAt} > now(), FALSE)))"
                + " AND {exists:e1:Duties:e2}(({col:e2:Kind} IS NOT DISTINCT FROM {arg:1})"
                + " AND (({col:e2:EndsAt} IS NULL) OR coalesce({col:e2:EndsAt} > now(), FALSE))){/exists}){/exists}",
                "the inner exists names the entity whose collection it asks, e1, and its own entities are e2");

    [Fact]
    public void Each_exists_inside_another_gets_the_next_alias()
        => EntitiesOfEntitiesSqlOf("order.Members.Any(member => member.Duties.Any(duty => duty.Shifts.Any(shift => shift.Day == 1) && duty.Shifts.Any()))")
            .Should().Be(
                "{exists:Members:e1}{exists:e1:Duties:e2}({exists:e2:Shifts:e3}({col:e3:Day} = 1){/exists}"
                + " AND {exists:e2:Shifts:e3}TRUE{/exists}){/exists}{/exists}",
                "an alias is the depth of its exists, so two side by side share one and one inside another never does");

    [Fact]
    public void An_inner_exists_reads_the_entity_it_is_inside_and_the_one_above()
        => EntitiesOfEntitiesSqlOf("order.Members.Any(member => member.Duties.Any(duty => duty.EndsAt == member.EndsAt))")
            .Should().Be("{exists:Members:e1}{exists:e1:Duties:e2}({col:e2:EndsAt} IS NOT DISTINCT FROM {col:e1:EndsAt}){/exists}{/exists}");

    [Theory]
    [InlineData("order.Members.Any(member => order.Notes.Any(note => note.Pinned))", "order.Notes.Any(note => note.Pinned)")]
    [InlineData("order.Members.Any(member => order.Members.Any())", "order.Members.Any()")]
    public void An_exists_over_the_aggregates_own_entities_inside_another_is_untranslatable(string expression, string reported)
    {
        var result = EntitiesOfEntitiesFunction(expression);

        result.Count("DDD00039").Should().Be(1);
        result.ReportedDiagnostics.Single(diagnostic => diagnostic.Id == "DDD00039").GetMessage().Should().Contain(reported);
    }

    [Fact]
    public void A_rule_that_reads_the_entities_of_an_entity_itself_is_told_to_ask_an_access_function()
    {
        var result = GeneratorTestHost.Create(EntitiesOfEntities +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.Duties.Any());
            }
            """).RunCore();

        result.Count("DDD00041").Should().Be(1, "the outer Any is what a policy cannot ask; the inner one is not reported again");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
    }

    [Fact]
    public void A_rule_asks_an_access_function_about_its_row()
    {
        var result = GeneratorTestHost.Create(Shop + Membership +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class MembersSeeTheirOrders
            {
                public static bool Allows(Order order, Caller caller) => order.IsPublic || Membership.Allows(order, caller);
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        ConstantIn(result.Source("MembersSeeTheirOrders.RowAccess")).Should().Be("({col:IsPublic} OR {call:shop.is_member})");
    }

    [Fact]
    public void An_access_function_is_asked_about_the_rules_own_row_and_caller()
    {
        var result = GeneratorTestHost.Create(Shop + Membership +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => Membership.Allows(order, Caller.Anonymous);
            }
            """).RunCore();

        result.Count("DDD00039").Should().Be(1);
    }

    [Fact]
    public void An_access_function_gets_its_name_and_an_Allows_that_takes_the_aggregates_key()
    {
        var result = GeneratorTestHost.Create(Shop + Membership).RunCore();

        result.ShouldCompile();
        result.ShouldContain("Membership.AccessFunction", "public const string Name = \"shop.is_member\";");
        result.ShouldContain("Membership.AccessFunction", "public static bool Allows(global::Shop.OrderId orderId) => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"shop.is_member(orderId)\");");
    }

    [Fact]
    public void A_rule_asks_an_access_function_by_a_key_it_holds()
    {
        var result = GeneratorTestHost.Create(Shop + Membership +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class MembersSeeTheirOrders
            {
                public static bool Allows(Order order, Caller caller) => Membership.Allows(order.Id);
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        ConstantIn(result.Source("MembersSeeTheirOrders.RowAccess")).Should().Be("{fn:shop.is_member}({col:Id})");
    }

    [Fact]
    public void A_contract_publishes_the_function_to_other_modules_by_its_key()
    {
        var result = GeneratorTestHost.Create(Shop +
            """
            [AccessFunctionContract<OrderId>("shop.is_member")]
            public static partial class OrderMembers;

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class MembersSeeTheirOrders
            {
                public static bool Allows(Order order, Caller caller) => OrderMembers.Allows(order.Id);
            }
            """).RunCore();

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        result.ShouldContain("OrderMembers.AccessFunctionContract", "public const string Name = \"shop.is_member\";");
        result.ShouldContain("OrderMembers.AccessFunctionContract", "public static bool Allows(global::Shop.OrderId orderId)");
        ConstantIn(result.Source("MembersSeeTheirOrders.RowAccess")).Should().Be("{fn:shop.is_member}({col:Id})");
    }

    [Fact]
    public void A_contract_named_without_its_schema_in_an_assembly_without_a_module_is_DDD00052()
        => GeneratorTestHost.Create(Shop +
            """
            [AccessFunctionContract<OrderId>("is_member")]
            public static partial class OrderMembers;
            """).RunCore().ShouldHaveDiagnostic("DDD00052", at: "OrderMembers");

    [Theory]
    [InlineData("is_member", "DDD00052")]
    [InlineData("shop.is-member", "DDD00038")]
    public void An_access_function_named_without_its_schema_and_owner_or_not_as_an_identifier_is_an_error(string name, string id)
        => Function("public static bool Allows(Order order, Caller caller) => order.IsPublic;", name).Count(id).Should().Be(1);

    // ------------------------------------------------------------------ DDD00041

    [Fact]
    public void A_rule_that_reads_the_entities_itself_is_told_to_ask_an_access_function()
    {
        var result = Rule("public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.UserId == caller.UserId);");

        result.ReportedDiagnostics.Single(diagnostic => diagnostic.Id == "DDD00041").GetMessage().Should().Contain("[AccessFunction<Order>]");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
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
