using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A key the calling seat holds now, and where, as the schema shows it: declared over the record the seat's own
/// overview answers.
/// </summary>
[ObjectType<SampleTenancy.KeyReach>]
internal static partial class KeyOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<SampleTenancy.KeyReach> descriptor) => descriptor.Name("KeyOfMine");
}
