using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// What the application tells Tenancy: its catalogue, and how to make a new id of each kind. Every one is
/// required. The ids are the application's, of whatever key type it chose, so Tenancy never guesses how to
/// make one; a command that is given an id (an import, fixed seed data) uses that one instead.
/// <see cref="TenantSelection"/> has a default and <see cref="OperatorTokenRoles"/> is empty unless the application
/// has operators: those two are not required.
/// </summary>
public sealed class TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The application's packs, unit kinds and keys. Required.</summary>
    public ApplicationCatalogue? Catalogue { get; set; }

    /// <summary>Makes a new tenant id. Required.</summary>
    public Func<TTenantId>? NewTenantId { get; set; }

    /// <summary>Makes a new seat id. Required.</summary>
    public Func<TSeatId>? NewSeatId { get; set; }

    /// <summary>Makes a new unit id. Required.</summary>
    public Func<TUnitId>? NewUnitId { get; set; }

    /// <summary>Makes a new role id. Required.</summary>
    public Func<TRoleId>? NewRoleId { get; set; }

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

    /// <summary>The names of the options that are not set, in the order they are declared.</summary>
    internal IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (Catalogue is null)
        {
            missing.Add(nameof(Catalogue));
        }

        if (NewTenantId is null)
        {
            missing.Add(nameof(NewTenantId));
        }

        if (NewSeatId is null)
        {
            missing.Add(nameof(NewSeatId));
        }

        if (NewUnitId is null)
        {
            missing.Add(nameof(NewUnitId));
        }

        if (NewRoleId is null)
        {
            missing.Add(nameof(NewRoleId));
        }

        return missing;
    }

    /// <summary>The options, checked: a use case made without registration still names what is missing.</summary>
    internal TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> Checked()
    {
        var missing = Missing();
        return missing.Count == 0 ? this : throw MissingOptions(missing);
    }

    internal static InvalidOperationException MissingOptions(IReadOnlyList<string> missing)
        => new("Tenancy needs every one of its options, and these are not set: " + string.Join(", ", missing)
               + ". Set them in the configure callback of AddTenancy.");
}
