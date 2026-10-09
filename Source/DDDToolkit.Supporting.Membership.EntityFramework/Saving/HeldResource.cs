using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.Supporting.Membership.Access;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// One kind of resource with members, as <see cref="MemberHoldInterceptor"/> asks about it: its rules, and the hold
/// the request in hand passed with, closed over the resource's id so the interceptor itself needs no type of the
/// application's.
/// </summary>
/// <param name="registration">The resource, as its registration added it.</param>
internal abstract class HeldResource(MembershipRegistration registration)
{
    /// <summary>The resource's aggregate.</summary>
    public Type Resource => registration.Resource;

    /// <summary>The rules the resource was registered with.</summary>
    public MembershipRules Rules => registration.Rules;

    /// <summary>The one for <paramref name="registration"/>'s resource.</summary>
    public static HeldResource For(MembershipRegistration registration)
        => (HeldResource)Activator.CreateInstance(
            typeof(HeldResource<,>).MakeGenericType(registration.Resource, registration.ResourceIdType),
            registration)!;

    /// <summary>
    /// The hold the request in hand passed with, when it is about <paramref name="entity"/>: <see langword="null"/>
    /// with no request in hand, and for a request whose check read no such resource, or another one.
    /// </summary>
    /// <param name="entity">A tracked resource of this kind.</param>
    public abstract HeldVersion? HoldInHand(object entity);

    /// <summary>The resource of this kind the request in hand passed its check on, or <see langword="null"/>: for a message.</summary>
    public abstract object? ResourceInHand();

    /// <summary>The id of <paramref name="entity"/>, for a message.</summary>
    /// <param name="entity">A tracked resource of this kind.</param>
    public abstract object IdOf(object entity);
}

/// <summary><see cref="HeldResource"/> for one resource and its id.</summary>
/// <typeparam name="TResource">The resource's aggregate.</typeparam>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
/// <param name="registration">The resource, as its registration added it.</param>
internal sealed class HeldResource<TResource, TResourceId>(MembershipRegistration registration) : HeldResource(registration)
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <inheritdoc />
    public override HeldVersion? HoldInHand(object entity)
        => Checked<MemberHold<TResourceId>>.TryFindInHand(out var hold) && hold.Resource.Equals(((TResource)entity).Id)
            ? new HeldVersion(hold, hold.Resource, hold.Version)
            : null;

    /// <inheritdoc />
    public override object? ResourceInHand() => Checked<MemberHold<TResourceId>>.TryFindInHand(out var hold) ? hold.Resource : null;

    /// <inheritdoc />
    public override object IdOf(object entity) => ((TResource)entity).Id;
}
