using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A set the access to a resource answers, published by the resource's id with
/// <c>[ResourceAccessContract&lt;TKey&gt;]</c>: what the generator writes into the contract, how a rule that asks
/// it is translated, in its own module and in another, and which declarations it refuses. The name a rule asks
/// it by is the one the export resolves, and a test here holds the two spellings of it together.
/// </summary>
public class ResourceAccessContractGenerationTests
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

            public int Level { get; private set; }
        }


        """;

    private static GeneratorRunOutcome Run(string source) => GeneratorTestHost.Create(Shop + source).RunCore();

    /// <summary>The SQL the generator wrote into the rule <c>TheRule</c>.</summary>
    private static string RuleSql(GeneratorRunOutcome result)
    {
        var literal = CSharpSyntaxTree.ParseText(result.Source("TheRule.RowAccess")).GetRoot()
            .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(variable => variable.Identifier.Text == "RowAccessSql")
            .Initializer!.Value;

        return ((LiteralExpressionSyntax)literal).Token.ValueText;
    }

    [Fact]
    public void The_resources_seen_are_asked_by_the_resources_id_and_no_functions_name()
    {
        var result = Run(
            """
            [ResourceAccessContract<OrderId>(ResourceAccessSet.Seen)]
            public static partial class OrdersISee;

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => OrdersISee.Ids().Contains(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        result.ShouldContain("OrdersISee.ResourceAccessContract", "public const string Name = \"@Shop.OrderId/seen\";");
        result.ShouldContain("OrdersISee.ResourceAccessContract", "public static global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> Ids() => throw new global::DDDToolkit.Abstractions.Access.DatabaseOnlyException(\"@Shop.OrderId/seen()\");");
        RuleSql(result).Should().Be("({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/seen}())))", "the set is asked once per statement, by a name the export resolves");
    }

    [Fact]
    public void The_resources_a_key_is_held_on_are_asked_with_the_key()
    {
        var result = Run(
            """
            [ResourceAccessContract<OrderId>(ResourceAccessSet.HeldOn)]
            public static partial class OrdersWhereIHold;

            [RowAccess<Order>(RowOperations.Change)]
            public static partial class TheRule
            {
                public const string Edit = "orders.edit";

                public static bool Allows(Order order, Caller caller) => OrdersWhereIHold.Ids(Edit).Contains(order.Id) || OrdersWhereIHold.Ids("orders.close").Contains(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        result.ShouldContain("OrdersWhereIHold.ResourceAccessContract", "public const string Name = \"@Shop.OrderId/held_on\";");
        result.ShouldContain("OrdersWhereIHold.ResourceAccessContract", "public static global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> Ids(string key) => throw");
        RuleSql(result).Should().Be(
            "(({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/held_on}('orders.edit')))) OR ({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/held_on}('orders.close')))))");

        var emitted = result.Emit();
        var asked = () => emitted.Type("Shop.OrdersWhereIHold").GetMethod("Ids")!.Invoke(null, ["orders.edit"]);
        asked.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>()
            .Which.GetType().Name.Should().Be("DatabaseOnlyException", "only the database answers it");
    }

    [Fact]
    public void Another_modules_rule_asks_a_published_contract_by_what_its_metadata_says()
    {
        const string Contracts =
            """
            using DDDToolkit.Abstractions.Attributes;

            [assembly: Module("Projects")]

            namespace Projects.Contracts;

            [ModuleContract]
            [EntityId<System.Guid>]
            public readonly partial record struct ProjectId;

            [ModuleContract]
            [ResourceAccessContract<ProjectId>(ResourceAccessSet.Seen)]
            public static partial class ProjectsISee;

            [ModuleContract]
            [ResourceAccessContract<ProjectId>(ResourceAccessSet.HeldOn)]
            public static partial class ProjectsWhereIHold;
            """;

        var result = GeneratorTestHost.Create(
            """
            using System;
            using DDDToolkit.Abstractions.Access;
            using DDDToolkit.Abstractions.Attributes;
            using Projects.Contracts;

            [assembly: Module("Inspections")]

            namespace Inspections;

            [EntityId<Guid>]
            public readonly partial record struct InspectionId;

            [AggregateRoot<InspectionId>]
            public partial class Inspection
            {
                public Inspection(InspectionId id) : base(id) { }

                public ProjectId ProjectId { get; private set; }
            }

            [RowAccess<Inspection>(RowOperations.Read | RowOperations.Create)]
            public static partial class TheRule
            {
                public static bool Allows(Inspection inspection, Caller caller)
                    => ProjectsISee.Ids().Contains(inspection.ProjectId) && ProjectsWhereIHold.Ids("inspections.record").Contains(inspection.ProjectId);
            }
            """).WithReferencedAssembly(Contracts, "Projects.Contracts").RunCore();

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be(
            "(({col:ProjectId} = ANY (ARRAY(SELECT {fn:@Projects.Contracts.ProjectId/seen}()))) AND ({col:ProjectId} = ANY (ARRAY(SELECT {fn:@Projects.Contracts.ProjectId/held_on}('inspections.record')))))",
            "the asking module knows the contract and the id, and neither the projects nor what answers for them");
    }

    [Fact]
    public void The_name_the_generator_writes_is_the_one_the_export_resolves()
    {
        // A key nested in another type, which the runtime spells with a '+': the one place the two spellings could part.
        var result = Run(
            """
            public static partial class Desk
            {
                [EntityId<Guid>]
                public readonly partial record struct TicketId;
            }

            [ResourceAccessContract<Desk.TicketId>(ResourceAccessSet.Seen)]
            public static partial class TicketsISee;

            [ResourceAccessContract<OrderId>(ResourceAccessSet.HeldOn)]
            public static partial class OrdersWhereIHold;
            """);

        result.ShouldCompile();
        var emitted = result.Emit();
        string NameIn(string contract) => (string)emitted.Type(contract).GetField("Name")!.GetRawConstantValue()!;

        NameIn("Shop.TicketsISee").Should().Be(ResourceAccessAnswer.NameOf(emitted.Type("Shop.Desk+TicketId"), ResourceAccessSet.Seen)).And.Be("@Shop.Desk+TicketId/seen");
        NameIn("Shop.OrdersWhereIHold").Should().Be(ResourceAccessAnswer.NameOf(emitted.Type("Shop.OrderId"), ResourceAccessSet.HeldOn));
    }

    [Fact]
    public void A_contract_that_declares_its_question_gets_it_implemented()
    {
        var result = Run(
            """
            [ResourceAccessContract<OrderId>(ResourceAccessSet.HeldOn)]
            public static partial class OrdersWhereIHold
            {
                /// <summary>The orders the caller holds <paramref name="permission"/> on.</summary>
                internal static partial AccessSet<OrderId> Ids(string permission);
            }

            [ResourceAccessContract<OrderId>(ResourceAccessSet.Seen)]
            public static partial class OrdersISee
            {
                public static partial AccessSet<OrderId> Ids();
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldCompile();
        result.ShouldContain("OrdersWhereIHold.ResourceAccessContract", "internal static partial global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> Ids(string permission) => throw");
        result.ShouldContain("OrdersISee.ResourceAccessContract", "public static partial global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.OrderId> Ids() => throw");
    }

    [Theory]
    [InlineData("ResourceAccessSet.Seen", "public static partial AccessSet<OrderId> Ids(string key);", "Ids")]
    [InlineData("ResourceAccessSet.HeldOn", "public static partial AccessSet<OrderId> Ids();", "Ids")]
    [InlineData("ResourceAccessSet.HeldOn", "public static partial AccessSet<OrderId> Ids(int level);", "Ids")]
    [InlineData("ResourceAccessSet.Seen", "public static partial AccessSet<Guid> Ids();", "Ids")]
    [InlineData("ResourceAccessSet.Seen", "public static AccessSet<OrderId> Ids() => default!;", "Ids")]
    [InlineData("ResourceAccessSet.Seen", "public static partial bool Allows(OrderId order);", "Allows")]
    public void A_question_of_another_shape_is_DDD00038(string set, string declaration, string at)
        => Run(
            $$"""
            [ResourceAccessContract<OrderId>({{set}})]
            public static partial class OrderSet
            {
                {{declaration}}
            }
            """).ShouldHaveDiagnostic("DDD00038", at: at).GetMessage().Should().Contain("OrderSet");

    [Fact]
    public void A_key_that_is_no_id_is_DDD00038()
        => Run(
            """
            [ResourceAccessContract<Guid>(ResourceAccessSet.Seen)]
            public static partial class Anything;
            """).ShouldHaveDiagnostic("DDD00038", at: "Anything").GetMessage().Should().Contain("the export finds the resource by the type of its id");

    [Fact]
    public void A_contract_that_is_no_static_partial_class_is_DDD00038()
        => Run(
            """
            [ResourceAccessContract<OrderId>(ResourceAccessSet.Seen)]
            public partial class OrdersISee;
            """).ShouldHaveDiagnostic("DDD00038", at: "OrdersISee").GetMessage().Should().Contain("static partial class");

    [Fact]
    public void A_set_the_enum_does_not_have_is_DDD00038()
        => Run(
            """
            [ResourceAccessContract<OrderId>((ResourceAccessSet)7)]
            public static partial class OrdersSomehow;
            """).ShouldHaveDiagnostic("DDD00038", at: "OrdersSomehow").GetMessage().Should().Contain("ResourceAccessSet.Seen or ResourceAccessSet.HeldOn");

    [Fact]
    public void The_id_the_toolkit_writes_beside_an_aggregate_root_keys_a_contract_as_a_declared_one_does()
    {
        // [AggregateRoot<Guid>] has the toolkit write BoardId, which no other generator sees while this one runs.
        var result = Run(
            """
            [AggregateRoot<Guid>]
            public partial class Board
            {
                public Board(BoardId id) : base(id) { }
            }

            [ResourceAccessContract<BoardId>(ResourceAccessSet.Seen)]
            public static partial class BoardsISee;

            [ResourceAccessContract<BoardId>(ResourceAccessSet.HeldOn)]
            public static partial class BoardsWhereIHold
            {
                public static partial AccessSet<BoardId> Ids(string key);
            }

            [RowAccess<Board>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Board board, Caller caller) => BoardsISee.Ids().Contains(board.Id) || BoardsWhereIHold.Ids("boards.edit").Contains(board.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        result.ShouldContain("BoardsISee.ResourceAccessContract", "public static global::DDDToolkit.Abstractions.Access.AccessSet<global::Shop.BoardId> Ids() => throw");
        RuleSql(result).Should().Be("(({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/seen}()))) OR ({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/held_on}('boards.edit')))))");

        var emitted = result.Emit();
        ((string)emitted.Type("Shop.BoardsISee").GetField("Name")!.GetRawConstantValue()!).Should().Be(
            ResourceAccessAnswer.NameOf(emitted.Type("Shop.BoardId"), ResourceAccessSet.Seen), "the export finds the resource by the id the toolkit wrote");
    }

    [Fact]
    public void An_id_the_generator_cannot_find_is_DDD00038_that_says_to_declare_it()
        => Run(
            """
            [ResourceAccessContract<CrateId>(ResourceAccessSet.Seen)]
            public static partial class CratesISee;
            """).ShouldHaveDiagnostic("DDD00038", at: "CratesISee").GetMessage().Should().Contain(
                "Declare the id itself: [EntityId<Guid>] public readonly partial record struct CrateId;", "no aggregate root here has the toolkit write CrateId");

    private const string ProjectsContracts =
        """
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Projects")]

        namespace Projects.Contracts;

        [ModuleContract]
        [EntityId<System.Guid>]
        public readonly partial record struct ProjectId;
        """;

    [Fact]
    public void Another_module_declaring_a_contract_of_a_resource_it_does_not_own_is_DDD00038()
        => GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;
                using Projects.Contracts;

                [assembly: Module("Inspections")]

                namespace Inspections;

                [ResourceAccessContract<ProjectId>(ResourceAccessSet.HeldOn)]
                public static partial class ProjectsWhereIHoldAsInspectionsSaysSo;
                """)
            .WithReferencedAssembly(ProjectsContracts, "Projects.Contracts").RunCore()
            .ShouldHaveDiagnostic("DDD00038", at: "ProjectsWhereIHoldAsInspectionsSaysSo").GetMessage().Should().Contain(
                "to be declared in the module that owns ProjectId, Projects", "what Projects publishes of its projects is Projects' own to say, and Inspections asks its contract");

    [Fact]
    public void A_project_of_the_owning_module_declares_a_contract_of_an_id_in_its_contracts()
        => GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;
                using Projects.Contracts;

                [assembly: Module("Projects")]

                namespace Projects.Infrastructure;

                [ResourceAccessContract<ProjectId>(ResourceAccessSet.Seen)]
                public static partial class ProjectsISee;
                """)
            .WithReferencedAssembly(ProjectsContracts, "Projects.Contracts").RunCore()
            .ShouldNotHaveDiagnostic("DDD00038").ShouldCompile();

    [Fact]
    public void A_set_asked_with_anything_but_Contains_is_DDD00039_that_names_the_contract_among_what_a_rule_may_ask()
        => Run(
            """
            [ResourceAccessContract<OrderId>(ResourceAccessSet.Seen)]
            public static partial class OrdersISee;

            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, Caller caller) => OrdersISee.Ids() != null;
            }
            """).ShouldHaveDiagnostic("DDD00039", at: "OrdersISee.Ids()").GetMessage().Should().Contain("the Ids of a [ResourceAccessContract]");
}
