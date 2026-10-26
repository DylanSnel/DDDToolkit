using System.Collections.Concurrent;
using System.Diagnostics;
using GreenDonut;
using HotChocolate.Fetching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.HotChocolate.Tests.Infrastructure;

/// <summary>
/// Sends a data loader's batch when it is whole: when it holds as many keys as the test says the answer has
/// parents, the items of a list or the rows of a page for one. For a test that counts what a batch costs.
/// </summary>
/// <remarks>
/// HotChocolate's own dispatcher goes by the clock: a batch leaves once no key was added to it for a moment. On a
/// busy machine the fields of one list are resolved further apart than that, and their keys leave as two batches.
/// The answers are the same and the cost is not, so a test that counts questions or statements under that
/// dispatcher counts how busy the machine was. Here time decides nothing: a batch waits for its keys, however long
/// they take.
/// <para>
/// What a test proves with it is what a loader promises: the keys of one batch cost one question. That
/// HotChocolate gathers a list into one batch is its own business, and usually so.
/// </para>
/// <para>
/// A batch that does not get whole is sent as it is once <see cref="Patience"/> ran out, so a request with a
/// wrong number ends instead of hanging: the test is judged on what it counted, and its length says that a
/// batch was waited for in vain.
/// </para>
/// <para>
/// This file is compiled into <c>DDDToolkit.HotChocolate.Fusion.InMemory.Tests</c> and into
/// <c>Examples.Tenancy.Tests</c> as well: a loader behind the in-memory gateway and the loaders of the Tenancy
/// sample are counted the same way, and there is one dispatcher to keep true to HotChocolate.
/// </para>
/// <code>
/// var batches = new WholeBatches();
/// var schema = services.AddGraphQL().AddQueryType&lt;Query&gt;();   // or AddGraphQLServer("name"), for each schema
/// batches.AddTo(services);                                      // after the last of them
/// batches.WholeAt(4);                                           // the list that is asked for next has four items
/// </code>
/// </remarks>
public sealed class WholeBatches : IBatchDispatcher
{
    /// <summary>How long a batch is waited for before it is sent short.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Type, int> _keysByKind = new();
    private readonly ConcurrentQueue<(Type? Kind, int Keys)> _sent = new();
    private volatile int _keys = 1;

    /// <summary>
    /// Every batch sent so far, as the number of keys it held when it was sent on its way, in the order they
    /// went. A test that finds its own numbers here knows its batches went through this dispatcher, and whole.
    /// </summary>
    /// <remarks>
    /// A batch that was whole has that number. One that was sent before all its keys had come, with the first of
    /// them where no number was named, may have left with a key more than is noted: a loader closes a batch a
    /// moment after it is sent on its way.
    /// </remarks>
    public IReadOnlyList<int> Sent => [.. _sent.Select(batch => batch.Keys)];

    /// <summary>
    /// What <see cref="Sent"/> says of the batches whose keys are <typeparamref name="TKey"/> alone: for an
    /// answer several loaders work on, where a test knows the numbers of its own and not of the others.
    /// </summary>
    public IReadOnlyList<int> SentOf<TKey>()
        where TKey : notnull
        => [.. _sent.Where(batch => batch.Kind == typeof(TKey)).Select(batch => batch.Keys)];

    /// <summary>
    /// From now on a batch is whole, and sent, when it holds <paramref name="keys"/> keys. Until a test says
    /// otherwise that is one: a batch leaves with its first key.
    /// </summary>
    public void WholeAt(int keys)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keys, 1);
        _keys = keys;
    }

    /// <summary>
    /// From now on a batch of a loader whose keys are <typeparamref name="TKey"/> is whole when it holds
    /// <paramref name="keys"/> keys, whatever <see cref="WholeAt(int)"/> says for the others: for an answer whose
    /// loaders are each asked about another number of things, the seats and the roles of a list of crews for one.
    /// </summary>
    /// <remarks>
    /// A batch says nothing of its loader but the type of its keys, so two loaders with keys of one type share the
    /// number.
    /// </remarks>
    public void WholeAt<TKey>(int keys)
        where TKey : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keys, 1);
        _keysByKind[typeof(TKey)] = keys;
    }

    /// <summary>
    /// Makes this the dispatcher of every request <paramref name="services"/> serve: HotChocolate hands a request's
    /// loaders the one the request's services give. Call it after the last <c>AddGraphQL</c> or
    /// <c>AddGraphQLServer</c> on these services, since each of those puts HotChocolate's own dispatcher back.
    /// </summary>
    public void AddTo(IServiceCollection services)
    {
        services.RemoveAll<IBatchDispatcher>();
        services.AddScoped<IBatchDispatcher>(_ => this);
    }

    /// <inheritdoc />
    public void Schedule(Batch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // A loader schedules while it holds its lock, so the waiting happens elsewhere.
        _ = Task.Run(() => SendWhenWholeAsync(batch));
    }

    /// <inheritdoc />
    public void BeginDispatch(CancellationToken cancellationToken = default)
    {
        // Nothing to hurry: a batch leaves when it is whole.
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IObserver<BatchDispatchEventArgs> observer) => Unsubscribed.Instance;

    /// <inheritdoc />
    public void Dispose()
    {
        // One instance serves every request of the services, so a request that ends leaves it as it is.
    }

    private async Task SendWhenWholeAsync(Batch batch)
    {
        var waited = Stopwatch.StartNew();
        while (batch.Size < KeysToBeWhole(batch) && waited.Elapsed < Patience)
        {
            await Task.Delay(1);
        }

        // Noted before it is sent: a loader takes a batch back once it is answered, and empties it.
        _sent.Enqueue((KindOf(batch), batch.Size));

        try
        {
            await batch.DispatchAsync();
        }
        catch (Exception)
        {
            // What a fetch throws is its keys' answer, which the loader hands to whoever asked.
        }
    }

    /// <summary>The number of keys <paramref name="batch"/> is whole at: the one named for its kind of key, or the one for every batch.</summary>
    private int KeysToBeWhole(Batch batch)
        => KindOf(batch) is { } kind && _keysByKind.TryGetValue(kind, out var keys) ? keys : _keys;

    /// <summary>
    /// The type of the keys of <paramref name="batch"/>: all a batch says of its loader. GreenDonut's batch is
    /// generic in it and not public, so it is read from the type's own argument.
    /// </summary>
    private static Type? KindOf(Batch batch)
        => batch.GetType() is { IsGenericType: true, GenericTypeArguments: [var kind] } ? kind : null;

    private sealed class Unsubscribed : IDisposable
    {
        public static readonly Unsubscribed Instance = new();

        public void Dispose()
        {
        }
    }
}
