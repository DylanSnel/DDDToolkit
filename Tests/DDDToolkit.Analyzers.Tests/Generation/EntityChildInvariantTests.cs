using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using DDDToolkit.Invariants;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// An aggregate root answers for its children. Asking a root whether it is consistent that skipped
/// what it holds would answer for half of it, and the root is the consistency boundary: answering for
/// the boundary means answering for what is inside it.
/// <para>
/// This is a domain question, so every test here works on an aggregate in memory with no persistence
/// anywhere near it. The self-only pair is tested alongside, because it is what makes the walk safe:
/// the save enumerates the graph itself and asks each object for its own rules, so it never hears
/// about one child twice.
/// </para>
/// </summary>
public class EntityChildInvariantTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Invariants;
        using System.Collections.Generic;

        namespace Sample;

        [EntityId<int>]
        public readonly partial record struct OrderId;

        [EntityId<int>]
        public readonly partial record struct LineId;


        """;

    /// <summary>
    /// An order with lines. The root states no rule of its own by default, so anything the tests see
    /// reported has come from a line.
    /// </summary>
    private const string OrderAndLine =
        """
        [AggregateRoot<OrderId>]
        public partial class Order
        {
            public Order(OrderId id) : base(id) { }

            public partial IReadOnlyList<Line> Lines { get; }

            public Line Add(LineId id, int quantity)
            {
                var line = new Line(id, quantity);
                _lines.Add(line);
                return line;
            }
        }

        [Entity<LineId>]
        public partial class Line
        {
            public Line(LineId id, int quantity) : base(id) => _quantity = quantity;

            private readonly int _quantity;

            public sealed class MustOrderSomething : IInvariant<Line>
            {
                public string Code => "line.quantity";

                public string? Check(Line entity) => entity._quantity > 0 ? null : "A line must order at least one of something.";
            }
        }
        """;

    /// <summary>
    /// An order carrying one line per quantity, as the running object the tests ask. It takes the
    /// emitted assembly rather than the run, because every <c>Emit()</c> loads an assembly of its own
    /// and two ids from two of them are two unrelated types.
    /// </summary>
    private static IHasInvariants Order(EmittedAssembly emitted, params int[] quantities)
    {
        var order = emitted.New("Sample.Order", emitted.New("Sample.OrderId", 1));

        for (var index = 0; index < quantities.Length; index++)
        {
            emitted.Call(order, "Add", emitted.New("Sample.LineId", index + 1), quantities[index]);
        }

        return (IHasInvariants)order;
    }

    // ------------------------------------------------------------------ the root answers for its children

    [Fact]
    public void A_root_reports_what_its_child_broke_and_says_which_child_it_was()
    {
        var result = GeneratorTestHost.Create(Preamble + OrderAndLine).RunCore();

        result.ShouldCompile();

        var emitted = result.Emit();

        Order(emitted, 1, 2).GetInvariantViolations().Should().BeEmpty("every line orders something");

        var violation = Order(emitted, 1, 0).GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be("line.quantity");
        violation.EntityType!.FullName.Should().Be("Sample.Line", "the root answers, but the line is the problem");
        violation.EntityId.Should().Be(emitted.New("Sample.LineId", 2), "and it is the second line, not the first");
    }

    [Fact]
    public void A_root_throws_for_what_its_child_broke_and_the_exception_names_the_child()
    {
        var result = GeneratorTestHost.Create(Preamble + OrderAndLine).RunCore();
        var emitted = result.Emit();

        Order(emitted, 1).Invoking(order => order.EnsureInvariants()).Should().NotThrow();

        var exception = Order(emitted, 0).Invoking(order => order.EnsureInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.AggregateType!.FullName.Should().Be("Sample.Order", "the boundary that was asked is the one that answers");
        exception.Violations.Should().ContainSingle()
            .Which.Should().Contain("Line").And.Contain("line.quantity").And.Contain("at least one of something");
    }

    [Fact]
    public void A_rule_of_the_root_and_a_rule_of_a_child_come_back_from_one_call()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Line> Lines { get; }

                public Line Add(LineId id, int quantity)
                {
                    var line = new Line(id, quantity);
                    _lines.Add(line);
                    return line;
                }

                public sealed class MustHaveLines : IInvariant<Order>
                {
                    public string Code => "order.lines";

                    public string? Check(Order entity) => entity._lines.Count > 0 ? null : "An order must have a line.";
                }
            }

            [Entity<LineId>]
            public partial class Line
            {
                public Line(LineId id, int quantity) : base(id) => _quantity = quantity;

                private readonly int _quantity;

                public sealed class MustOrderSomething : IInvariant<Line>
                {
                    public string Code => "line.quantity";

                    public string? Check(Line entity) => entity._quantity > 0 ? null : "A line must order at least one of something.";
                }
            }
            """).RunCore();

        result.ShouldCompile();

        var emitted = result.Emit();
        var violations = Order(emitted, 0, 0).GetInvariantViolations();

        violations.Should().HaveCount(2, "both lines are wrong, and the root itself is not");
        violations.Should().OnlyContain(violation => violation.Code == "line.quantity");
        violations.Select(violation => violation.EntityId).Should().OnlyHaveUniqueItems("each line names itself");

        // And with no lines at all it is the root's own rule that fails, reported by the root.
        var own = Order(emitted).GetInvariantViolations().Should().ContainSingle().Which;

        own.Code.Should().Be("order.lines");
        own.EntityType!.FullName.Should().Be("Sample.Order");
    }

    [Fact]
    public void The_roots_own_violations_come_before_its_childrens()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Line> Lines { get; }

                public Line Add(LineId id, int quantity)
                {
                    var line = new Line(id, quantity);
                    _lines.Add(line);
                    return line;
                }

                partial void CheckInvariants() => throw InvariantViolation("This order is never right.");
            }

            [Entity<LineId>]
            public partial class Line
            {
                public Line(LineId id, int quantity) : base(id) => _quantity = quantity;

                private readonly int _quantity;

                public sealed class MustOrderSomething : IInvariant<Line>
                {
                    public string Code => "line.quantity";

                    public string? Check(Line entity) => entity._quantity > 0 ? null : "A line must order at least one of something.";
                }
            }
            """).RunCore();

        result.ShouldCompile();

        var emitted = result.Emit();

        Order(emitted, 0).GetInvariantViolations()
            .Select(violation => violation.EntityType!.Name).Should().Equal("Order", "Line");

        // The exception phrases a child's violation with the child's own name and leaves the root's
        // alone, because the exception already says which order it is about.
        var exception = Order(emitted, 0).Invoking(order => order.EnsureInvariants())
            .Should().Throw<InvariantViolationException>().Which;

        exception.Violations.Should().HaveCount(2);
        exception.Violations[0].Should().Be("This order is never right.", "the exception already names the order");
        exception.Violations[1].Should().StartWith("Line ", "and nothing else says which line it was");
        exception.InnerException.Should().BeOfType<InvariantViolationException>("the seam's own throw is kept");
    }

    // ------------------------------------------------------------------ grandchildren

    [Fact]
    public void A_grandchild_is_reached_because_a_child_answers_the_same_way()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Line> Lines { get; }

                public void Add(Line line) => _lines.Add(line);
            }

            [Entity<LineId>]
            public partial class Line
            {
                public Line(LineId id) : base(id) { }

                public partial IReadOnlyList<Serial> Serials { get; }

                public void Add(Serial serial) => _serials.Add(serial);
            }

            [Entity<LineId>]
            public partial class Serial
            {
                public Serial(LineId id, string code) : base(id) => _code = code;

                private readonly string _code;

                public sealed class MustBeStamped : IInvariant<Serial>
                {
                    public string Code => "serial.stamped";

                    public string? Check(Serial entity) => entity._code.Length > 0 ? null : "A serial number must be stamped.";
                }
            }
            """).RunCore();

        result.ShouldCompile();

        var emitted = result.Emit();
        var order = emitted.New("Sample.Order", emitted.New("Sample.OrderId", 1));
        var line = emitted.New("Sample.Line", emitted.New("Sample.LineId", 1));
        emitted.Call(order, "Add", line);
        emitted.Call(line, "Add", emitted.New("Sample.Serial", emitted.New("Sample.LineId", 9), ""));

        var violation = ((IHasInvariants)order).GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be("serial.stamped");
        violation.EntityType!.FullName.Should().Be("Sample.Serial", "the root never had to know a serial exists");

        // The line in between says the same thing, which is the whole of why the root can.
        ((IHasInvariants)line).GetInvariantViolations().Should().ContainSingle()
            .Which.EntityType!.FullName.Should().Be("Sample.Serial");
    }

    // ------------------------------------------------------------------ the self-only pair

    [Fact]
    public void The_self_only_pair_leaves_the_children_out()
    {
        var result = GeneratorTestHost.Create(Preamble + OrderAndLine).RunCore();

        var order = Order(result.Emit(), 0);

        order.GetInvariantViolations().Should().ContainSingle("the aggregate is asked as a whole");
        order.GetOwnInvariantViolations().Should().BeEmpty("the order itself has broken nothing");
        order.Invoking(subject => subject.EnsureOwnInvariants()).Should().NotThrow("and it is the line that is wrong");
        order.Invoking(subject => subject.EnsureInvariants()).Should().Throw<InvariantViolationException>();
    }

    [Fact]
    public void The_self_only_pair_still_reports_the_objects_own_rules()
    {
        // Leaving the children out is all it does: a broken rule of the object itself comes back from
        // both pairs, or a save that asked only the self-only one would write a broken root.
        var result = GeneratorTestHost.Create(Preamble + OrderAndLine).RunCore();
        var emitted = result.Emit();

        var line = (IHasInvariants)emitted.New("Sample.Line", emitted.New("Sample.LineId", 1), 0);

        line.GetOwnInvariantViolations().Should().ContainSingle().Which.Code.Should().Be("line.quantity");
        line.Invoking(subject => subject.EnsureOwnInvariants()).Should().Throw<InvariantViolationException>();
    }

    // ------------------------------------------------------------------ what is and is not a child

    [Fact]
    public void Only_collections_of_entities_are_walked()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [ValueObject]
            public partial record Note
            {
                public string Text { get; protected init; } = "";
            }

            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Line> Lines { get; }

                public partial IReadOnlyList<Note> Notes { get; }

                public partial IReadOnlySet<string> Labels { get; }
            }

            [Entity<LineId>]
            public partial class Line
            {
                public Line(LineId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Order"), "foreach (var child in _lines)");
        result.ShouldNotContain(Hint.Of("Sample.Order"), "foreach (var child in _notes)", "a value object states no invariants");
        result.ShouldNotContain(Hint.Of("Sample.Order"), "foreach (var child in _labels)", "and neither does a string");
    }

    [Fact]
    public void The_walk_reads_the_backing_field_and_not_the_read_only_view()
    {
        // Lines is a ReadOnlyCollection built on every read, and this runs on every check.
        var result = GeneratorTestHost.Create(Preamble + OrderAndLine).RunCore();

        result.ShouldContain(Hint.Of("Sample.Order"), "foreach (var child in _lines)");
        result.ShouldNotContain(Hint.Of("Sample.Order"), "foreach (var child in Lines)");
    }

    [Fact]
    public void A_child_with_no_rules_of_its_own_costs_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Line> Lines { get; }

                public void Add(LineId id) => _lines.Add(new Line(id));
            }

            [Entity<LineId>]
            public partial class Line
            {
                public Line(LineId id) : base(id) { }
            }
            """).RunCore();

        result.ShouldCompile();

        // The line has neither a rule nor a seam, so its GetInvariantViolations hands back the shared
        // empty array and the root allocates nothing on top of it.
        result.ShouldContain(Hint.Of("Sample.Line"), "return global::System.Array.Empty<global::DDDToolkit.Invariants.InvariantViolation>();");
        result.ShouldNotContain(Hint.Of("Sample.Line"), "__invariants");
        result.ShouldNotContain(Hint.Of("Sample.Line"), "CollectChildInvariantViolations", "a line holds no children either");

        var emitted = result.Emit();
        var order = emitted.New("Sample.Order", emitted.New("Sample.OrderId", 1));
        emitted.Call(order, "Add", emitted.New("Sample.LineId", 1));

        ((IHasInvariants)order).GetInvariantViolations().Should().BeSameAs(
            Array.Empty<InvariantViolation>(),
            "nothing is allocated while everything holds, the children included");
    }

    [Fact]
    public void A_consistent_aggregate_allocates_nothing()
    {
        var result = GeneratorTestHost.Create(Preamble + OrderAndLine).RunCore();
        var source = result.Source(Hint.Of("Sample.Order"));

        // The list is created where the first failure is added, in the walk as in the rules, and the
        // empty answer is the shared array rather than an empty list.
        source.Should().Contain("? violations = null;");
        source.Should().Contain("violations ??= new global::System.Collections.Generic.List<global::DDDToolkit.Invariants.InvariantViolation>();");
        source.Should().Contain("return global::System.Array.Empty<global::DDDToolkit.Invariants.InvariantViolation>();");

        var emitted = result.Emit();

        Order(emitted, 1, 2).GetInvariantViolations().Should().BeSameAs(Array.Empty<InvariantViolation>());
        Order(emitted, 1, 2).GetOwnInvariantViolations().Should().BeSameAs(Array.Empty<InvariantViolation>());
    }

    [Fact]
    public void A_child_from_another_assembly_is_walked_too()
    {
        // The attribute is what identifies a child entity, and an attribute survives the trip through
        // metadata where a declaration does not.
        var result = GeneratorTestHost.Create(Preamble +
            """
            [AggregateRoot<OrderId>]
            public partial class Order
            {
                public Order(OrderId id) : base(id) { }

                public partial IReadOnlyList<Other.Line> Lines { get; }

                public void Add(Other.Line line) => _lines.Add(line);
            }
            """)
            .WithReferencedAssembly(
                """
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Invariants;

                namespace Other;

                [EntityId<int>]
                public readonly partial record struct LineId;

                [Entity<LineId>]
                public partial class Line
                {
                    public Line(LineId id, int quantity) : base(id) => _quantity = quantity;

                    private readonly int _quantity;

                    public sealed class MustOrderSomething : IInvariant<Line>
                    {
                        public string Code => "line.quantity";

                        public string? Check(Line entity) => entity._quantity > 0 ? null : "A line must order at least one of something.";
                    }
                }
                """)
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Sample.Order"), "foreach (var child in _lines)");
    }
}
