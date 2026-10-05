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
/// Other modules name a unit by its id, and the gateway fills in the rest from here. Every field but the key may
/// be <see langword="null"/> in the schema, by the host's convention for a type with a key: a unit that is not
/// there, or not the caller's to see, arrives as its id with nothing else, and without an error. Within Tenancy's
/// own answers the fields are always filled, but <c>parentId</c>, which the root has none of.
/// </remarks>
[ObjectType<UnitListing>]
[EntityKey("id")]
internal static partial class OrganizationUnitType
{
    static partial void Configure(IObjectTypeDescriptor<UnitListing> descriptor) => descriptor.Name("OrganizationUnit");
}
