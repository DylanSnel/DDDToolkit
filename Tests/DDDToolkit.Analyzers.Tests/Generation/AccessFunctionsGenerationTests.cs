using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// Questions only the database answers, as rules ask them and as the generator declares them: set-shaped
/// questions and scalar ones of an <c>[AccessFunctions]</c> class, access functions that take more than the
/// aggregate or answer with a set of keys, the questions a contract declares, and the names they go by,
/// with their schema or relative to the module that owns them.
/// </summary>
public class AccessFunctionsGenerationTests
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

        [AggregateRoot<OrderId>]
        public partial class Order
        {
            public Order(OrderId id) : base(id) { }

            public string? Team { get; private set; }

            public int Level { get; private set; }

            public Guid? PlacedBy { get; private set; }

            public partial System.Collections.Generic.IReadOnlyList<Member> Members { get; }
        }

        [EntityId<Guid>]
        public readonly partial record struct MemberId;

        [Entity<MemberId>]
        public partial class Member
        {
            public Member(MemberId id) : base(id) { }

            public Guid UserId { get; private set; }
        }


        """;

    /// <summary>The questions a shop asks the database, relative to its owner.</summary>
    private const string Questions =
        """
        [AccessFunctions]
        public static partial class ShopQuestions
        {
            [AccessSet("orders_i_follow")]
            public static partial AccessSet<OrderId> OrdersIFollow(string reason);

            [AccessScalar("is_cashier")]
            public static partial bool IsCashier();

            [AccessScalar("level_of")]
            public static partial int LevelOf(OrderId order);

            [AccessScalar("caller_team")]
            public static partial string CallerTeam();
        }


        """;

    private const string ShopModule = """[assembly: DDDToolkit.Abstractions.Attributes.Module("Shop")]""";

    private static GeneratorRunOutcome Run(string source, string? module = ShopModule)
    {
        var host = GeneratorTestHost.Create(Shop + source);
        return (module is null ? host : host.WithSource(module, "AssemblyInfo.cs")).RunCore();
    }

    /// <summary>The SQL the generator wrote into <paramref name="type"/>, a rule or an access function.</summary>
    private static string SqlIn(GeneratorRunOutcome result, string hint)
    {
        var literal = CSharpSyntaxTree.ParseText(result.Source(hint)).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

    private static string RuleSqlOf(string expression, string? module = ShopModule, string more = "")
    {
        var result = Run(Questions + more +
            $$"""
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => {{expression}};
            }
            """,
            module);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00051").ShouldNotHaveDiagnostic("DDD00052").ShouldCompile();
        return SqlIn(result, "TheRule.RowAccess");
    }

    // ------------------------------------------------------------------ questions in rules

    [Fact]
    public void A_set_question_in_a_rule_is_any_of_an_array_of_one_select()
        => RuleSqlOf("ShopQuestions.OrdersIFollow(\"watching\").Contains(order.Id)")
            .Should().Be("({col:Id} = ANY (ARRAY(SELECT {fn:shop/orders_i_follow}('watching'))))");

    [Fact]
    public void A_scalar_question_without_row_arguments_is_wrapped_in_a_select()
        => RuleSqlOf("ShopQuestions.IsCashier() && order.Level < 3")
            .Should().Be("((SELECT {fn:shop/is_cashier}()) AND coalesce({col:Level} < 3, FALSE))");

    [Fact]
    public void A_scalar_question_with_a_row_argument_is_asked_per_row()
        => RuleSqlOf("ShopQuestions.LevelOf(order.Id) > 2")
            .Should().Be("({fn:shop/level_of}({col:Id}) > 2)");

    [Fact]
    public void A_set_question_argument_that_reads_the_row_is_DDD00051()
    {
        var result = Run(Questions +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => ShopQuestions.OrdersIFollow(order.Team!).Contains(order.Id);
            }
            """);

        result.ShouldHaveDiagnostic("DDD00051", at: "order.Team!").GetMessage().Should().Contain("'ShopQuestions.OrdersIFollow'").And.Contain("once per row");
        result.Count("DDD00051").Should().Be(1, "the value compared with the set, the argument of Contains, is what may read the row");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
    }

    [Fact]
    public void A_set_question_asked_for_anything_but_Contains_is_untranslatable()
    {
        var result = Run(Questions +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => ShopQuestions.OrdersIFollow("x").Equals(null);
            }
            """);

        result.Count("DDD00039").Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------ questions as the generator declares them

    [Fact]
    public void A_question_gets_a_body_that_throws_with_its_logical_name()
    {
        var result = Run(Questions);

        result.ShouldCompile();
        result.ShouldContain("ShopQuestions.AccessFunctions", "public static partial global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> OrdersIFollow(string reason) => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"shop/orders_i_follow\");");

        var asked = () => result.Emit().Type("Shop.ShopQuestions").GetMethod("CallerTeam")!.Invoke(null, null);
        asked.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>()
            .Which.GetType().Name.Should().Be("DatabaseOnlyException");
    }

    [Fact]
    public void A_generic_question_over_an_entity_id_is_allowed()
    {
        var result = Run(
            """
            [AccessFunctions]
            public static partial class Holdings
            {
                [AccessSet("ids_where_i_hold")]
                public static partial AccessSet<TId> IdsWhereIHold<TId>(string key) where TId : DDDToolkit.Abstractions.Interfaces.IEntityId;
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => Holdings.IdsWhereIHold<OrderId>("orders.view").Contains(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldCompile();
        result.ShouldContain("Holdings.AccessFunctions", "IdsWhereIHold<TId>(string key) where TId : global::DDDToolkit.Abstractions.Interfaces.IEntityId => throw");
        SqlIn(result, "TheRule.RowAccess").Should().Be("({col:Id} = ANY (ARRAY(SELECT {fn:shop/ids_where_i_hold}('orders.view'))))");
    }

    [Fact]
    public void A_generic_question_takes_an_argument_of_the_hosts_id_type()
    {
        // A package's question about an id of the host's, which it can only name by a type parameter: a scalar one
        // is asked of the row, and a set-shaped one of a parameter of the module's own access function.
        var result = Run(
            """
            [AccessFunctions]
            public static partial class Holdings
            {
                [AccessScalar("seated_at")]
                public static partial bool SeatedAt<TPlace>(TPlace place)
                    where TPlace : struct, DDDToolkit.Abstractions.Interfaces.IEntityId, IEquatable<TPlace>;

                [AccessSet("ids_held_at")]
                public static partial AccessSet<TId> IdsHeldAt<TId, TPlace>(TPlace place, string key)
                    where TId : struct, DDDToolkit.Abstractions.Interfaces.IEntityId, IEquatable<TId>
                    where TPlace : struct, DDDToolkit.Abstractions.Interfaces.IEntityId, IEquatable<TPlace>;
            }

            [AccessFunction<Order>("shop.held_at")]
            public static partial class HeldAt
            {
                public static bool Allows(Order order, Caller caller, MemberId place)
                    => Holdings.IdsHeldAt<OrderId, MemberId>(place, "orders.view").Contains(order.Id);
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => Holdings.SeatedAt<OrderId>(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldNotHaveDiagnostic("DDD00051").ShouldCompile();
        result.ShouldContain("Holdings.AccessFunctions", "SeatedAt<TPlace>(TPlace place) where TPlace : struct, global::DDDToolkit.Abstractions.Interfaces.IEntityId, global::System.IEquatable<TPlace> => throw");
        result.ShouldContain("Holdings.AccessFunctions", "IdsHeldAt<TId, TPlace>(TPlace place, string key) where TId : struct");

        SqlIn(result, "TheRule.RowAccess").Should().Be("{fn:shop/seated_at}({col:Id})", "the argument reads the row, so the question is asked of each row");
        SqlIn(result, "HeldAt.AccessFunction").Should().Be(
            "({col:Id} = ANY (ARRAY(SELECT {fn:shop/ids_held_at}({arg:1}, 'orders.view'))))",
            "the function's own parameter is its argument, so the set is worked out once");
        result.ShouldContain("HeldAt.AccessFunction", "public const string RowAccessParameters = \"uuid\";");
    }

    [Theory]
    [InlineData("[AccessSet(\"x\")] public static partial System.Collections.Generic.List<OrderId> X();", "to return AccessSet<T>")]
    [InlineData("[AccessScalar(\"x\")] public static partial AccessSet<OrderId> X();", "to return the one value")]
    [InlineData("[AccessScalar(\"x\")] public static partial bool X(decimal amount);", "'amount' is decimal")]
    [InlineData("[AccessScalar(\"x\")] public static bool X() => true;", "static partial method without a body")]
    [InlineData("[AccessScalar(\"x\")] public static partial bool X(Guid? user);", "'user' is System.Guid?")]
    public void A_question_of_another_shape_is_DDD00038(string declaration, string reason)
    {
        var result = Run(
            $$"""
            [AccessFunctions]
            public static partial class ShopQuestions
            {
                {{declaration}}
            }
            """);

        result.ShouldHaveDiagnostic("DDD00038", at: "X").GetMessage().Should().Contain(reason);
    }

    [Fact]
    public void A_question_outside_an_AccessFunctions_class_is_DDD00038()
        => Run(
            """
            public static partial class NotMarked
            {
                [AccessScalar("x")]
                public static partial bool X();
            }
            """).ShouldHaveDiagnostic("DDD00038", at: "X").GetMessage().Should().Contain("[AccessFunctions]");

    // ------------------------------------------------------------------ names

    [Fact]
    public void A_relative_name_takes_the_modules_owner()
    {
        var result = Run(Questions +
            """
            [AccessFunction<Order>("is_member")]
            public static partial class Membership
            {
                public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.UserId == caller.UserId);
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => Membership.Allows(order, caller) || ShopQuestions.OrdersIFollow("x").Contains(order.Id);
            }
            """,
            """[assembly: DDDToolkit.Abstractions.Attributes.Module("Shop Floor")]""");

        result.ShouldCompile();
        result.ShouldContain("Membership.AccessFunction", "public const string Name = \"shop-floor/is_member\";");
        result.ShouldContain("Membership.AccessFunction", "public const string? RowAccessOwner = \"shop-floor\";");
        SqlIn(result, "TheRule.RowAccess").Should().Be("({call:shop-floor/is_member} OR ({col:Id} = ANY (ARRAY(SELECT {fn:shop-floor/orders_i_follow}('x')))))",
            "the owner is the module's name as a migration's file name writes it");
    }

    [Fact]
    public void A_package_class_names_its_owner()
    {
        var questions = Questions.Replace("[AccessFunctions]", "[AccessFunctions(Owner = \"Desk\")]", StringComparison.Ordinal);

        var result = Run(questions +
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => ShopQuestions.IsCashier();
            }
            """);

        result.ShouldCompile();
        SqlIn(result, "TheRule.RowAccess").Should().Be("(SELECT {fn:desk/is_cashier}())", "the class's owner takes the module's place");
    }

    [Fact]
    public void A_relative_name_with_no_owner_is_DDD00052()
    {
        var result = Run(
            """
            [AccessFunctions]
            public static partial class Questions
            {
                [AccessScalar("is_cashier")]
                public static partial bool IsCashier();
            }

            [AccessFunction<Order>("is_member")]
            public static partial class Membership
            {
                public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.UserId == caller.UserId);
            }

            [AccessFunctionContract<OrderId>("is_member")]
            public static partial class OrderMembers;
            """,
            module: null);

        result.ShouldHaveDiagnostic("DDD00052", at: "IsCashier").GetMessage().Should().Be(
            "'is_cashier' is named without its schema, and nothing says which module it belongs to. Add [assembly: Module(...)], give the class [AccessFunctions(Owner = ...)], or write schema.name.");
        result.ShouldHaveDiagnostic("DDD00052", at: "Membership");
        result.ShouldHaveDiagnostic("DDD00052", at: "OrderMembers");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("Membership.AccessFunction") || source.HintName.Contains("OrderMembers"));
    }

    [Fact]
    public void A_qualified_name_keeps_working()
    {
        var fixedResult = Run(
            """
            [AccessFunctions]
            public static partial class Questions
            {
                [AccessScalar("support.is_agent")]
                public static partial bool IsAgent(string team);
            }

            [AccessFunction<Order>("shop.is_member")]
            public static partial class Membership
            {
                public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.UserId == caller.UserId);
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => Membership.Allows(order, caller) || Questions.IsAgent("north");
            }
            """,
            module: null);

        fixedResult.ShouldNotHaveDiagnostic("DDD00052").ShouldCompile();
        fixedResult.ShouldContain("Membership.AccessFunction", "public const string Name = \"shop.is_member\";");
        SqlIn(fixedResult, "TheRule.RowAccess").Should().Be(
            "({call:shop.is_member} OR (SELECT support.is_agent('north')))",
            "a question named with its schema is a function of your own, called as it is, like Sql.Call");
    }

    [Theory]
    [InlineData("shop.is-member")]
    [InlineData("a.b.c")]
    [InlineData("Shop/is_member")]
    [InlineData("shop/is member")]
    public void A_name_that_is_no_function_name_is_DDD00038(string name)
        => Run(
            $$"""
            [AccessFunction<Order>("{{name}}")]
            public static partial class Membership
            {
                public static bool Allows(Order order, Caller caller) => order.Level > 1;
            }
            """).ShouldHaveDiagnostic("DDD00038", at: "Membership");

    [Fact]
    public void A_contracts_name_constant_is_its_logical_name()
    {
        var result = GeneratorTestHost.Create(
            """
            [assembly: DDDToolkit.Abstractions.Attributes.Module("Projects")]

            namespace Projects.Contracts;

            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct ProjectId;

            [DDDToolkit.Abstractions.Attributes.AccessFunctionContract<ProjectId>("project_ids_where_i_hold", Shape = DDDToolkit.Abstractions.Attributes.AccessFunctionShape.Set)]
            public static partial class ProjectsWhereIHold;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain("ProjectsWhereIHold.AccessFunctionContract", "public const string Name = \"projects/project_ids_where_i_hold\";");
        result.ShouldContain("ProjectsWhereIHold.AccessFunctionContract", "public static global::DDDToolkit.Abstractions.Access.AccessSet<global::Projects.Contracts.ProjectId> Ids()");
    }

    [Fact]
    public void An_access_function_named_with_a_slash_defines_another_modules_contract()
    {
        const string Contracts =
            """
            [assembly: DDDToolkit.Abstractions.Attributes.Module("Projects")]

            namespace Projects.Contracts;

            [DDDToolkit.Abstractions.Attributes.EntityId<System.Guid>]
            public readonly partial record struct ProjectId;

            [DDDToolkit.Abstractions.Attributes.AccessFunctionContract<ProjectId>("project_ids_where_i_hold", Shape = DDDToolkit.Abstractions.Attributes.AccessFunctionShape.Set)]
            public static partial class ProjectsWhereIHold;
            """;

        var result = GeneratorTestHost.Create(
            """
            using System;
            using System.Linq;
            using DDDToolkit.Abstractions.Access;
            using DDDToolkit.Abstractions.Attributes;
            using Projects.Contracts;

            [assembly: Module("Project Definitions")]

            namespace Projects;

            [AggregateRoot<ProjectId>]
            public partial class Project
            {
                public Project(ProjectId id) : base(id) { }

                public partial System.Collections.Generic.IReadOnlyList<Seat> Crew { get; }
            }

            [EntityId<Guid>]
            public readonly partial record struct SeatId;

            [Entity<SeatId>]
            public partial class Seat
            {
                public Seat(SeatId id) : base(id) { }

                public Guid UserId { get; private set; }
            }

            [AccessFunction<Project>(ProjectsWhereIHold.Name, Shape = AccessFunctionShape.Set)]
            public static partial class ProjectsOfMyCrews
            {
                public static bool Allows(Project project, Caller caller) => project.Crew.Any(seat => seat.UserId == caller.UserId);
            }

            [RowAccess<Project>(RowOperations.Read)]
            public static partial class CrewsSeeTheirProjects
            {
                public static bool Allows(Project project, Caller caller) => ProjectsWhereIHold.Ids().Contains(project.Id);
            }
            """).WithReferencedAssembly(Contracts, "Projects.Contracts").RunCore();

        result.ShouldCompile();
        result.ShouldContain("ProjectsOfMyCrews.AccessFunction", "public const string Name = \"projects/project_ids_where_i_hold\";",
            "a name with a slash is taken as it is, whichever module the definition is in");
        SqlIn(result, "CrewsSeeTheirProjects.RowAccess").Should().Be("({col:Id} = ANY (ARRAY(SELECT {fn:projects/project_ids_where_i_hold}())))");
    }

    // ------------------------------------------------------------------ access functions that take more, or answer with a set

    [Fact]
    public void An_access_function_with_extra_parameters_writes_arg_tokens()
    {
        var result = Run(
            """
            [AccessFunction<Order>("shop.in_team_at_level")]
            public static partial class InTeamAtLevel
            {
                public static bool Allows(Order order, Caller caller, string team, int level) => order.Team == team && order.Level >= level;
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => InTeamAtLevel.Allows(order, caller, "north", 2) || InTeamAtLevel.Allows(order.Id, "south", 3);
            }
            """);

        result.ShouldCompile();
        SqlIn(result, "InTeamAtLevel.AccessFunction").Should().Be("(({col:Team} IS NOT DISTINCT FROM {arg:1}) AND coalesce({col:Level} >= {arg:2}, FALSE))");
        result.ShouldContain("InTeamAtLevel.AccessFunction", "public const string RowAccessParameters = \"text, integer\";");
        result.ShouldContain("InTeamAtLevel.AccessFunction", "public static bool Allows(global::Shop.OrderId orderId, string team, int level) => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"shop.in_team_at_level(orderId, team, level)\");");
        SqlIn(result, "TheRule.RowAccess").Should().Be("({fn:shop.in_team_at_level}({key}, 'north', 2) OR {fn:shop.in_team_at_level}({col:Id}, 'south', 3))");
    }

    [Fact]
    public void An_access_function_parameter_it_cannot_pass_to_sql_is_DDD00038()
        => Run(
            """
            [AccessFunction<Order>("shop.over")]
            public static partial class Over
            {
                public static bool Allows(Order order, Caller caller, decimal amount) => order.Level > 1;
            }
            """).ShouldHaveDiagnostic("DDD00038", at: "amount").GetMessage().Should().Contain("'amount' is decimal");

    [Fact]
    public void A_set_access_function_generates_Ids_and_its_constants()
    {
        var result = Run(
            """
            [AccessFunction<Order>("orders_i_am_in", Shape = AccessFunctionShape.Set)]
            public static partial class OrdersIAmIn
            {
                public static bool Allows(Order order, Caller caller) => order.Members.Any(member => member.UserId == caller.UserId);
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => OrdersIAmIn.Ids().Contains(order.Id) || OrdersIAmIn.Allows(order, caller);
            }
            """);

        result.ShouldCompile();
        result.ShouldContain("OrdersIAmIn.AccessFunction", "public const string Name = \"shop/orders_i_am_in\";");
        result.ShouldContain("OrdersIAmIn.AccessFunction", "public const string? RowAccessOwner = \"shop\";");
        result.ShouldContain("OrdersIAmIn.AccessFunction", "public const string RowAccessParameters = \"\";");
        result.ShouldContain("OrdersIAmIn.AccessFunction", "public const global::DDDToolkit.Abstractions.Attributes.AccessFunctionShape RowAccessShape = global::DDDToolkit.Abstractions.Attributes.AccessFunctionShape.Set;");
        result.ShouldContain("OrdersIAmIn.AccessFunction", "public static global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> Ids() => throw");
        result.ShouldNotContain("OrdersIAmIn.AccessFunction", "public static bool Allows(global::Shop.OrderId");
        SqlIn(result, "TheRule.RowAccess").Should().Be(
            "(({col:Id} = ANY (ARRAY(SELECT {fn:shop/orders_i_am_in}()))) OR ({key} = ANY (ARRAY(SELECT {fn:shop/orders_i_am_in}()))))",
            "asked about its own row, a set-shaped function is asked whether the row's key is in its set");
    }

    [Fact]
    public void A_composite_key_cannot_be_a_set_function()
        => GeneratorTestHost.Create(
            """
            using System;
            using DDDToolkit.Abstractions.Access;
            using DDDToolkit.Abstractions.Attributes;

            [assembly: Module("Shop")]

            namespace Shop;

            [EntityId<Guid>]
            public readonly partial record struct LineId;

            [AggregateRoot<LineId>]
            public partial class Line
            {
                public Line(LineId id, int number) : base(id) => Number = number;

                [KeyPart]
                public int Number { get; private set; }
            }

            [AccessFunction<Line>("lines_i_see", Shape = AccessFunctionShape.Set)]
            public static partial class LinesISee
            {
                public static bool Allows(Line line, Caller caller) => line.Number > 0;
            }
            """).RunCore().ShouldHaveDiagnostic("DDD00038", at: "LinesISee").GetMessage().Should().Contain("key is one column");

    // ------------------------------------------------------------------ contracts

    [Fact]
    public void A_contract_method_is_implemented_and_throws()
    {
        var result = Run(
            """
            [AccessFunctionContract<OrderId>("in_team")]
            public static partial class OrdersInTeam
            {
                public static partial bool Allows(OrderId order, string team);
            }

            [AccessFunctionContract<OrderId>("followed_for")]
            public static partial class OrdersFollowedFor
            {
                internal static partial AccessSet<OrderId> Ids(string reason, bool closedToo);
            }

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => OrdersInTeam.Allows(order.Id, "north") || OrdersFollowedFor.Ids("audit", true).Contains(order.Id);
            }
            """);

        result.ShouldCompile();
        result.ShouldContain("OrdersInTeam.AccessFunctionContract", "public static partial bool Allows(global::Shop.OrderId order, string team) => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"shop/in_team(order, team)\");");
        result.ShouldContain("OrdersFollowedFor.AccessFunctionContract", "internal static partial global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> Ids(string reason, bool closedToo) => throw");
        SqlIn(result, "TheRule.RowAccess").Should().Be("({fn:shop/in_team}({col:Id}, 'north') OR ({col:Id} = ANY (ARRAY(SELECT {fn:shop/followed_for}('audit', TRUE)))))");

        var emitted = result.Emit();
        var asked = () => emitted.Type("Shop.OrdersInTeam").GetMethod("Allows")!.Invoke(null, [Activator.CreateInstance(emitted.Type("Shop.OrderId")), "north"]);
        asked.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>()
            .Which.GetType().Name.Should().Be("DatabaseOnlyException");
    }

    [Fact]
    public void A_contract_without_methods_keeps_Allows_of_its_key()
    {
        var result = Run(
            """
            [AccessFunctionContract<OrderId>("shop.is_member")]
            public static partial class OrderMembers;
            """);

        result.ShouldCompile();
        result.ShouldContain("OrderMembers.AccessFunctionContract", "public const string Name = \"shop.is_member\";");
        result.ShouldContain("OrderMembers.AccessFunctionContract", "public static bool Allows(global::Shop.OrderId orderId) => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"shop.is_member(orderId)\");");
    }

    [Theory]
    [InlineData("public static partial bool Allows(string team);")]
    [InlineData("public static partial AccessSet<string> Ids();")]
    [InlineData("public static partial bool Allows(OrderId order); public static partial AccessSet<OrderId> Ids();")]
    [InlineData("public static bool Allows(OrderId order) => true;")]
    public void A_contract_question_of_another_shape_is_DDD00038(string declaration)
        => Run(
            $$"""
            [AccessFunctionContract<OrderId>("shop.is_member")]
            public static partial class OrderMembers
            {
                {{declaration}}
            }
            """).Count("DDD00038").Should().BeGreaterThan(0);

    [Fact]
    public void A_contract_that_declares_Ids_with_the_row_shape_is_DDD00038()
        => Run(
            """
            [AccessFunctionContract<OrderId>("shop.is_member", Shape = AccessFunctionShape.Row)]
            public static partial class OrderMembers
            {
                public static partial AccessSet<OrderId> Ids();
            }
            """).ShouldHaveDiagnostic("DDD00038", at: "Ids").GetMessage().Should().Contain("Shape = AccessFunctionShape.Set");
}
