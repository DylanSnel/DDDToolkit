using HotChocolate;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Projects.Api.GraphQL;

/// <summary>
/// A unit as Projects knows it: its key, and nothing else. Its name and its path are Tenancy's to give, and the
/// gateway asks Tenancy for them when a client asks for more than the id.
/// </summary>
/// <param name="Id">The unit.</param>
[GraphQLName("OrganizationUnit")]
[EntityKey("id")]
internal sealed record ReferencedOrganizationUnit(OrganizationUnitId Id);
