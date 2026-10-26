using DDDToolkit.Abstractions.Access;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Where the roles a member holds on a resource come from: one choice of three, which the rules are made with.
/// <code>
/// Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]), new("onlooker", [DocumentPermissions.View])])
/// Roles.Custom(starters: [new("editor", [BoardPermissions.View, BoardPermissions.Edit])])
/// Roles.External(OrganizationFunctions.RolesWithKey)
/// </code>
/// <list type="bullet">
/// <item><b>Fixed</b>: roles in code, the same for everybody, changed with a deploy. A member holds one by its name,
/// as a <see cref="NamedRole"/>.</item>
/// <item><b>Custom</b>: rows a customer makes for itself while the application runs, of the application's role class
/// (<see cref="CustomRoleAttribute{TRoleId, TResource}"/>), with a name, keys of the resource's and an archive. A
/// member holds one by the class's id. The starter roles are what a customer's first rows are made from.</item>
/// <item><b>External</b>: roles the application keeps in a store of its own, the roles of its organization say,
/// which it answers for through <see cref="UseCases.IMemberRoles{TResourceId, TRoleId}"/> and, in a database that
/// answers the questions itself, through a function. A member holds one by that store's id.</item>
/// </list>
/// <para>
/// Whichever it is, what a role gives a member is held to the resource's permissions: only a permission marked
/// <c>InRoles</c> (<see cref="OnAttribute{TResource}.InRoles"/>), and, where the roles take them, keys of other
/// modules (<see cref="OtherKeys"/>). The owner holds no role: it holds the permissions marked <c>Owner</c> by owning
/// the resource.
/// </para>
/// </summary>
public sealed class Roles
{
    private Roles(RolesKind kind, IReadOnlyList<DeclaredRole> declared, DatabaseFunction? function, OtherKeys? otherKeys)
    {
        Kind = kind;
        Declared = declared;
        Function = function;
        OtherKeys = otherKeys;
    }

    /// <summary>Which of the three it is.</summary>
    public RolesKind Kind { get; }

    /// <summary>
    /// The roles declared in code: the roles a member holds, for <see cref="RolesKind.Fixed"/>; the starter roles
    /// a customer's first rows are made from, for <see cref="RolesKind.Custom"/>; none for
    /// <see cref="RolesKind.External"/>.
    /// </summary>
    public IReadOnlyList<DeclaredRole> Declared { get; }

    /// <summary>
    /// For <see cref="RolesKind.External"/>, the function that answers in the database which of the application's
    /// roles give a key, or <see langword="null"/> where the database does not answer the questions itself.
    /// </summary>
    public DatabaseFunction? Function { get; }

    /// <summary>
    /// Which keys of other modules a role gives on the resource besides its permissions, or <see langword="null"/>
    /// for none: then a role gives the resource's permissions marked <c>InRoles</c> and nothing else.
    /// </summary>
    public OtherKeys? OtherKeys { get; }

    /// <summary>
    /// Roles in code, each with the permissions it gives on the resource, held by name: the same for every resource
    /// of the kind, and changed with a deploy.
    /// </summary>
    /// <param name="roles">
    /// The roles, at least one, each named once, with permissions of the resource that a role can give
    /// (<see cref="OnAttribute{TResource}.InRoles"/>).
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="roles"/> is empty, or a role has no name, the name of another, or a key twice.</exception>
    public static Roles Fixed(IReadOnlyList<DeclaredRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Count == 0)
        {
            throw new ArgumentException(
                "Fixed roles are at least one role a member can hold, with the keys it gives: Roles.Fixed([new(\"onlooker\", [DocumentPermissions.View])]). "
                + "Where a customer makes the roles, say Roles.Custom(); where the application keeps them elsewhere, Roles.External().",
                nameof(roles));
        }

        return new Roles(RolesKind.Fixed, Copied(roles, nameof(roles)), null, null);
    }

    /// <summary>
    /// Roles a customer makes for itself while the application runs: rows of the application's role class
    /// (<see cref="CustomRoleAttribute{TRoleId, TResource}"/>), each with a name and the keys it gives, which a
    /// customer renames, re-keys and archives, held by the class's id.
    /// </summary>
    /// <param name="starters">
    /// The roles a customer starts with: what its first rows are made from (<see cref="UseCases.StarterRoles"/>),
    /// none when left out. A starter role is no role a member holds; the row made from it is.
    /// </param>
    /// <param name="otherKeys">Which keys of other modules a role may hold besides the resource's permissions; none when left out.</param>
    /// <exception cref="ArgumentException">A starter role has no name, the name of another, or a key twice.</exception>
    public static Roles Custom(IReadOnlyList<DeclaredRole>? starters = null, OtherKeys? otherKeys = null)
        => new(RolesKind.Custom, Copied(starters ?? [], nameof(starters)), null, otherKeys);

    /// <summary>
    /// Roles the application keeps in a store of its own, such as the roles of its organization, held by that
    /// store's id. In C# the application answers which roles there are and which give a key, through ports it
    /// registers with the resource; in a database that answers the questions itself, <paramref name="function"/>
    /// answers which roles give a key.
    /// </summary>
    /// <param name="function">
    /// The function that answers the roles that give a key in the database, one a package ships or the application
    /// declares: the organization package's <c>roles_with_key</c> for the roles of its organization. It takes the key as
    /// text and answers the ids of the roles, as a member's row stores them. Left out by an application whose
    /// database does not answer the questions itself; whatever writes the resource's functions refuses rules that
    /// name none. It is asked by the resource's functions, which run as their owner, so it reads every row whatever
    /// policies its tables have: where the roles are some customer's, it keeps to the caller's by a condition of
    /// its own, never by a policy. It is never asked without a key.
    /// </param>
    /// <param name="otherKeys">
    /// Which keys of other modules a role gives on the resource besides the resource's permissions: none when left
    /// out. Roles of an organization usually hold keys of many modules, so say it where those reach the resource.
    /// </param>
    public static Roles External(DatabaseFunction? function = null, OtherKeys? otherKeys = null)
        => new(RolesKind.External, [], function, otherKeys);

    private static DeclaredRole[] Copied(IReadOnlyList<DeclaredRole> roles, string parameter)
    {
        var copied = new List<DeclaredRole>(roles.Count);
        foreach (var role in roles)
        {
            if (role is null || string.IsNullOrWhiteSpace(role.Name) || role.Name != role.Name.Trim() || role.Name.Length > NamedRole.MaxLength)
            {
                throw new ArgumentException(
                    "A role's name is 1 to " + NamedRole.MaxLength + " characters, with no white space around it: '" + role?.Name + "' is not.",
                    parameter);
            }

            if (copied.Any(other => other.Name == role.Name))
            {
                throw new ArgumentException("The role '" + role.Name + "' is declared twice. A role's name is what a member's row stores, so it is declared once.", parameter);
            }

            if (role.Keys is null || role.Keys.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("The role '" + role.Name + "' lists a blank key. A role lists the keys it gives, and none is blank.", parameter);
            }

            if (role.Keys.Distinct(StringComparer.Ordinal).Count() != role.Keys.Count)
            {
                throw new ArgumentException("The role '" + role.Name + "' lists a key twice.", parameter);
            }

            // Copied, so the keys a role gives are the ones it was declared with, whatever happens to the list it was given.
            copied.Add(role with { Keys = [.. role.Keys] });
        }

        return [.. copied];
    }
}
