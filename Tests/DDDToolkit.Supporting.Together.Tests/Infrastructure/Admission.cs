using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Together.Tests.Infrastructure;

/// <summary>
/// What the member manager of a resource asks of the organization before it changes the members, asked here alone: whether
/// a seat may be made a member, and whether a role goes to one, of the ports the join with the organization answers. For
/// the tests about those answers, which need no member added to say them.
/// </summary>
/// <param name="provider">The services of the scope the questions are asked in.</param>
/// <param name="codes">The codes of the resource, which a refusal carries.</param>
internal sealed class Admission<TResourceId, TMemberId, TRoleId>(IServiceProvider provider, MembershipCodes codes)
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>Returns where <paramref name="member"/> may be made a member; refuses with <c>member-not-active</c> otherwise.</summary>
    public async ValueTask RequireMemberAsync(TMemberId member, CancellationToken cancellationToken)
    {
        if (provider.GetService<IMemberDirectory<TResourceId, TMemberId>>() is { } directory && !await directory.IsActiveAsync(member, cancellationToken))
        {
            throw codes.Refuse(MembershipRefusals.MemberNotActive, (MembershipCodes.DefaultMemberArgument, member));
        }
    }

    /// <summary>Returns where <paramref name="role"/> goes to a member; refuses with <c>role-not-for-members</c> otherwise.</summary>
    public async ValueTask RequireRoleAsync(TRoleId role, CancellationToken cancellationToken)
    {
        if (!await provider.GetRequiredService<IMemberRoles<TResourceId, TRoleId>>().ExistsAsync(role, cancellationToken)
            || (provider.GetService<IMemberRolePolicy<TResourceId, TRoleId>>() is { } policy && !await policy.MayHoldAsync(role, cancellationToken)))
        {
            throw codes.Refuse(MembershipRefusals.RoleNotForMembers, ("Role", role));
        }
    }
}
