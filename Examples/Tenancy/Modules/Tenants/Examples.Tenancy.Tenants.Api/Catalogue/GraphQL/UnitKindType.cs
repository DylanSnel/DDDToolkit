using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Catalogue.GraphQL;

/// <summary>
/// A kind a unit can be, as the schema shows it: declared over the record the catalogue's query answers.
/// </summary>
[ObjectType<UnitKindOverview>]
internal static partial class UnitKindType
{
    static partial void Configure(IObjectTypeDescriptor<UnitKindOverview> descriptor) => descriptor.Name("UnitKind");
}
