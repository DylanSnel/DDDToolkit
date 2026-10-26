using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

public abstract partial class CustomRoleAggregate<TRoleId>
{
    /// <summary>
    /// A role's permissions are distinct, not blank, and in ordinal order: the one way a set of permissions is written
    /// down, so two roles that give the same permissions hold the same list. Setting a role's permissions writes them so;
    /// this is the net under code that got round it.
    /// <para>
    /// Whether each permission is one a member's role can give is not asked here. That is the resource's rules to
    /// say, which a role does not carry, and it is asked where the permissions are set and again where they are
    /// read. It reports the package's own name for the rule,
    /// <see cref="MembershipRefusals.RolePermissionsNotNormalized"/>.
    /// </para>
    /// </summary>
    public sealed class PermissionsNormalized : IInvariant<CustomRoleAggregate<TRoleId>>
    {
        /// <inheritdoc />
        public string Code => MembershipRefusals.RolePermissionsNotNormalized;

        /// <inheritdoc />
        public InvariantFailure? Check(CustomRoleAggregate<TRoleId> entity)
        {
            var permissions = entity.Permissions ?? [];
            for (var index = 0; index < permissions.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(permissions[index])
                    || (index > 0 && string.CompareOrdinal(permissions[index - 1], permissions[index]) >= 0))
                {
                    return MembershipCodes.Failure(MembershipRefusals.RolePermissionsNotNormalized);
                }
            }

            return null;
        }
    }
}
