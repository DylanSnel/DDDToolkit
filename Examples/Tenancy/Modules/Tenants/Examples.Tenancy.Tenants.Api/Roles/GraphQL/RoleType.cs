using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Application.Roles;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>
/// A role of the tenant as the schema shows it: its name, the keys it brings, and what the tenant uses it for.
/// </summary>
/// <remarks>
/// Declared over the application's own answer, <see cref="RoleListing"/>. Each field is said here, in the order
/// the schema lists them, so the one that carries a rule stands among the others; <c>use</c> is the record's own.
/// <para>
/// Other modules name a role by its id, and the gateway fills in the rest from here. Every field but the key may
/// be <see langword="null"/> in the schema, by the host's convention for a type with a key: a role that is not
/// there, or not the caller's to see, arrives as its id with nothing else, and without an error.
/// </para>
/// </remarks>
[ObjectType<RoleListing>]
[EntityKey("id")]
internal static partial class RoleType
{
    static partial void Configure(IObjectTypeDescriptor<RoleListing> descriptor) => descriptor.Name("Role");

    /// <summary>The key other modules name it by.</summary>
    public static RoleId GetId([Parent] RoleListing listed) => listed.Id;

    /// <summary>Its name.</summary>
    public static string GetName([Parent] RoleListing listed) => listed.Name;

    /// <summary>The pack it was copied from, or nothing for a role the tenant made.</summary>
    public static string? GetFromPack([Parent] RoleListing listed) => listed.FromPack;

    /// <summary>Whether it can still be given.</summary>
    public static RoleStatus GetStatus([Parent] RoleListing listed) => listed.Status;

    /// <summary>
    /// The permission keys it brings: for whoever manages the tenant's roles. Anybody who works in the tenant
    /// reads what a role is called; what it lets its holder do is for those who decide that. A caller without the
    /// key gets <see langword="null"/> here, with the refusal at the field's path, and the role's other fields.
    /// </summary>
    /// <remarks>
    /// The rule is the query's: it left the keys out of what this role was read as, for the route as for this
    /// field. What the attribute adds is the rule where a client reads it, in the schema, and the refusal that
    /// says why the field is empty.
    /// </remarks>
    [Authorize(RoleListing.KeysKey)]
    public static IReadOnlyList<string>? GetKeys([Parent] RoleListing listed) => listed.Keys;

    /// <summary>
    /// Whether one of its keys manages access, and so, while containment is on, it is given only by a seat that
    /// holds those keys.
    /// </summary>
    public static bool GetManagesAccess([Parent] RoleListing listed) => listed.ManagesAccess;
}
