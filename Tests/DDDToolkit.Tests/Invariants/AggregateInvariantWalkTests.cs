using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using DDDToolkit.Invariants;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Invariants;

/// <summary>
/// Asking an aggregate root whether it is consistent asks the entities it holds. The root is the
/// consistency boundary, so a handler that acts and then asks "did that break anything" has to be
/// told about the whole boundary and not half of it.
/// <para>
/// Everything here happens in memory. No <c>DbContext</c> is involved, because none is needed: this
/// is a question about the domain, and a persistence concern such as a collection that might not have
/// been loaded is not allowed to shape the answer. The save keeps handling partial graphs separately,
/// through the change tracker, and that is what the self-only pair covered below exists for.
/// </para>
/// </summary>
public class AggregateInvariantWalkTests
{
    private static Tab TabWithLine(decimal price, decimal limit = 100m)
    {
        var tab = new Tab(TabId.CreateUnique(), limit);
        tab.Order(new TabLineId(1), "Negroni", price);
        return tab;
    }

    // ------------------------------------------------------------------ the root answers for its children

    [Fact]
    public void A_root_reports_what_a_child_broke_and_names_the_child()
    {
        var tab = TabWithLine(price: -1m);

        var violation = tab.GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be(InvariantViolation.SeamCode, "the line states its rule in a seam");
        violation.Message.Should().Contain("cannot cost less than nothing");
        violation.EntityType.Should().Be<TabLine>("the tab answers, but the line is what is wrong");
        violation.EntityId.Should().Be(new TabLineId(1), "and a caller can say which line without reading a message");
    }

    [Fact]
    public void A_root_throws_for_what_a_child_broke()
    {
        var tab = TabWithLine(price: -1m);

        var exception = tab.Invoking(t => t.EnsureInvariants()).Should().Throw<InvariantViolationException>().Which;

        exception.AggregateType.Should().Be<Tab>("the boundary that was asked is the one that answers");
        exception.Violations.Should().ContainSingle()
            .Which.Should().Contain("TabLine").And.Contain("cannot cost less than nothing");
    }

    [Fact]
    public void A_root_that_is_whole_says_so_and_a_child_that_is_whole_is_no_objection()
    {
        var tab = TabWithLine(price: 12m);

        tab.GetInvariantViolations().Should().BeEmpty();
        tab.Invoking(t => t.EnsureInvariants()).Should().NotThrow();
    }

    [Fact]
    public void A_rule_of_the_root_and_a_rule_of_a_child_arrive_together_and_in_order()
    {
        var tab = new Tab(TabId.CreateUnique(), limit: 5m);
        tab.Order(new TabLineId(1), "Champagne", 90m);
        tab.Order(new TabLineId(2), "Water", -1m);

        var violations = tab.GetInvariantViolations();

        violations.Select(violation => violation.EntityType).Should().Equal(typeof(Tab), typeof(TabLine));
        violations[0].Message.Should().Contain("may not exceed its limit");
        violations[1].Message.Should().Contain("cannot cost less than nothing");

        // The exception leaves the root's own violation alone, because it already says which tab this
        // is, and phrases the child's with the child's own type and id, because nothing else would.
        var exception = tab.Invoking(t => t.EnsureInvariants()).Should().Throw<InvariantViolationException>().Which;

        exception.Violations[0].Should().StartWith("A tab may not exceed");
        exception.Violations[1].Should().StartWith("TabLine ");
    }

    [Fact]
    public void A_grandchild_is_reached_because_a_child_answers_the_same_way()
    {
        var tab = new Tab(TabId.CreateUnique(), limit: 100m);
        var line = tab.Order(new TabLineId(1), "Negroni", 12m);
        line.Decorate(new GarnishId(1), " ");

        var violation = tab.GetInvariantViolations().Should().ContainSingle().Which;

        violation.Code.Should().Be("garnish.named");
        violation.EntityType.Should().Be<Garnish>("the tab never had to know a garnish exists");
        violation.EntityId.Should().Be(new GarnishId(1));

        // The line in the middle is the whole of why the tab can: it asks its own children too.
        line.GetInvariantViolations().Should().ContainSingle().Which.EntityType.Should().Be<Garnish>();
    }

    // ------------------------------------------------------------------ the self-only pair

    [Fact]
    public void The_self_only_pair_leaves_the_children_out()
    {
        var tab = TabWithLine(price: -1m);

        tab.GetInvariantViolations().Should().ContainSingle("the aggregate is asked as a whole");
        tab.GetOwnInvariantViolations().Should().BeEmpty("the tab itself is within its limit");
        tab.Invoking(t => t.EnsureOwnInvariants()).Should().NotThrow();
        tab.Invoking(t => t.EnsureInvariants()).Should().Throw<InvariantViolationException>();
    }

    [Fact]
    public void The_self_only_pair_still_reports_the_objects_own_rules()
    {
        // Leaving the children out is all it does. A save that asked only this pair, on every object
        // it writes, still refuses every broken one of them.
        var tab = new Tab(TabId.CreateUnique(), limit: 5m);
        tab.Order(new TabLineId(1), "Champagne", 90m);

        tab.GetOwnInvariantViolations().Should().ContainSingle()
            .Which.Message.Should().Contain("may not exceed its limit");
        tab.Invoking(t => t.EnsureOwnInvariants()).Should().Throw<InvariantViolationException>();

        var line = new TabLine(new TabLineId(1), "Water", -1m);

        line.GetOwnInvariantViolations().Should().ContainSingle();
        line.Invoking(l => l.EnsureOwnInvariants()).Should().Throw<InvariantViolationException>();
    }

    [Fact]
    public void Asking_every_object_in_the_graph_for_its_own_rules_reports_each_one_once()
    {
        // What the save does, spelled out: the pair exists so that walking the graph by hand and
        // asking each object separately cannot double-count a child.
        var tab = new Tab(TabId.CreateUnique(), limit: 5m);
        tab.Order(new TabLineId(1), "Champagne", 90m);
        tab.Order(new TabLineId(2), "Water", -1m);

        var walked = new List<IHasInvariants> { tab, tab.Lines[0], tab.Lines[1] }
            .SelectMany(entity => entity.GetOwnInvariantViolations())
            .ToList();

        walked.Should().HaveCount(2);
        walked.Should().BeEquivalentTo(tab.GetInvariantViolations(), "the two routes find exactly the same thing");
    }

    // ------------------------------------------------------------------ what it costs

    [Fact]
    public void A_consistent_aggregate_allocates_nothing()
    {
        // Both stages hand back the shared empty array rather than a list, walk or no walk, so the
        // case that has to be free stays free.
        var tab = TabWithLine(price: 12m);
        tab.Lines[0].Decorate(new GarnishId(1), "orange peel");

        tab.GetInvariantViolations().Should().BeSameAs(Array.Empty<InvariantViolation>());
        tab.GetOwnInvariantViolations().Should().BeSameAs(Array.Empty<InvariantViolation>());
        tab.Lines[0].GetInvariantViolations().Should().BeSameAs(Array.Empty<InvariantViolation>());
    }

    [Fact]
    public void A_child_that_states_no_rules_costs_the_root_nothing()
    {
        // BasketLine has neither a rule nor a seam, so what it hands its basket is the shared empty
        // array and the basket allocates nothing on top of it.
        var basket = new Basket(BasketId.CreateUnique(), "someone");
        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("SKU-1"), 1));

        basket.GetInvariantViolations().Should().BeSameAs(Array.Empty<InvariantViolation>());
        basket.Invoking(b => b.EnsureInvariants()).Should().NotThrow();
    }
}
