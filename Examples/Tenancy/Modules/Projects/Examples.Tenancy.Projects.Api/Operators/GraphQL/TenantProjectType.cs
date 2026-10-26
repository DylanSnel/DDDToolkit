using Examples.Tenancy.Projects.Application.Operators;
using HotChocolate.Types;

namespace Examples.Tenancy.Projects.Api.Operators.GraphQL;

/// <summary>
/// A project as the application's own staff read it, as the schema shows it: its own data and who changed it
/// last. Declared over the record the operators' query answers.
/// </summary>
/// <remarks>
/// It is no <c>Project</c>: that type shows how its caller reaches a project and names the unit and the owner as
/// what Tenancy says they are, and an operator has no reach and works in no tenant for Tenancy to answer in. So
/// the unit and the owning seat are their ids here, as on the route. The project's own id is a node id, as in
/// every schema that names a project, and is what the operators' field for a project's inspections takes. Its
/// planned range is the value object the modules share, <c>DateRange</c>, as on a <c>Project</c>.
/// </remarks>
[ObjectType<TenantProject>]
internal static partial class TenantProjectType
{
    static partial void Configure(IObjectTypeDescriptor<TenantProject> descriptor) => descriptor.Field(project => project.Id).ID("Project");
}
