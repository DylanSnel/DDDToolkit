using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// What the member manager asks of a role before it gives one, asked here alone: whether the role is one in use for the
/// resource's members, as the storage reads it on the scope's context, refused as the manager refuses it. For the tests
/// about which roles a request reads, which need no member added to say it.
/// </summary>
internal static class Admitted
{
    /// <summary>Returns where the role goes to a member; refuses with <c>role-not-for-members</c> under <paramref name="codes"/> otherwise.</summary>
    public static async Task RoleAsync<TResourceId, TRoleId>(IServiceProvider provider, MembershipCodes codes, TRoleId role, CancellationToken cancellationToken)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        if (!await provider.GetRequiredService<IMemberRoles<TResourceId, TRoleId>>().ExistsAsync(role, cancellationToken))
        {
            throw codes.Refuse(MembershipRefusals.RoleNotForMembers, ("Role", role));
        }
    }
}
