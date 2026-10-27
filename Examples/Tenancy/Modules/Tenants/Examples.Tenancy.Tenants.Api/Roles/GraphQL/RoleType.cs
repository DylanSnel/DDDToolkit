using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Application.Roles;
using Examples.Tenancy.Tenants.Domain;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Roles.GraphQL;

/// <summary>
/// A role of the tenant as the schema shows it: the module's own <see cref="Role"/>, the class the role manager
/// answers, with its name, the permissions it brings, and whether it manages access.
/// </summary>
/// <remarks>
/// The type is the class itself, so nothing is copied. Its own fields are listed one by one, and the one that carries
/// a rule is a resolver, so the rule stands on it. Whether the role manages access is not on the role: the role
/// manager says so from the catalogue, and the resolver reads it through the directory's data loader, which asks for
/// the roles of one batch in one question.
/// <para>
/// Other modules name a role by its id, and the gateway fills in the rest from here. Every field but the key may
/// be <see langword="null"/> in the schema, by the host's convention for a type with a key: a role that is not
/// there, or not the caller's to see, arrives as its id with nothing else, and without an error.
/// </para>
/// </remarks>
[ObjectType<Role>]
[EntityKey("id")]
internal static partial class RoleType
{
    static partial void Configure(IObjectTypeDescriptor<Role> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(role => role.Id);
        descriptor.Field(role => role.Name);
        descriptor.Field(role => role.FromStarterRole);
        descriptor.Field(role => role.Status);
    }

    /// <summary>
    /// The permissions it brings: for whoever manages the tenant's roles. Anybody who works in the tenant reads what a
    /// role is called; what it lets its holder do is for those who decide that. A caller without the permission gets
    /// <see langword="null"/> here, with the refusal at the field's path, and the role's other fields.
    /// </summary>
    /// <remarks>
    /// The route of the list keeps the same rule: the query says whether the caller holds the permission, and the
    /// route writes the permissions only then. The attribute puts the rule where a client reads it, in the schema,
    /// with the refusal that says why the field is empty.
    /// </remarks>
    [Authorize(RoleListing.ManageRolesPermission)]
    public static IReadOnlyList<string> GetPermissions([Parent] Role role) => role.Permissions;

    /// <summary>
    /// Whether one of its permissions manages access, and so, while containment is on, it is given only by a seat that
    /// holds those permissions.
    /// </summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<bool?> GetManagesAccessAsync([Parent] Role role, IRoleByIdDataLoader roles, CancellationToken cancellationToken)
        => (await roles.LoadAsync(role.Id, cancellationToken))?.ManagesAccess;
}
