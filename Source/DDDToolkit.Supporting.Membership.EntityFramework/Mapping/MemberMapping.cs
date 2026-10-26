using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// A resource with its members, as one model maps them with <c>HasMembers</c>: the resource's own entity type,
/// the table of its members and the table of the roles they hold, where the resource keeps its owner, where
/// it sits, for one that says so, and its role class, for one whose roles are kept. What works from the model
/// reads the tables and columns from here, so it follows whatever the application calls them.
/// </summary>
/// <param name="Resource">The resource's entity type: the application's aggregate.</param>
/// <param name="Navigation">The resource's collection of members.</param>
/// <param name="Owner">The resource's property that holds its owner, as a member is known.</param>
/// <param name="Members">The members' entity type, in a table of its own, owned by the resource.</param>
/// <param name="Roles">The entity type of the roles members hold, in a table of its own, owned by the member.</param>
/// <param name="At">
/// The resource's property that holds where it sits, for a resource mapped with <c>at:</c>, or
/// <see langword="null"/>: what a key held from above is compared with.
/// </param>
/// <param name="RoleClass">
/// The entity type of the application's role class for this resource, mapped with <c>IsKeptRole</c>, or
/// <see langword="null"/> when the model maps none: the rows a role's keys are read from, for a resource whose
/// roles are kept.
/// </param>
public sealed record MemberMapping(
    IEntityType Resource,
    INavigation Navigation,
    IProperty Owner,
    IEntityType Members,
    IEntityType Roles,
    IProperty? At = null,
    IEntityType? RoleClass = null)
{
    /// <summary>The application's member class.</summary>
    public Type MemberClass => Members.ClrType;
}
