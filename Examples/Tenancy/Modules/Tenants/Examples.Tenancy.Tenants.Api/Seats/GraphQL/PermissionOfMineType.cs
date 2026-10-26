using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Seats.GraphQL;

/// <summary>
/// A permission the calling seat holds now, and where, as the schema shows it: declared over the record the seat's own
/// overview answers.
/// </summary>
[ObjectType<TenancyUseCases.PermissionReach>]
internal static partial class PermissionOfMineType
{
    static partial void Configure(IObjectTypeDescriptor<TenancyUseCases.PermissionReach> descriptor) => descriptor.Name("PermissionOfMine");
}
