using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A rule asks about a resource by the resource's id, of its user: <c>user.Sees&lt;TKey&gt;()</c>,
/// <c>user.Holds&lt;TKey&gt;(permission)</c> and <c>user.FellowMembers&lt;TKey, TMemberId&gt;()</c>. How such a rule is
/// translated, in the module that owns the id and in another, whichever generator writes the id, and what does not
/// compile. The name a rule asks by is the one the export resolves, and a test here holds the two spellings of it
/// together.
/// </summary>
public class ResourceQuestionsGenerationTests
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
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => user.Sees<OrderId>().Contains(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be("({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/seen}())))", "the set is asked once per statement, by a name the export resolves");
    }

    [Fact]
    public void The_resources_a_permission_is_held_on_are_asked_with_the_permission()
    {
        var result = Run(
            """
            [RowAccess<Order>(RowOperations.Change)]
            public static partial class TheRule
            {
                public const string Edit = "orders.edit";

                public static bool Allows(Order order, IRowUser user) => user.Holds<OrderId>(Edit).Contains(order.Id) || user.Holds<OrderId>("orders.close").Contains(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be(
            "(({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/held_on}('orders.edit')))) OR ({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/held_on}('orders.close')))))");
    }

    [Fact]
    public void Called_in_csharp_each_question_says_only_the_database_answers_it_by_the_name_the_export_resolves()
    {
        var user = RowUser.From(CurrentUserSnapshot.Anonymous);

        Asked(() => user.Sees<AnId>()).Should().Be(ResourceAccessAnswer.NameOf(typeof(AnId), ResourceAccessSet.Seen) + "()");
        Asked(() => user.Holds<AnId>("orders.edit")).Should().Be(ResourceAccessAnswer.NameOf(typeof(AnId), ResourceAccessSet.HeldOn) + "(permission)");
        Asked(() => user.FellowMembers<AnId, AnId>()).Should().Be(ResourceAccessAnswer.NameOf(typeof(AnId), ResourceAccessSet.FellowMembers) + "()");

        static string Asked(Func<AccessSet<AnId>> question)
            => FluentActions.Invoking(question).Should().Throw<DatabaseOnlyException>().Which.SqlText;
    }

    /// <summary>An id of the tests' own.</summary>
    private readonly record struct AnId : IEntityId;

    [Fact]
    public void The_members_of_the_resources_the_caller_is_a_member_of_are_asked_by_the_resources_id_with_the_id_the_members_are_known_by()
    {
        var result = Run(
            """
            [EntityId<Guid>]
            public readonly partial record struct ClerkId;

            [AggregateRoot<ClerkId>]
            public partial class Clerk
            {
                public Clerk(ClerkId id) : base(id) { }
            }

            [RowAccess<Clerk>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Clerk clerk, IRowUser user) => user.FellowMembers<OrderId, ClerkId>().Contains(clerk.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be("({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/fellow_members}())))", "the set is the resource's, and holds the members' ids");
    }

    [Fact]
    public void The_fellow_members_without_the_id_the_members_are_known_by_do_not_compile()
        => Run(
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => user.FellowMembers<OrderId>().Contains(order.Id);
            }
            """).CompilationErrors.Should().NotBeEmpty("the resource's id does not say what the members' ids are");

    [Fact]
    public void Another_modules_rule_needs_only_the_id_from_the_contracts()
    {
        const string Contracts =
            """
            using DDDToolkit.Abstractions.Attributes;

            [assembly: Module("Projects")]

            namespace Projects.Contracts;

            [ModuleContract]
            [EntityId<System.Guid>]
            public readonly partial record struct ProjectId;
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
                public static bool Allows(Inspection inspection, IRowUser user)
                    => user.Sees<ProjectId>().Contains(inspection.ProjectId) && user.Holds<ProjectId>("inspections.record").Contains(inspection.ProjectId);
            }
            """).WithReferencedAssembly(Contracts, "Projects.Contracts").RunCore();

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be(
            "(({col:ProjectId} = ANY (ARRAY(SELECT {fn:@Projects.Contracts.ProjectId/seen}()))) AND ({col:ProjectId} = ANY (ARRAY(SELECT {fn:@Projects.Contracts.ProjectId/held_on}('inspections.record')))))",
            "the asking module knows the id, and neither the projects nor what answers for them");
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

            [EntityId<Guid>]
            public readonly partial record struct NoteId;

            [AggregateRoot<NoteId>]
            public partial class Note
            {
                public Note(NoteId id) : base(id) { }

                public Desk.TicketId Ticket { get; private set; }

                public OrderId Order { get; private set; }
            }

            [RowAccess<Note>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Note note, IRowUser user) => user.Sees<Desk.TicketId>().Contains(note.Ticket) && user.Holds<OrderId>("orders.read").Contains(note.Order);
            }
            """);

        result.ShouldCompile();
        var emitted = result.Emit();
        var sql = (string)emitted.Type("Shop.TheRule").GetField("RowAccessSql")!.GetRawConstantValue()!;

        var seen = ResourceAccessAnswer.NameOf(emitted.Type("Shop.Desk+TicketId"), ResourceAccessSet.Seen);
        var held = ResourceAccessAnswer.NameOf(emitted.Type("Shop.OrderId"), ResourceAccessSet.HeldOn);
        seen.Should().Be("@Shop.Desk+TicketId/seen");
        sql.Should().Be("(({col:Ticket} = ANY (ARRAY(SELECT {fn:" + seen + "}()))) AND ({col:Order} = ANY (ARRAY(SELECT {fn:" + held + "}('orders.read')))))");
    }

    [Theory]
    [InlineData("user.Sees<OrderId>(\"orders.read\").Contains(order.Id)")]
    [InlineData("user.Holds<OrderId>().Contains(order.Id)")]
    [InlineData("user.Holds<OrderId>(3).Contains(order.Id)")]
    public void A_question_of_another_shape_does_not_compile(string expression)
        => Run(
            $$"""
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => {{expression}};
            }
            """).CompilationErrors.Should().NotBeEmpty();

    [Fact]
    public void A_key_that_is_no_id_does_not_compile()
    {
        var result = Run(
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => user.Sees<Guid>().Contains(Guid.Empty);
            }
            """);

        result.CompilationErrors.Should().Contain(error => error.Id == "CS0315", "the id of the resource's aggregate is what the export finds it by");
        result.Count("DDD00039").Should().BeGreaterThan(0, "and no SQL is written for it");
    }

    [Fact]
    public void The_id_the_toolkit_writes_beside_an_aggregate_root_is_asked_about_as_a_declared_one_is()
    {
        // [AggregateRoot<Guid>] has the toolkit write BoardId, which no other generator sees while this one runs.
        var result = Run(
            """
            [AggregateRoot<Guid>]
            public partial class Board
            {
                public Board(BoardId id) : base(id) { }
            }

            [RowAccess<Board>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Board board, IRowUser user) => user.Sees<BoardId>().Contains(board.Id) || user.Holds<BoardId>("boards.edit").Contains(board.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be("(({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/seen}()))) OR ({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/held_on}('boards.edit')))))");

        var emitted = result.Emit();
        ((string)emitted.Type("Shop.TheRule").GetField("RowAccessSql")!.GetRawConstantValue()!).Should().Contain(
            ResourceAccessAnswer.NameOf(emitted.Type("Shop.BoardId"), ResourceAccessSet.Seen), "the export finds the resource by the id the toolkit wrote");
    }

    [Theory]
    [InlineData("user.Sees<BoardId>().Contains(board.Id)", "({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/seen}())))")]
    [InlineData("user.Holds<BoardId>(\"boards.edit\").Contains(board.Id)", "({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/held_on}('boards.edit'))))")]
    [InlineData("user.FellowMembers<BoardId, OrderId>().Contains(board.Order)", "({col:Order} = ANY (ARRAY(SELECT {fn:@Shop.BoardId/fellow_members}())))")]
    public void Each_question_is_asked_by_the_id_the_toolkit_writes_beside_an_aggregate_root(string expression, string sql)
    {
        var result = Run(
            $$"""
            [AggregateRoot<Guid>]
            public partial class Board
            {
                public Board(BoardId id) : base(id) { }

                public OrderId Order { get; private set; }
            }

            [RowAccess<Board>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Board board, IRowUser user) => {{expression}};
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be(sql);
    }

    [Fact]
    public void An_id_nothing_writes_does_not_compile_and_writes_no_sql()
    {
        var result = Run(
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => user.Sees<CrateId>().Contains(default);
            }
            """);

        result.CompilationErrors.Should().Contain(error => error.Id == "CS0246");
        result.ShouldHaveDiagnostic("DDD00039", at: "user.Sees<CrateId>()");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("TheRule.RowAccess"));
    }

    [Fact]
    public void A_rule_asks_by_an_id_the_switch_of_its_own_project_writes()
    {
        // A module of one project: the switch writes OrganizationUnitId, and the rule is beside it.
        var result = GeneratorTestHost.Create(
                """
                [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

                namespace Shop;

                using System;
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;

                [AggregateRoot<Guid>]
                public partial class Board
                {
                    public Board(BoardId id, OrganizationUnitId unit) : base(id) => Unit = unit;

                    public OrganizationUnitId Unit { get; private set; }
                }

                [RowAccess<Board>(RowOperations.Read)]
                public static partial class TheRule
                {
                    public static bool Allows(Board board, IRowUser user) => user.Holds<OrganizationUnitId>("boards.view").Contains(board.Unit);
                }
                """)
            .WithBuildProperty("RootNamespace", "Shop")
            .WithTenancy()
            .WithModuleFromTheBuild("Shop")
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be("({col:Unit} = ANY (ARRAY(SELECT {fn:@Shop.OrganizationUnitId/held_on}('boards.view'))))", "the rule asks the set by the id the switch wrote");
    }

    [Theory]
    [InlineData("user.Sees<OrganizationUnitId>().Contains(board.Unit)", "({col:Unit} = ANY (ARRAY(SELECT {fn:@Shop.OrganizationUnitId/seen}())))")]
    [InlineData("user.Holds<OrganizationUnitId>(\"boards.view\").Contains(board.Unit)", "({col:Unit} = ANY (ARRAY(SELECT {fn:@Shop.OrganizationUnitId/held_on}('boards.view'))))")]
    [InlineData("user.FellowMembers<OrganizationUnitId, SeatId>().Contains(board.Seat)", "({col:Seat} = ANY (ARRAY(SELECT {fn:@Shop.OrganizationUnitId/fellow_members}())))")]
    public void Each_question_is_asked_by_an_id_a_switch_of_the_project_writes(string expression, string sql)
    {
        var result = GeneratorTestHost.Create(
                $$"""
                [assembly: DDDToolkit.Supporting.Tenancy.GenerateTenancyClasses]

                namespace Shop;

                using System;
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;

                [AggregateRoot<Guid>]
                public partial class Board
                {
                    public Board(BoardId id, OrganizationUnitId unit, SeatId seat) : base(id) => (Unit, Seat) = (unit, seat);

                    public OrganizationUnitId Unit { get; private set; }

                    public SeatId Seat { get; private set; }
                }

                [RowAccess<Board>(RowOperations.Read)]
                public static partial class TheRule
                {
                    public static bool Allows(Board board, IRowUser user) => {{expression}};
                }
                """)
            .WithBuildProperty("RootNamespace", "Shop")
            .WithTenancy()
            .WithModuleFromTheBuild("Shop")
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be(sql);
    }

    /// <summary>Projects' contracts as the sample's build compiles them: the module is the folder's <c>DDD_Module</c>, and no file says it.</summary>
    private const string ProjectsContractsOfTheBuild =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Projects.Contracts;

        [ModuleContract]
        [EntityId<System.Guid>]
        public readonly partial record struct ProjectId;
        """;

    [Theory]
    [InlineData("Inspections", "another module, which sees the id and nothing else of Projects")]
    [InlineData("Projects", "another project of the module that owns the id")]
    public void Any_module_that_sees_the_id_asks_about_the_resource_where_the_build_declares_the_module(string module, string because)
    {
        var result = GeneratorTestHost.Create(
                """
                using System;
                using DDDToolkit.Abstractions.Access;
                using DDDToolkit.Abstractions.Attributes;
                using Projects.Contracts;

                namespace Asking;

                [EntityId<Guid>]
                public readonly partial record struct NoteId;

                [AggregateRoot<NoteId>]
                public partial class Note
                {
                    public Note(NoteId id) : base(id) { }

                    public ProjectId ProjectId { get; private set; }
                }

                [RowAccess<Note>(RowOperations.Read)]
                public static partial class TheRule
                {
                    public static bool Allows(Note note, IRowUser user) => user.Sees<ProjectId>().Contains(note.ProjectId);
                }
                """)
            .WithAssemblyName("Asking")
            .WithModuleFromTheBuild(module)
            .WithReferencedProject("Projects.Contracts", project => project
                .WithSource(ProjectsContractsOfTheBuild, "ProjectId.cs")
                .WithModuleFromTheBuild("Projects"))
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00038").ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be("({col:ProjectId} = ANY (ARRAY(SELECT {fn:@Projects.Contracts.ProjectId/seen}())))", because);
    }

    [Fact]
    public void A_set_asked_with_anything_but_Contains_is_DDD00039_that_names_the_questions_among_what_a_rule_may_ask()
        => Run(
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => user.Sees<OrderId>().Equals(null);
            }
            """).ShouldHaveDiagnostic("DDD00039", at: "user.Sees<OrderId>().Equals(null)").GetMessage().Should().Contain("Sees, Holds and FellowMembers");

    [Fact]
    public void The_static_form_carries_the_user_as_an_argument_which_adds_nothing_to_the_sql()
    {
        var result = Run(
            """
            [RowAccess<Order>(RowOperations.Change)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user)
                    => ResourceQuestions.Sees<OrderId>(user).Contains(order.Id) || ResourceQuestions.Holds<OrderId>(user, "orders.edit").Contains(order.Id);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00039").ShouldCompile();
        RuleSql(result).Should().Be("(({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/seen}()))) OR ({col:Id} = ANY (ARRAY(SELECT {fn:@Shop.OrderId/held_on}('orders.edit')))))");
    }

    [Theory]
    [InlineData("RowUser.From(CurrentUserSnapshot.Anonymous).Sees<OrderId>().Contains(order.Id)", "RowUser.From(CurrentUserSnapshot.Anonymous).Sees<OrderId>()")]
    [InlineData("ResourceQuestions.Sees<OrderId>(RowUser.From(CurrentUserSnapshot.Anonymous)).Contains(order.Id)", "ResourceQuestions.Sees<OrderId>(RowUser.From(CurrentUserSnapshot.Anonymous))")]
    public void A_question_asked_of_anything_but_the_rules_user_is_DDD00039(string expression, string at)
    {
        var result = Run(
            $$"""
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, IRowUser user) => {{expression}};
            }
            """);

        result.ShouldHaveDiagnostic("DDD00039", at: at);
        result.Count("DDD00039").Should().Be(1);
    }

    [Theory]
    [InlineData("currentUser.Sees<OrderId>()", "CS1929")]
    [InlineData("currentUser.Holds<OrderId>(\"orders.edit\")", "CS1929")]
    [InlineData("currentUser.FellowMembers<OrderId, OrderId>()", "CS1929")]
    [InlineData("ResourceQuestions.Sees<OrderId>(currentUser)", "CS1503")]
    public void A_question_is_not_there_on_the_current_user_a_handler_has(string question, string error)
        => Run(
            $$"""
            public sealed class Handler(ICurrentUser currentUser)
            {
                public object Handle() => {{question}};
            }
            """).CompilationErrors.Should().Contain(diagnostic => diagnostic.Id == error, "a question only the database answers cannot be called where there is no rule");

    [Fact]
    public void A_handler_that_does_not_even_import_the_namespace_gets_the_plain_error_on_its_current_user()
        => GeneratorTestHost.Create(
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;

            namespace Elsewhere;

            [EntityId<Guid>]
            public readonly partial record struct OrderId;

            public sealed class Handler(DDDToolkit.Abstractions.Access.ICurrentUser currentUser)
            {
                public object Handle() => currentUser.Sees<OrderId>();
            }
            """).RunCore().CompilationErrors.Should().Contain(diagnostic => diagnostic.Id == "CS1061");

    [Fact]
    public void A_rule_that_takes_the_current_user_is_DDD00038_with_the_signature_it_needs()
        => Run(
            """
            [RowAccess<Order>(RowOperations.Read)]
            public static partial class TheRule
            {
                public static bool Allows(Order order, ICurrentUser user) => user.IsAuthenticated;
            }
            """).ShouldHaveDiagnostic("DDD00038", at: "Allows").GetMessage().Should().Contain("'public static bool Allows(Order order, IRowUser user)'");
}
