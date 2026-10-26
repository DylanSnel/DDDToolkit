using System.Linq.Expressions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Where a caller holds a key above the resources of one kind, as a condition about a resource: the place it
/// sits at is one the application answers. Closed over what a place is known by, which only the model knows.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
internal abstract class PlacesAbove<TResource>
    where TResource : class
{
    /// <summary>
    /// The condition that a resource sits where <paramref name="caller"/> holds <paramref name="key"/>, or
    /// below, for the context a statement runs on: what the application answers, as a subquery of that
    /// statement.
    /// </summary>
    public abstract Func<DbContext, Expression<Func<TResource, bool>>> Reaching(IServiceProvider services, Caller caller, string key);
}

/// <inheritdoc />
/// <param name="at">The resource's property that holds where it sits.</param>
internal sealed class PlacesAbove<TResource, TResourceId, TPlaceId>(string at) : PlacesAbove<TResource>
    where TResource : class
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TPlaceId : struct, IEntityId, IEquatable<TPlaceId>
{
    /// <inheritdoc />
    public override Func<DbContext, Expression<Func<TResource, bool>>> Reaching(IServiceProvider services, Caller caller, string key)
    {
        var above = services.GetService<IPlacesReached<TResourceId, TPlaceId>>()
            ?? throw new InvalidOperationException(
                typeof(TResource).Name + " sits at a " + typeof(TPlaceId).Name + ", and nothing answers where a caller holds a key among those: no IPlacesReached<"
                + typeof(TResourceId).Name + ", " + typeof(TPlaceId).Name + "> is registered. Register one that answers the places the resource's 'at' property holds.");
        var property = at;

        return context =>
        {
            // Copied into a local, so Entity Framework sees the set itself and makes it a subquery of the statement.
            var places = above.PlacesReached(context, caller, key)
                ?? throw new InvalidOperationException(above.GetType().Name + " answered no query for where '" + key + "' is held. Answer an empty one for nowhere.");

            return resource => places.Contains(EF.Property<TPlaceId>(resource, property));
        };
    }
}
