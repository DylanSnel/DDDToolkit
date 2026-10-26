using System.Reflection;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.Postgres;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// What the sample marks for the packages' row access contributions, read from its assemblies the way the Supabase
/// package's generator reads them in the program that exports: every static property marked with an attribute a
/// package's contribution is made from. The exporter writes Tenancy's and Membership's SQL from these, so a test
/// that holds them to what the host runs with holds the exported files to it.
/// </summary>
/// <remarks>
/// The exporter is a program nothing references, and the classes the build writes into it are its own, so they are
/// not asked here: what they are made from is. The export check of the build holds the files to those classes.
/// </remarks>
public static class ExportedMarks
{
    /// <summary>The sample's own assemblies: every module's projects, and the catalogue's.</summary>
    private static readonly Assembly[] Assemblies =
        [.. SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(SampleCatalogue).Assembly).Distinct()];

    /// <summary>
    /// Every static property of the sample marked with <paramref name="mark"/>, a generic one closed over any type
    /// arguments, in the order of their full names, as the generator orders them. Only public ones, in public types,
    /// with a public getter: the exporter references the projects that declare them, and of a library it sees nothing
    /// less, so a mark that is not public would be read here and missed by the export.
    /// </summary>
    /// <param name="mark">The attribute, open where it is generic.</param>
    public static IReadOnlyList<PropertyInfo> Marked(Type mark)
        => [.. Assemblies
            .SelectMany(TypeScan.TypesOf)
            .Where(type => type.IsVisible)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(property => property.GetMethod is { IsPublic: true })
            .Where(property => property.GetCustomAttributes().Any(attribute => (attribute.GetType().IsGenericType ? attribute.GetType().GetGenericTypeDefinition() : attribute.GetType()) == mark))
            .OrderBy(property => property.DeclaringType!.FullName + "." + property.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Tenancy's contribution as the export makes it, with the constructor the package marks for that: every list of
    /// keys marked <see cref="TenancyPermissionsAttribute"/>, the catalogue marked <see cref="TenancyCatalogueAttribute"/>
    /// and the operators marked <see cref="DDDToolkit.Supporting.Tenancy.TenancyOperatorsAttribute"/>.
    /// </summary>
    public static TenancyRowAccessContribution Tenancy()
        => new(
            modules: [.. Marked(typeof(TenancyPermissionsAttribute)).Select(property => (IEnumerable<Permission>)property.GetValue(null)!)],
            application: (ApplicationCatalogue?)Marked(typeof(TenancyCatalogueAttribute)).SingleOrDefault()?.GetValue(null),
            operatorTokenRoles: (IReadOnlyCollection<string>?)Marked(typeof(DDDToolkit.Supporting.Tenancy.TenancyOperatorsAttribute)).SingleOrDefault()?.GetValue(null));
}
