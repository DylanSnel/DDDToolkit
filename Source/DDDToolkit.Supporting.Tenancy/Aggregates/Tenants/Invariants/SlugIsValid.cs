using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

public abstract partial class TenantAggregate<TTenantId>
{
    /// <summary>The slug follows the pattern. A slug is only ever set from a valid one, so this is the net under that.</summary>
    public sealed class SlugIsValid : IInvariant<TenantAggregate<TTenantId>>
    {
        /// <inheritdoc />
        public string Code => TenancyRefusals.InvalidSlug;

        /// <inheritdoc />
        public InvariantFailure? Check(TenantAggregate<TTenantId> entity)
            => entity.Slug is { IsValid: true } ? null : TenancyRefusals.Failure(TenancyRefusals.InvalidSlug);
    }
}
