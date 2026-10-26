using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class RoleAggregate<TRoleId, TTenantId>
{
    /// <summary>A role has a name of 1 to <see cref="MaxNameLength"/> characters and a description of at most <see cref="MaxDescriptionLength"/>.</summary>
    public sealed class NameIsValid : IInvariant<RoleAggregate<TRoleId, TTenantId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.NameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(RoleAggregate<TRoleId, TTenantId> entity)
        {
            if (!TenancyNames.IsValid(entity.Name, MaxNameLength))
            {
                return TenancyNames.RequiredFailure(TenancyNames.RoleNameToken, MaxNameLength);
            }

            return entity.Description is { Length: <= MaxDescriptionLength }
                ? null
                : TenancyNames.OptionalFailure(TenancyNames.RoleDescriptionToken, MaxDescriptionLength);
        }
    }
}
