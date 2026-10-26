using System.Reflection;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// A module states its permission keys once, on the list it marks with <see cref="TenancyPermissionsAttribute"/>,
/// and what composes the modules finds them: the host registers every module's list with the one call Tenancy's
/// generator wrote into it, and the program that exports builds the catalogue the policies are written from with
/// every list it finds marked in the modules it references. No module registers its own, and no project names a
/// module's keys.
/// </summary>
/// <remarks>
/// Read from the host's registrations as it makes them, with no database behind it, and from the marks as the export
/// reads them, so this holds in every build. That the two catalogues agree as a whole, packs and marks included, is
/// <see cref="Host.StartupTests"/>' to say, with the host on Supabase.
/// </remarks>
public sealed class ModuleKeysTests
{
    /// <summary>The keys every module of the sample states, each module's list in the order of its name.</summary>
    private static readonly IReadOnlyList<Permission> EveryModulesKeys = [.. InspectionCatalogue.Permissions, .. ProjectCatalogue.Permissions];

    [Fact]
    public void The_host_adds_every_modules_keys_in_one_contribution_and_no_module_adds_its_own()
    {
        var contributions = HostRegistrations.All
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(PermissionContribution))
            .Select(descriptor => descriptor.ImplementationInstance)
            .ToList();

        contributions.Should().ContainSingle("the host's one generated call adds them, and a module that also added its own would add its keys twice")
            .Which.Should().BeOfType<PermissionContribution>()
            .Which.Permissions.Should().Equal(EveryModulesKeys, "every list a module marks, each once, and the same declarations the modules hold");
    }

    [Fact]
    public void The_catalogue_the_policies_are_exported_from_holds_the_same_lists()
    {
        // The export builds without the host's services, from the lists it finds marked. Tenancy's own keys and the
        // application's part come on top; of the modules' keys, exactly these.
        ExportedMarks.Tenancy().Catalogue.Permissions.Where(permission => permission.Module != "Tenancy").Select(permission => permission.Key)
            .Should().BeEquivalentTo(EveryModulesKeys.Select(permission => permission.Key));
    }

    [Fact]
    public void Every_list_of_keys_a_module_declares_is_marked()
    {
        // A list a module declares and does not mark would reach neither the host nor the export: its keys would be
        // missing from the catalogue, and the first question about one of them would throw.
        var lists = SampleLayout.Projects
            .Select(project => project.Anchor.Assembly)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(property => typeof(IEnumerable<Permission>).IsAssignableFrom(property.PropertyType)))
            .ToList();

        lists.Select(list => list.DeclaringType!.Name + "." + list.Name)
            .Should().BeEquivalentTo(["InspectionCatalogue.Permissions", "ProjectCatalogue.Permissions"], "the sample's modules each keep one list");
        lists.Should().OnlyContain(list => list.IsDefined(typeof(TenancyPermissionsAttribute)), "a module states its keys on the list it marks");
    }
}
