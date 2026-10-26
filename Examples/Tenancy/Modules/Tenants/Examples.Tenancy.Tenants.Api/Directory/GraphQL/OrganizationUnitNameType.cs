using Examples.Tenancy.Tenants.Application.Directory;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tenants.Api.Directory.GraphQL;

/// <summary>
/// What a unit is called, as the schema shows it: its id, its name and its path from the root, and nothing more.
/// What another module names a unit by: the unit of a project is one, filled in by the gateway from here.
/// </summary>
/// <remarks>
/// The directory answers a unit by id with its name and path alone, to a crew member on a project at a unit it is not
/// placed under as well, so the name is no <c>OrganizationUnit</c>, whose kind and status the list of the units a
/// caller reads answers: an entity has all its fields wherever its owner answers it. Every field but the key may be
/// <see langword="null"/> in the schema, by the host's convention for a type with a key: a unit that is not there,
/// or not the caller's tenant's, arrives as its id with nothing else, and without an error.
/// </remarks>
[ObjectType<UnitName>]
[EntityKey("id")]
internal static partial class OrganizationUnitNameType
{
    static partial void Configure(IObjectTypeDescriptor<UnitName> descriptor) => descriptor.Name("OrganizationUnitName");
}
