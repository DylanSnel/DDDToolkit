using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A unit as the calling seat's own overview names it, its id and its path from the root, as the schema shows
/// it. It is no <c>OrganizationUnit</c>: an entity has all its fields wherever its owner answers it.
/// </summary>
[ObjectType<SampleTenancy.UnitRef>]
internal static partial class UnitPathType
{
    static partial void Configure(IObjectTypeDescriptor<SampleTenancy.UnitRef> descriptor) => descriptor.Name("UnitPath");
}
