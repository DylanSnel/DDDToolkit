using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Mediator.Tests.Domain;
using Mediator;

namespace DDDToolkit.Mediator.Tests.Requests;

/// <summary>
/// What every request about a basket implements, to say what it requires of its caller. This project runs the
/// toolkit's generator and Mediator's own, so the behavior the toolkit writes for this interface,
/// <c>BasketAccessBehavior&lt;TMessage, TResponse&gt;</c>, is written into the very compilation Mediator's
/// generator writes its mediator into: the single-project case.
/// </summary>
[AccessRequests]
public interface IBasketRequest : IRequireAccess;

/// <summary>The caller owns the basket. The one case of the test module.</summary>
/// <param name="Basket">The basket, from the request.</param>
public sealed record OwnsBasket(BasketId Basket) : AccessRequirement;

/// <summary>What the handlers, the check and the behaviors of a test did, in order.</summary>
public sealed class StepLog
{
    private readonly List<string> _taken = [];

    /// <summary>The baskets the caller of the test owns.</summary>
    public HashSet<BasketId> Owned { get; } = [];

    /// <summary>The request in hand where each handler ran, and what the check kept of a basket there.</summary>
    public List<(object? Request, BasketId? Kept)> InHand { get; } = [];

    /// <summary>Notes what is in hand where a handler runs.</summary>
    public void NoteInHand()
    {
        lock (InHand)
        {
            InHand.Add((RequestInHand.Current, Checked<BasketId>.TryFindInHand(out var kept) ? kept : null));
        }
    }

    /// <summary>The steps so far, in order.</summary>
    public IReadOnlyList<string> InOrder
    {
        get
        {
            lock (_taken)
            {
                return [.. _taken];
            }
        }
    }

    /// <summary>Notes a step.</summary>
    public void Add(string step)
    {
        lock (_taken)
        {
            _taken.Add(step);
        }
    }
}

/// <summary>Decides <see cref="OwnsBasket"/>, and keeps the basket it checked for the handler.</summary>
public sealed class BasketAccessCheck(StepLog steps, Checked<BasketId> checkedBasket) : IAccessCheck
{
    /// <summary>The code a caller that does not own the basket is refused with.</summary>
    public const string NotYours = "baskets.not-yours";

    public bool Decides(AccessRequirement requirement) => requirement is OwnsBasket;

    public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        var basket = ((OwnsBasket)requirement).Basket;
        steps.Add("check");
        if (!steps.Owned.Contains(basket))
        {
            throw new RefusalException(NotYours, RefusalKind.NotPermitted, "The basket is not the caller's.");
        }

        checkedBasket.KeepFor(request, basket);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Renames a basket of the caller's own.</summary>
public sealed record RenameBasket(BasketId Basket, string Name) : ICommand<BasketId>, IBasketRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new OwnsBasket(Basket);
}

/// <summary>Answers with the basket the check kept for the command: what it would load and rename.</summary>
public sealed class RenameBasketHandler(StepLog steps, Checked<BasketId> checkedBasket) : ICommandHandler<RenameBasket, BasketId>
{
    public ValueTask<BasketId> Handle(RenameBasket command, CancellationToken cancellationToken)
    {
        steps.Add("handler");
        steps.NoteInHand();
        return ValueTask.FromResult(checkedBasket.TakeFor(command));
    }
}

/// <summary>How many kinds of basket there are: a request of the module that anyone may send.</summary>
public sealed record BasketKinds : IQuery<int>, IBasketRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.AllowAnonymous();
}

/// <summary>Answers <see cref="BasketKinds"/>.</summary>
public sealed class BasketKindsHandler(StepLog steps) : IQueryHandler<BasketKinds, int>
{
    public ValueTask<int> Handle(BasketKinds query, CancellationToken cancellationToken)
    {
        steps.Add("handler");
        return ValueTask.FromResult(3);
    }
}

/// <summary>
/// What is in a basket of the caller's own, one line at a time: a query of the module that is answered with a
/// stream. Mediator sends it through a pipeline of its own, which no <c>IPipelineBehavior</c> is part of, so the
/// toolkit writes a second behavior for the interface, <c>BasketAccessStreamBehavior&lt;TMessage, TResponse&gt;</c>.
/// </summary>
public sealed record BasketLines(BasketId Basket) : IStreamQuery<string>, IBasketRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new OwnsBasket(Basket);
}

/// <summary>Answers <see cref="BasketLines"/> with two lines.</summary>
public sealed class BasketLinesHandler(StepLog steps) : IStreamQueryHandler<BasketLines, string>
{
    public async IAsyncEnumerable<string> Handle(BasketLines query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        steps.Add("handler");

        // Before the first line: each line after it is asked for by whoever reads the stream, in that reader's flow.
        steps.NoteInHand();
        yield return "bread";
        await Task.Yield();
        yield return "milk";
    }
}

/// <summary>A request of the module that declares a case no check of the module decides.</summary>
public sealed record AuditBaskets : ICommand, IBasketRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new Audits();

    /// <summary>A case nobody registered a check for.</summary>
    public sealed record Audits : AccessRequirement;
}

/// <summary>Handles <see cref="AuditBaskets"/>, which never gets this far.</summary>
public sealed class AuditBasketsHandler(StepLog steps) : ICommandHandler<AuditBaskets>
{
    public ValueTask<Unit> Handle(AuditBaskets command, CancellationToken cancellationToken)
    {
        steps.Add("handler");
        return ValueTask.FromResult(Unit.Value);
    }
}

/// <summary>A request that is no module's: it implements no request interface, so no access behavior is its.</summary>
public sealed record Ping : ICommand;

/// <summary>Handles <see cref="Ping"/>.</summary>
public sealed class PingHandler(StepLog steps) : ICommandHandler<Ping>
{
    public ValueTask<Unit> Handle(Ping command, CancellationToken cancellationToken)
    {
        steps.Add("handler");
        return ValueTask.FromResult(Unit.Value);
    }
}

/// <summary>
/// A behavior of the host's own, which the host names in <c>MediatorOptions.PipelineBehaviors</c> for Mediator's
/// generator to register. That generator sees it because somebody wrote it: it cannot see the behavior the
/// toolkit's generator writes, so that one is added to the container with its own registration instead.
/// </summary>
public sealed class Listed<TMessage, TResponse>(StepLog steps) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        steps.Add("listed");
        return await next(message, cancellationToken);
    }
}

/// <summary>
/// What every request of a second module implements, whose behaviors the host lists for Mediator's generator, as a
/// host may for a module in a class library. It is not marked <c>[AccessRequests]</c>: its behaviors are written here
/// by hand, constrained as the generated ones are, and the test assembly describes them as the toolkit's generator
/// describes the ones it writes. The module has no query answered with a stream.
/// </summary>
public interface IStockRequest : IRequireAccess;

/// <summary>Counts the stock: the one request of the second module.</summary>
public sealed record TakeStock : ICommand, IStockRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => new AccessRequirement.Open("Anybody may count the stock.");
}

/// <summary>Handles <see cref="TakeStock"/>.</summary>
public sealed class TakeStockHandler(StepLog steps) : ICommandHandler<TakeStock>
{
    public ValueTask<Unit> Handle(TakeStock command, CancellationToken cancellationToken)
    {
        steps.Add("handler");
        return ValueTask.FromResult(Unit.Value);
    }
}

/// <summary>The second module's behavior, listed for Mediator's generator, which closes it over each message of the module.</summary>
public sealed class StockAccess<TMessage, TResponse>(StepLog steps) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IStockRequest, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        steps.Add("stock access");
        return await next(message, cancellationToken);
    }
}

/// <summary>
/// The second module's behavior for streams, listed for Mediator's generator as well, which closes it over nothing:
/// the module has no query answered with a stream.
/// </summary>
public sealed class StockStreamAccess<TMessage, TResponse>(StepLog steps) : IStreamPipelineBehavior<TMessage, TResponse>
    where TMessage : IStockRequest, IStreamMessage
{
    public IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        steps.Add("stock access");
        return next(message, cancellationToken);
    }
}
