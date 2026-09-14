using DDDToolkit.Exceptions;
using FluentAssertions;

namespace DDDToolkit.Tests.Invariants;

/// <summary>The shape of the exception itself: what it carries and how it reads.</summary>
public class InvariantViolationExceptionTests
{
    [Fact]
    public void It_names_the_aggregate_and_the_single_rule()
    {
        var id = TabId.CreateUnique();

        var exception = new InvariantViolationException(typeof(Tab), id, "A tab must be in someone's name.");

        exception.AggregateType.Should().Be<Tab>();
        exception.AggregateId.Should().Be(id);
        exception.Violations.Should().ContainSingle();
        exception.Message.Should().Be($"The Tab '{id}' broke an invariant: A tab must be in someone's name.");
    }

    [Fact]
    public void It_counts_several_rules_and_lists_them()
    {
        var exception = new InvariantViolationException(typeof(Tab), null, ["First.", "Second."]);

        exception.AggregateId.Should().BeNull();
        exception.Violations.Should().Equal("First.", "Second.");
        exception.Message.Should().Be("The Tab broke 2 invariants: First. Second.");
    }

    [Fact]
    public void It_falls_back_to_a_neutral_message_when_nothing_is_known()
    {
        var exception = new InvariantViolationException(null, null, Array.Empty<string>());

        exception.Message.Should().Be("The aggregate broke one of its invariants.");
        exception.Violations.Should().BeEmpty();
    }

    [Fact]
    public void It_accepts_a_message_of_your_own()
    {
        var inner = new InvalidOperationException("cause");

        var exception = new InvariantViolationException("Say it your way.", inner);

        exception.Message.Should().Be("Say it your way.");
        exception.InnerException.Should().BeSameAs(inner);
        exception.Violations.Should().BeEmpty();
        exception.AggregateType.Should().BeNull();
    }

    [Fact]
    public void It_is_a_DDDToolkitException_and_not_a_validation_or_concurrency_failure()
    {
        var exception = new InvariantViolationException(typeof(Tab), null, "Anything.");

        exception.Should().BeAssignableTo<DDDToolkitException>();
        exception.Should().NotBeAssignableTo<ConcurrencyConflictException>();
        exception.Should().NotBeAssignableTo<InvalidValueObjectException>();
    }

    [Fact]
    public void It_rejects_a_null_rule_list()
    {
        var act = () => new InvariantViolationException(typeof(Tab), null, (IEnumerable<string>)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
