using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

public abstract partial class KeptRoleAggregate<TRoleId>
{
    /// <summary>
    /// A role's keys are distinct, not blank, and in ordinal order: the one way a set of keys is written
    /// down, so two roles that give the same keys hold the same list. Setting a role's keys writes them so;
    /// this is the net under code that got round it.
    /// <para>
    /// Whether each key is one a member's role can give is not asked here. That is the resource's rules to
    /// say, which a role does not carry, and it is asked where the keys are set and again where they are
    /// read. It reports the package's own name for the rule,
    /// <see cref="MembershipRefusals.RoleKeysNotNormalized"/>.
    /// </para>
    /// </summary>
    public sealed class KeysNormalized : IInvariant<KeptRoleAggregate<TRoleId>>
    {
        /// <inheritdoc />
        public string Code => MembershipRefusals.RoleKeysNotNormalized;

        /// <inheritdoc />
        public InvariantFailure? Check(KeptRoleAggregate<TRoleId> entity)
        {
            var keys = entity.Keys ?? [];
            for (var index = 0; index < keys.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(keys[index])
                    || (index > 0 && string.CompareOrdinal(keys[index - 1], keys[index]) >= 0))
                {
                    return MembershipCodes.Failure(MembershipRefusals.RoleKeysNotNormalized);
                }
            }

            return null;
        }
    }
}
