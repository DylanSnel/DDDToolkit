using DDDToolkit.Supporting.Membership;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;

namespace Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;

/// <summary>
/// A project role of the tenant: the GraphQL type <c>ProjectRole</c>, declared over the answer the application's
/// queries give. What a crew member's role is, and what <c>projectRoles</c> lists.
/// </summary>
[ObjectType<ProjectRoleListing>]
internal static partial class ProjectRoleType
{
    static partial void Configure(IObjectTypeDescriptor<ProjectRoleListing> descriptor) => descriptor.Name("ProjectRole");

    /// <summary>The role's id.</summary>
    public static ProjectRoleId GetId([Parent] ProjectRoleListing role) => role.Id;

    /// <summary>Its name.</summary>
    public static string GetName([Parent] ProjectRoleListing role) => role.Name;

    /// <summary>What it is for; empty when nobody said.</summary>
    public static string GetDescription([Parent] ProjectRoleListing role) => role.Description;

    /// <summary>The starter role it was made from, or nothing for one the tenant made.</summary>
    public static string? GetMadeFrom([Parent] ProjectRoleListing role) => role.MadeFrom;

    /// <summary>Whether it can still be given.</summary>
    public static KeptRoleStatus GetStatus([Parent] ProjectRoleListing role) => role.Status;

    /// <summary>
    /// The keys it gives on a crew. What a role lets its holders do is for whoever manages the tenant's roles: a
    /// caller who does not hold the key for the whole tenant is answered nothing here, with the refusal a command
    /// would give for the same key.
    /// </summary>
    [Authorize(ProjectRoleListing.KeysKey)]
    public static IReadOnlyList<string>? GetKeys([Parent] ProjectRoleListing role) => role.Keys;
}
