using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class RoleAggregate<TRoleId, TTenantId>
{
    /// <summary>A role's permissions are distinct, not blank, and in ordinal order.</summary>
    public sealed class PermissionsNormalized : IInvariant<RoleAggregate<TRoleId, TTenantId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.PermissionsNotNormalized;

        /// <inheritdoc />
        public InvariantFailure? Check(RoleAggregate<TRoleId, TTenantId> entity)
        {
            var permissions = entity.Permissions ?? [];
            for (var index = 0; index < permissions.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(permissions[index])
                    || (index > 0 && string.CompareOrdinal(permissions[index - 1], permissions[index]) >= 0))
                {
                    return TenancyRefusals.Failure(TenancyRefusals.PermissionsNotNormalized);
                }
            }

            return null;
        }
    }
}
