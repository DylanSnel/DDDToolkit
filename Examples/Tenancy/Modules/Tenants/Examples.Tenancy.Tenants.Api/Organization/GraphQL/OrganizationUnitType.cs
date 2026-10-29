using Examples.Tenancy.Tenants.Api.Directory.GraphQL;
using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.Entities;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Examples.Tenancy.Tenants.Api.Organization.GraphQL;

/// <summary>
/// A unit of the organization as the schema shows it: the module's own <see cref="OrganizationUnit"/>, the class the
/// organization manager answers, with its kind, and its path from the root and its depth beside it.
/// </summary>
/// <remarks>
/// The type is the class itself, so nothing is copied, and its own fields are listed one by one. The path and the
/// depth are not on the unit: they depend on the units above it. So each is a resolver here, read through the
/// directory's data loader, which asks the organization manager for the units of one batch in one question, and only
/// when a client asks for a path or a depth.
/// <para>
/// It is what the list of the units a caller reads answers, and what a mutation of the tree answers. Other modules
/// name a unit by its id, and the gateway names it from the directory
/// (<see cref="OrganizationUnitNameType"/>): its name and its path, and nothing more.
/// </para>
/// </remarks>
[ObjectType<OrganizationUnit>]
internal static partial class OrganizationUnitType
{
    static partial void Configure(IObjectTypeDescriptor<OrganizationUnit> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(unit => unit.Id);
        descriptor.Field(unit => unit.ParentId);
        descriptor.Field(unit => unit.Name);
        descriptor.Field(unit => unit.Kind);
        descriptor.Field(unit => unit.Status);
    }

    /// <summary>The unit's path from the root, the names joined with <c>" / "</c>.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<string?> GetPathAsync([Parent] OrganizationUnit unit, IOrganizationUnitNameByIdDataLoader names, CancellationToken cancellationToken)
        => (await names.LoadAsync(unit.Id, cancellationToken))?.Path;

    /// <summary>How deep the unit is, the root being 1.</summary>
    [Cost(DirectoryQueries.LoadedForTheRequest)]
    public static async Task<int?> GetDepthAsync([Parent] OrganizationUnit unit, IOrganizationUnitNameByIdDataLoader names, CancellationToken cancellationToken)
        => (await names.LoadAsync(unit.Id, cancellationToken))?.Depth;
}
