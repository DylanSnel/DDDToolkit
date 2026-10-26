using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Owners;

/// <summary>
/// Where the owner of each kind of aggregate is read, for the host: the context that maps the aggregate, and the column
/// of the property it marks <c>[Owner]</c>. Found the first time a request about the aggregate's owner is checked, from
/// the models of the contexts the host registered, and kept.
/// </summary>
/// <remarks>
/// Nothing is named by a string and nothing is configured: the aggregate is the root class of the models that derives
/// from <see cref="AggregateRoot{TIdObject}"/> over the id, and its owner is the property it marks, which is the
/// domain's own word for it. What is not as the check needs it fails the request rather than letting it through: no
/// context maps such an aggregate, two do, the aggregate marks no owner or two, or its context does not map the property.
/// </remarks>
internal sealed class OwnerColumns
{
    private readonly ConcurrentDictionary<Type, object> _found = new();

    /// <summary>The column the owner of an aggregate whose id is <typeparamref name="TId"/> is read from.</summary>
    /// <typeparam name="TId">The aggregate's id.</typeparam>
    /// <param name="services">The services of the scope that asks, where the contexts are.</param>
    /// <exception cref="InvalidOperationException">The owner of such an aggregate cannot be read from the host's contexts.</exception>
    public OwnerColumn<TId> For<TId>(IServiceProvider services)
        where TId : struct, IEntityId, IEquatable<TId>
    {
        if (_found.TryGetValue(typeof(TId), out var known))
        {
            return (OwnerColumn<TId>)known;
        }

        // Found again by a second request that asked at the same moment, which finds the same: either is kept.
        return (OwnerColumn<TId>)_found.GetOrAdd(typeof(TId), Find<TId>(services));
    }

    private static OwnerColumn<TId> Find<TId>(IServiceProvider services)
        where TId : struct, IEntityId, IEquatable<TId>
    {
        var id = typeof(TId).Name;
        var mapped = new List<(Type Context, IEntityType Aggregate)>();
        foreach (var contextType in EntityFrameworkChecks.RegisteredContexts(services))
        {
            if (ModelOf(contextType, services) is not { } model)
            {
                continue;
            }

            // The root of what the model maps over the id: an entity type of a hierarchy is read through its root.
            mapped.AddRange(model.GetEntityTypes()
                .Where(entity => entity.BaseType is null && !entity.IsOwned() && typeof(AggregateRoot<TId>).IsAssignableFrom(entity.ClrType))
                .Select(entity => (contextType, entity)));
        }

        if (mapped.Count == 0)
        {
            throw new InvalidOperationException(
                $"No context the host registered maps an aggregate whose id is {id}, so its owner cannot be read for a request about it. "
                + $"Register the context that maps it, with AddDbContext, a context pool or a context factory, or register an IAggregateOwners<{id}> that reads it where it is kept.");
        }

        if (mapped.Count > 1)
        {
            throw new InvalidOperationException(
                $"An aggregate whose id is {id} is mapped more than once, {Listed(mapped.Select(each => "'" + each.Aggregate.ClrType.Name + "' by '" + each.Context.Name + "'"))}, and which of them its owner is read from cannot be told. "
                + $"Map it in one context, or register an IAggregateOwners<{id}> that reads it from the one it is kept in.");
        }

        var (context, aggregate) = mapped[0];
        var name = aggregate.ClrType.Name;
        var owners = OwnersOf(aggregate.ClrType);
        if (owners.Count != 1)
        {
            throw new InvalidOperationException(owners.Count == 0
                ? $"'{name}', whose id is {id}, marks no property [Owner], so a request about its owner cannot be checked. Mark the property that names its owner [Owner]."
                : $"'{name}' marks {Listed(owners.Select(each => "'" + each.Name + "'"))} [Owner], and an aggregate has one owner (DDD00087). Several owners are members: a resource with members, which Membership keeps.");
        }

        var owner = owners[0];
        var ownerType = Nullable.GetUnderlyingType(owner.PropertyType) ?? owner.PropertyType;
        if (!ownerType.IsValueType || ownerType.IsEnum || !typeof(IEquatable<>).MakeGenericType(ownerType).IsAssignableFrom(ownerType))
        {
            throw new InvalidOperationException(
                $"'{name}.{owner.Name}', its [Owner], is a '{ownerType.Name}', which nothing can say the caller's own of: the caller's own is a value compared with the caller's, a struct that is IEquatable of itself, such as a Guid or an id declared with [EntityId] (DDD00088).");
        }

        if (aggregate.FindProperty(owner.Name) is not { } column || column.IsShadowProperty())
        {
            throw new InvalidOperationException(
                $"'{context.Name}' maps '{name}' without its [Owner] property '{owner.Name}', so the owner cannot be read from its rows. Map the property, as a column of the aggregate's table.");
        }

        return (OwnerColumn<TId>)Activator.CreateInstance(
            typeof(OwnerColumn<,,,>).MakeGenericType(typeof(TId), context, aggregate.ClrType, ownerType),
            owner,
            ReadsItsContext(aggregate))!;
    }

    /// <summary>
    /// The model of <paramref name="contextType"/>: of a context of its own where the host registered a factory for it,
    /// disposed when the model is read, and otherwise of the scope's; <see langword="null"/> where neither is registered.
    /// </summary>
    private static IModel? ModelOf(Type contextType, IServiceProvider services)
    {
        var factoryType = typeof(IDbContextFactory<>).MakeGenericType(contextType);
        if (services.GetService(factoryType) is { } factory)
        {
            using var made = (DbContext)factoryType.GetMethod(nameof(IDbContextFactory<DbContext>.CreateDbContext))!.Invoke(factory, null)!;
            return made.Model;
        }

        return (services.GetService(contextType) as DbContext)?.Model;
    }

    /// <summary>
    /// The properties of <paramref name="aggregate"/> marked <c>[Owner]</c>, its own and those of the classes it derives
    /// from, public or not, each once, the nearest first.
    /// </summary>
    private static List<PropertyInfo> OwnersOf(Type aggregate)
    {
        var found = new List<PropertyInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = aggregate; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length == 0 && seen.Add(property.Name) && property.IsDefined(typeof(OwnerAttribute), inherit: false))
                {
                    found.Add(property);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Whether <paramref name="aggregate"/> has a query filter that reads a member of the context a query runs on: the
    /// application's rule as that one context knows it, which a context a factory makes for one reading knows nothing of.
    /// </summary>
    private static bool ReadsItsContext(IEntityType aggregate)
        => aggregate.GetDeclaredQueryFilters().Any(static filter => filter.Expression is { } expression && ContextReader.Reads(expression));

    private static string Listed(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count <= 1 ? string.Concat(all) : string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1];
    }

    /// <summary>
    /// Finds, in a query filter, a property, a field or a method of a context being read: what makes the filter that
    /// context's own. A context that is only handed on is not read.
    /// </summary>
    private sealed class ContextReader : ExpressionVisitor
    {
        private bool _found;

        public static bool Reads(Expression filter)
        {
            var reader = new ContextReader();
            reader.Visit(filter);
            return reader._found;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _found |= IsAContext(node.Expression);
            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            _found |= IsAContext(node.Object);
            return base.VisitMethodCall(node);
        }

        private static bool IsAContext(Expression? read) => read is not null && typeof(DbContext).IsAssignableFrom(read.Type);
    }
}

/// <summary>The column the owner of an aggregate whose id is <typeparamref name="TId"/> is read from, and how.</summary>
/// <typeparam name="TId">The aggregate's id.</typeparam>
internal abstract class OwnerColumn<TId>
    where TId : struct, IEntityId, IEquatable<TId>
{
    /// <summary>The owner's type, without its <c>?</c>.</summary>
    public abstract Type OwnerType { get; }

    /// <summary>The owner of one aggregate, read as the caller in one statement, or <see langword="null"/> for no row.</summary>
    /// <param name="services">The services of the scope that asks.</param>
    /// <param name="aggregate">The aggregate's id.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    public abstract ValueTask<AggregateOwner?> ReadAsync(IServiceProvider services, TId aggregate, CancellationToken cancellationToken);
}

/// <summary>
/// The owner of <typeparamref name="TAggregate"/>, kept in <typeparamref name="TContext"/>: the one column of the one row,
/// read in one statement.
/// </summary>
/// <remarks>
/// The statement runs on a context of its own where the host registered a factory for the context, since the requests of
/// one scope may be checked side by side; otherwise on the scope's own. Where the aggregate's query filter reads a member
/// of the context, it runs on the scope's own wherever the scope has one, as Membership's questions do: that rule is the
/// request's context's. Either way it runs as the caller wherever the host runs its queries so, and a row the policies or
/// the filter keep from the caller is no row.
/// </remarks>
/// <typeparam name="TId">The aggregate's id.</typeparam>
/// <typeparam name="TContext">The context that maps it.</typeparam>
/// <typeparam name="TAggregate">The aggregate.</typeparam>
/// <typeparam name="TOwner">The type of its owner, without its <c>?</c>.</typeparam>
internal sealed class OwnerColumn<TId, TContext, TAggregate, TOwner> : OwnerColumn<TId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TContext : DbContext
    where TAggregate : AggregateRoot<TId>
    where TOwner : struct, IEquatable<TOwner>
{
    private readonly Expression<Func<TAggregate, TOwner?>> _owner;
    private readonly bool _followsTheRequest;

    /// <summary>The column of <paramref name="owner"/>.</summary>
    /// <param name="owner">The property the aggregate marks <c>[Owner]</c>.</param>
    /// <param name="followsTheRequest">Whether the aggregate's query filter reads a member of its context.</param>
    public OwnerColumn(PropertyInfo owner, bool followsTheRequest)
    {
        // candidate => (TOwner?)candidate.Owner, the property as the domain declares it, whatever its accessibility.
        var candidate = Expression.Parameter(typeof(TAggregate), "candidate");
        Expression read = Expression.Property(candidate, owner);
        if (read.Type != typeof(TOwner?))
        {
            read = Expression.Convert(read, typeof(TOwner?));
        }

        _owner = Expression.Lambda<Func<TAggregate, TOwner?>>(read, candidate);
        _followsTheRequest = followsTheRequest;
    }

    /// <inheritdoc />
    public override Type OwnerType => typeof(TOwner);

    /// <inheritdoc />
    public override async ValueTask<AggregateOwner?> ReadAsync(IServiceProvider services, TId aggregate, CancellationToken cancellationToken)
    {
        // Whether there is a row is told apart from whose it is: none read is no row, and a null read a row of nobody's.
        var owners = await ReadOnAsync(
            services,
            context => context.Set<TAggregate>().AsNoTracking()
                .Where(candidate => candidate.Id.Equals(aggregate))
                .Select(_owner)
                .Take(1)
                .ToListAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return owners.Count == 0 ? null
            : owners[0] is { } owner ? new AggregateOwner(owner)
            : AggregateOwner.Nobody;
    }

    private async Task<T> ReadOnAsync<T>(IServiceProvider services, Func<TContext, Task<T>> read, CancellationToken cancellationToken)
    {
        if (services.GetService<IDbContextFactory<TContext>>() is not { } contexts)
        {
            return await read(services.GetRequiredService<TContext>()).ConfigureAwait(false);
        }

        if (_followsTheRequest && services.GetService<TContext>() is { } own)
        {
            return await read(own).ConfigureAwait(false);
        }

        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await read(context).ConfigureAwait(false);
        }
    }
}
