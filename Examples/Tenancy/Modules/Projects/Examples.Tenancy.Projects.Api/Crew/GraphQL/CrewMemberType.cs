using Examples.Tenancy.Projects.Api.GraphQL;
using Examples.Tenancy.Projects.Application.Crew;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;

namespace Examples.Tenancy.Projects.Api.Crew.GraphQL;

/// <summary>
/// A seat on a project's crew, for a period, with the crew roles it holds: the GraphQL type <c>CrewMember</c>,
/// declared over the answer the application's queries give.
/// </summary>
[ObjectType<CrewOverview>]
internal static partial class CrewMemberType
{
    static partial void Configure(IObjectTypeDescriptor<CrewOverview> descriptor)
    {
        descriptor.Name("CrewMember");

        // The project is where the member was read from, and what the rule below is asked on: no field.
        descriptor.Ignore(member => member.ProjectId);
    }

    /// <summary>The seat, by id: its name is Tenancy's to give.</summary>
    [BindMember(nameof(CrewOverview.SeatId))]
    public static ReferencedSeat? GetSeat([Parent] CrewOverview member) => new(member.SeatId);

    /// <summary>
    /// The crew roles it holds, each for its own period. Who holds which role on a crew, and until when, is for
    /// whoever manages that crew: a caller who does not hold the key on the project is answered nothing here,
    /// with the refusal a command would give for the same key, and the member all the same.
    /// </summary>
    /// <remarks>
    /// The rule is the query's: it left the roles out of what this member was read as, for the route as for this
    /// field. What the attribute adds is the rule where a client reads it, in the schema, and the refusal that
    /// says why the field is empty.
    /// </remarks>
    [Authorize(CrewOverview.RolesKey)]
    public static IReadOnlyList<CrewRoleOverview>? GetRoles([Parent] CrewOverview member) => member.Roles;
}
