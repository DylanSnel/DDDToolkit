using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Membership;

public abstract partial class KeptRoleAggregate<TRoleId>
{
    /// <summary>
    /// A role has a name of 1 to <see cref="MaxNameLength"/> characters with no white space around it, and a
    /// description of at most <see cref="MaxDescriptionLength"/>. Everything that names a role refuses
    /// another name before anything changes; this is the net under code that got round it.
    /// <para>
    /// It reports the package's own name for the rule, <see cref="MembershipRefusals.RoleNameInvalid"/>, and
    /// not a code of the resource: a role does not know whose it is.
    /// </para>
    /// </summary>
    public sealed class NameIsValid : IInvariant<KeptRoleAggregate<TRoleId>>
    {
        /// <inheritdoc />
        public string Code => MembershipRefusals.RoleNameInvalid;

        /// <inheritdoc />
        public InvariantFailure? Check(KeptRoleAggregate<TRoleId> entity)
        {
            if (string.IsNullOrWhiteSpace(entity.Name) || entity.Name.Length > MaxNameLength || entity.Name != entity.Name.Trim())
            {
                return MembershipCodes.Failure(MembershipRefusals.RoleNameInvalid, ("Min", 1), ("Max", MaxNameLength));
            }

            return entity.Description is { Length: <= MaxDescriptionLength }
                ? null
                : MembershipCodes.Failure(MembershipRefusals.RoleNameInvalid, ("Min", 0), ("Max", MaxDescriptionLength), (RefusalException.FieldArgument, "description"));
        }
    }
}
