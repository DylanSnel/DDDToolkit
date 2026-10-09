using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The requirements about one kind of resource with members. A closed set: the constructor is private, so the
/// cases below are all there are, and each is a record, so two requirements that say the same are equal and a
/// test can hold every request to the one it is meant to declare.
/// <para>
/// What a requirement cannot say stays in the handler, in plain sight: whether the one to be made a member is
/// active, which roles go to a member, a second key a command's own rule asks for, a rule of the application's
/// own about what its caller may give. A rule that needs how or until when the caller holds the key asks for it
/// (<see cref="IMemberQuestions{TResourceId}.HoldAsync"/>).
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
[AccessCheckRegistration("services.Add{Resource}MemberAccess<{TRequests}>(), written for the resource whose id the case names")]
public abstract partial record MemberAccess<TResourceId> : AccessRequirement
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    private MemberAccess(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    /// <summary>The key the requirement is about.</summary>
    public string Key { get; }
}
