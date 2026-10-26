using Examples.Tenancy.Tenants.Application.Organization;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Organization.GraphQL;

/// <summary>
/// A unit of the organization as the schema shows it, with its path from the root and its kind: declared over the
/// record the module's queries answer, which is the directory's answer with the kind of the application's own unit
/// class beside it.
/// </summary>
/// <remarks>
/// It is what the list of the units a caller reads answers, and what a mutation of the tree answers. Other modules
/// name a unit by its id, and the gateway names it from the directory (<see cref="Directory.GraphQL.OrganizationUnitNameType"/>):
/// its name and its path, and nothing more.
/// </remarks>
[ObjectType<UnitListing>]
internal static partial class OrganizationUnitType
{
    static partial void Configure(IObjectTypeDescriptor<UnitListing> descriptor) => descriptor.Name("OrganizationUnit");
}
