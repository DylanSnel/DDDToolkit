using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// What the application tells Tenancy, all of it with a default: what it adds to the catalogue
/// (<see cref="Catalogue"/>), which signed-in users may hold a seat (<see cref="TenantSelection"/>), and who its
/// operators are (<see cref="OperatorTokenRoles"/>, empty unless it has any).
/// <para>
/// How a new id is made is no option: each id says it itself, with its <c>Create()</c>, which Tenancy calls as
/// <c>TTenantId.Create()</c>, in code and before the save. The generator writes it for an id over a <see cref="Guid"/>,
/// a time-ordered one, and an id over anything else declares its own (DDD00067 says so where it has none). A command
/// that is given an id, an import or fixed seed data, uses that one instead.
/// </para>
/// </summary>
public sealed class TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>
    /// What the application adds to the catalogue: role packs, keys of its own and marks on keys that manage
    /// access. Left unset, it adds none: the keys are Tenancy's and what the modules contribute, and every tenant
    /// starts with the default administrators' role (<see cref="TenancyPacks.DefaultAdministrators"/>).
    /// </summary>
    public ApplicationCatalogue? Catalogue { get; set; }

    /// <summary>
    /// Which signed-in users may hold a seat, by the role their token carries: those with <c>authenticated</c>
    /// unless the application lists others in <see cref="TenantSelectionOptions.SeatedTokenRoles"/>.
    /// </summary>
    public TenantSelectionOptions TenantSelection { get; } = new();

    /// <summary>
    /// The token roles whose users are operators of the application: staff who look across tenants, by the role
    /// their token carries (<c>Caller.Role</c>, as the host's token role map spells it). Empty by default: an
    /// application without operators has none.
    /// <code>
    /// options.OperatorTokenRoles.Add("operator");
    /// </code>
    /// <para>
    /// An operator holds no seat. Its token role is never one of <see cref="TenantSelectionOptions.SeatedTokenRoles"/>,
    /// so tenant selection answers nobody for it whatever tenant the request names, and registration refuses a
    /// role that is on both lists. An operator reads: the tenants, through <c>TenantDirectory</c>, and on Postgres
    /// whatever the policies of its own database role let it read. It changes nothing itself: what it asks for is
    /// carried out by system work that names it (<see cref="TenancyWork.BeginOperator"/> and
    /// <see cref="TenancyWork.BeginOperatorIn"/>).
    /// </para>
    /// </summary>
    public ISet<string> OperatorTokenRoles { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="caller"/> is one of the application's operators: a signed-in user whose token
    /// carries one of <see cref="OperatorTokenRoles"/>. The one place that says so: the tenants' directory and
    /// the check of <see cref="TenancyRequirement.Operator"/> both ask here, so they mean the same caller
    /// by it. System work is never one, whoever it is recorded as, and neither is anyone without an identity.
    /// </summary>
    /// <param name="caller">Who is calling, as the toolkit says: the caller of the request, not the Tenancy caller.</param>
    public bool IsOperator(Caller? caller)
        => caller is { Kind: CallerKind.User, UserId: not null, Role: { } tokenRole } && OperatorTokenRoles.Contains(tokenRole);

    /// <summary>The operator token roles that are seated as well, which no role may be: in ordinal order.</summary>
    internal IReadOnlyList<string> SeatedOperators()
        => [.. OperatorTokenRoles.Where(role => TenantSelection.Seats(role)).Order(StringComparer.Ordinal)];
}
