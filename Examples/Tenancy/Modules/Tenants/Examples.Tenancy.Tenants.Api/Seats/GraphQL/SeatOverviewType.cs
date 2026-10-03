using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// Who the calling seat is and what it may do where, as the schema shows it: its tenant, its placements with the
/// roles held at each, the roles it holds anywhere, and every key it holds now. Declared over the record the
/// package answers, under its plain name: the record is nested in a generic class, and the name HotChocolate
/// would infer for it spells every type argument out.
/// </summary>
[ObjectType<SampleTenancy.SeatOverview>]
internal static partial class SeatOverviewType
{
    static partial void Configure(IObjectTypeDescriptor<SampleTenancy.SeatOverview> descriptor) => descriptor.Name("SeatOverview");
}
