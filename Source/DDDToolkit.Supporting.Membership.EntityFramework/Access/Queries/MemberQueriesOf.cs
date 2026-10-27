using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Makes the queries on the set of one kind of resource for the services of a scope: what the extensions on the
/// resource's set find in the application's services, registered once for each kind of resource.
/// </summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
internal abstract class MemberQueriesOf<TResource>
    where TResource : class
{
    /// <summary>The context the resource was registered with.</summary>
    public abstract Type Context { get; }

    /// <summary>The queries for the caller that is current, answered with <paramref name="services"/>.</summary>
    /// <param name="services">The services of the scope the query is made in.</param>
    public abstract IMemberQueries<TResource> For(IServiceProvider services);
}

/// <summary><see cref="MemberQueriesOf{TResource}"/> closed over every type of the resource.</summary>
/// <param name="registration">The resource, with its rules.</param>
/// <param name="asMember">Who a caller is as a member, as the rules say; <see langword="null"/> where the application resolves it.</param>
internal sealed class MemberQueriesOf<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId>(MembershipRegistration registration, Func<Caller, TMemberId?>? asMember)
    : MemberQueriesOf<TResource>
    where TContext : DbContext
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    public override Type Context => typeof(TContext);

    /// <inheritdoc />
    public override IMemberQueries<TResource> For(IServiceProvider services)
        => new EfMemberQuestions<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId>(registration, asMember, services);
}
