using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The one rule every name in Tenancy follows: trimmed, not blank unless it may be empty, and no longer than
/// its maximum. The aggregates refuse a name that breaks it before anything changes, and their invariants
/// check the same rule afterwards.
/// <para>
/// A refusal says which name it is about with a stable token in its <c>What</c> argument, never with a word,
/// so its message reads the same in every language. Next to it, the <c>Field</c> argument names the input the
/// text belongs under, in the word the use cases call it by: <c>name</c> for the name of a tenant, a unit or a
/// role, since each is the one name its command takes, and <c>displayName</c>, <c>description</c> or
/// <c>reason</c> for the others.
/// </para>
/// </summary>
internal static class TenancyNames
{
    /// <summary>The longest role name, shared by the role and the catalogue's packs, which roles are made from.</summary>
    public const int MaxRoleNameLength = 120;

    /// <summary>The longest role description, shared by the role and the catalogue's packs.</summary>
    public const int MaxRoleDescriptionLength = 1000;

    /// <summary>
    /// The longest permission key. A stored right holds one key in a column of this length, so the catalogue
    /// refuses a longer one when it is built rather than the database at the first grant.
    /// </summary>
    public const int MaxPermissionKeyLength = 128;

    /// <summary>The longest pack key, which a role made from the pack keeps in a column of this length.</summary>
    public const int MaxPackKeyLength = 64;

    /// <summary>The token for a tenant's name, which is its organization's.</summary>
    public const string TenantNameToken = "tenant-name";

    /// <summary>The token for a unit's name.</summary>
    public const string UnitNameToken = "unit-name";

    /// <summary>The token for a seat's display name.</summary>
    public const string DisplayNameToken = "display-name";

    /// <summary>The token for a role's name.</summary>
    public const string RoleNameToken = "role-name";

    /// <summary>The token for a role's description.</summary>
    public const string RoleDescriptionToken = "role-description";

    /// <summary>The token for the reason a tenant is suspended or closed, or a grant is made.</summary>
    public const string ReasonToken = "reason";

    /// <summary>A name that must be there: trimmed, or <c>tenancy.name-invalid</c> when blank or too long.</summary>
    /// <param name="value">The name as given.</param>
    /// <param name="what">Which name it is, as one of the tokens above.</param>
    /// <param name="max">The longest it may be.</param>
    public static string Required(string? value, string what, int max)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || trimmed.Length > max
            ? throw TenancyRefusals.Of(TenancyRefusals.NameInvalid, Arguments(what, 1, max))
            : trimmed;
    }

    /// <summary>Text that may be empty: trimmed, or <c>tenancy.name-invalid</c> when too long.</summary>
    /// <param name="value">The text as given; <see langword="null"/> is empty.</param>
    /// <param name="what">Which text it is, as one of the tokens above.</param>
    /// <param name="max">The longest it may be.</param>
    public static string Optional(string? value, string what, int max)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > max
            ? throw TenancyRefusals.Of(TenancyRefusals.NameInvalid, Arguments(what, 0, max))
            : trimmed;
    }

    /// <summary>Whether a stored name follows the rule, for an invariant.</summary>
    public static bool IsValid(string? value, int max)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= max;

    /// <summary>What an invariant reports for a stored name that must be there and breaks the rule.</summary>
    public static InvariantFailure RequiredFailure(string what, int max)
        => TenancyRefusals.Failure(TenancyRefusals.NameInvalid, Arguments(what, 1, max));

    /// <summary>What an invariant reports for stored text that may be empty and is too long.</summary>
    public static InvariantFailure OptionalFailure(string what, int max)
        => TenancyRefusals.Failure(TenancyRefusals.NameInvalid, Arguments(what, 0, max));

    private static (string Name, object? Value)[] Arguments(string what, int min, int max)
        => [("What", what), ("Min", min), ("Max", max), (RefusalException.FieldArgument, FieldOf(what))];

    /// <summary>The input a name's refusal is about, as the use cases call it.</summary>
    private static string FieldOf(string what)
        => what switch
        {
            DisplayNameToken => "displayName",
            RoleDescriptionToken => "description",
            ReasonToken => "reason",
            _ => "name",
        };
}
