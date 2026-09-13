using DDDToolkit.Exceptions;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Exceptions;

/// <summary>
/// The typed conflict that HANDOFF 2.7 asked for: a stale <c>Version</c> must surface as something an
/// API layer can map to a 409, not as a bare <c>DbUpdateException</c>. These pin the message and the
/// properties a caller reads to build that response.
/// </summary>
public class ConcurrencyConflictExceptionTests
{
    [Fact]
    public void CarriesTheAggregateTypeAndId()
    {
        var id = BasketId.CreateUnique();

        var exception = new ConcurrencyConflictException(typeof(Basket), id);

        exception.AggregateType.Should().Be<Basket>();
        exception.AggregateId.Should().Be(id);
        exception.Message.Should().Be($"The Basket '{id}' was modified by another operation since it was loaded.");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void TheMessageNamesTheIdInItsPrefixedForm()
    {
        // The id's own ToString is what reaches the message, so a prefixed id stays recognisable in a log.
        var id = BasketId.Create(Guid.Parse("0194f0a0-1111-7000-8000-000000000001"));

        var exception = new ConcurrencyConflictException(typeof(Basket), id);

        exception.Message.Should().Contain("BSK_0194f0a0-1111-7000-8000-000000000001");
    }

    [Fact]
    public void FallsBackWhenTheIdIsUnknown()
    {
        var exception = new ConcurrencyConflictException(typeof(Basket), aggregateId: null);

        exception.Message.Should().Be("The Basket was modified by another operation since it was loaded.");
        exception.AggregateId.Should().BeNull();
        exception.AggregateType.Should().Be<Basket>();
    }

    [Fact]
    public void FallsBackWhenNothingIsKnown()
    {
        var exception = new ConcurrencyConflictException(aggregateType: null, aggregateId: null);

        exception.Message.Should().Be("The aggregate was modified by another operation since it was loaded.");
        exception.AggregateType.Should().BeNull();
        exception.AggregateId.Should().BeNull();
    }

    [Fact]
    public void WrapsTheProviderException()
    {
        var inner = new InvalidOperationException("row version mismatch");

        var exception = new ConcurrencyConflictException(typeof(Basket), BasketId.CreateUnique(), inner);

        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void TheMessageConstructorLeavesTheMetadataUnset()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new ConcurrencyConflictException("something else went stale", inner);

        exception.Message.Should().Be("something else went stale");
        exception.InnerException.Should().BeSameAs(inner);
        exception.AggregateType.Should().BeNull();
        exception.AggregateId.Should().BeNull();
    }

    [Fact]
    public void IsCatchableAsAToolkitException()
    {
        var exception = new ConcurrencyConflictException(typeof(Basket), BasketId.CreateUnique());

        exception.Should().BeAssignableTo<DDDToolkitException>()
            .And.BeAssignableTo<Exception>();
    }

    [Fact]
    public void InvalidValueObjectExceptionNamesTheType()
    {
        var exception = new InvalidValueObjectException(typeof(Basket));

        exception.Message.Should().Be("The value object Basket is invalid.");
        exception.Should().BeAssignableTo<DDDToolkitException>();
    }
}
