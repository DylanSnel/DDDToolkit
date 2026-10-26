using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

public abstract partial class MemberEntity<TId, TMemberId, TRoleId>
{
    /// <summary>
    /// A member holds a role once: one hold per role. The member list refuses a second hold before anything
    /// changes; this is the net under code that got round it.
    /// <para>
    /// It reports the package's own name for the rule, <see cref="MembershipRefusals.RoleHeld"/>, and not a
    /// code of the resource: a member does not know whose it is.
    /// </para>
    /// </summary>
    public sealed class OneHoldPerRole : IInvariant<MemberEntity<TId, TMemberId, TRoleId>>
    {
        /// <inheritdoc />
        public string Code => MembershipRefusals.RoleHeld;

        /// <inheritdoc />
        public InvariantFailure? Check(MemberEntity<TId, TMemberId, TRoleId> entity)
            => entity._roles.GroupBy(held => held.RoleId).FirstOrDefault(role => role.Count() > 1) is { } twice
                ? MembershipCodes.Failure(MembershipRefusals.RoleHeld, (MembershipCodes.DefaultMemberArgument, entity.MemberId), ("Role", twice.Key))
                : null;
    }
}
