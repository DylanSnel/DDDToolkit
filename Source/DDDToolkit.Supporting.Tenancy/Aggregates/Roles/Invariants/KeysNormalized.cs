using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class RoleAggregate<TRoleId, TTenantId>
{
    /// <summary>A role's keys are distinct, not blank, and in ordinal order.</summary>
    public sealed class KeysNormalized : IInvariant<RoleAggregate<TRoleId, TTenantId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.KeysNotNormalized;

        /// <inheritdoc />
        public InvariantFailure? Check(RoleAggregate<TRoleId, TTenantId> entity)
        {
            var keys = entity.Keys ?? [];
            for (var index = 0; index < keys.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(keys[index])
                    || (index > 0 && string.CompareOrdinal(keys[index - 1], keys[index]) >= 0))
                {
                    return TenancyRefusals.Failure(TenancyRefusals.KeysNotNormalized);
                }
            }

            return null;
        }
    }
}
