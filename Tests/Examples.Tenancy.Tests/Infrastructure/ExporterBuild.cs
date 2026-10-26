using System.Reflection;
using System.Text;
using System.Xml.Linq;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.Postgres;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.Postgres;
using Examples.Tenancy.Projects.Infrastructure.Access;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The export the exporter's build runs, called with what the code the build generates into the exporter hands it.
/// The exporter is a program nothing references, so a test cannot call that code, and this writes it out: every
/// module's context marked <c>[SupabaseMigrations]</c> with the design-time factory the build wrote beside it, the
/// <c>[RowAccess]</c> rules and <c>[AccessFunction]</c>s of the sample's assemblies as the Supabase package's generator
/// reads them, the packages' contributions made from what the sample marks, each held by a class like the one the
/// build writes for a package, then the two the exporter lists, and the roles and the login role its project says.
/// </summary>
/// <remarks>
/// As the sample is, it writes nothing the committed files do not hold, which a test checks, so what it writes with
/// one choice made another way is what the sample's build would write.
/// </remarks>
public static class ExporterBuild
{
    /// <summary>The program's project file, which says the roles and the login role.</summary>
    private static readonly string ExporterFile = SampleLayout.ProgramProjectFile(SampleLayout.Exporter);

    /// <summary>The sample's own assemblies: every module's projects, and the catalogue's.</summary>
    private static readonly Assembly[] Assemblies =
        [.. SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(SampleCatalogue).Assembly).Distinct()];

    /// <summary>
    /// Runs the build's export in <c>Write</c> mode over a copy of the sample's migrations and answers the files it
    /// wrote there, by name, with their SQL: with <paramref name="moreRules"/> beside the sample's rules, and with the
    /// application's part of the catalogue as <paramref name="application"/> changes it.
    /// </summary>
    /// <param name="moreRules">Rules the sample does not have, as the generator writes them.</param>
    /// <param name="application">A change to the part the sample marks <c>[TenancyCatalogue]</c>, or none.</param>
    public static IReadOnlyDictionary<string, string> Written(IReadOnlyList<RowAccessRule>? moreRules = null, Func<ApplicationCatalogue, ApplicationCatalogue>? application = null)
    {
        var committed = Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "supabase", "migrations");
        var directory = Directory.CreateTempSubdirectory("tenancy-export-").FullName;
        try
        {
            foreach (var file in Directory.GetFiles(committed))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }

            var before = Directory.GetFiles(directory).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
            using var output = new StringWriter();
            SupabaseMigrationBuild.Run(
                    "Write", Sources, [.. Rules(), .. moreRules ?? []], Functions(), Contributions(application), directory, start: null,
                    Property("SupabaseRowAccessRoles"), callerFunctions: null, grants: null, force: null, Property("SupabaseLoginRole"), output)
                .Should().Be(0, output.ToString());

            return Directory.GetFiles(directory)
                .Where(file => !before.Contains(Path.GetFileName(file)))
                .ToDictionary(file => Path.GetFileName(file), file => File.ReadAllText(file).ReplaceLineEndings("\n"), StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The committed file of that name, with the line endings the export writes.</summary>
    public static string Committed(string name)
        => File.ReadAllText(Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "supabase", "migrations", name)).ReplaceLineEndings("\n");

    /// <summary>Every module's context, with the factory the build wrote beside it because the context is marked.</summary>
    private static IReadOnlyList<SupabaseMigrationSource> Sources =>
    [
        SupabaseMigrationSource.For<TenantsContext, TenantsContextDesignTimeFactory>(),
        SupabaseMigrationSource.For<ProjectsContext, ProjectsContextDesignTimeFactory>(),
        SupabaseMigrationSource.For<InspectionsContext, InspectionsContextDesignTimeFactory>(),
    ];

    /// <summary>A property of the exporter's project, or null where it says none.</summary>
    private static string? Property(string name) => XDocument.Load(ExporterFile).Descendants(name).SingleOrDefault()?.Value;

    /// <summary>
    /// Every <c>[RowAccess]</c> rule of the sample, as the generator writes it into the exporter: named after its class
    /// in words, with its operations, its roles and its columns as its attribute says them, and the SQL the core
    /// generator wrote into it.
    /// </summary>
    private static IEnumerable<RowAccessRule> Rules()
    {
        foreach (var type in Types())
        {
            if (Attribute(type, typeof(RowAccessAttribute<>)) is not { } attribute || Sql(type) is not { } sql)
            {
                continue;
            }

            var aggregate = attribute.AttributeType.GetGenericArguments()[0].FullName!;
            var roles = Strings(attribute, "To");
            var columns = Strings(attribute, "Columns");
            yield return columns.Length == 0
                ? RowAccessRule.For(aggregate, Humanized(type.Name), (RowOperations)Convert.ToInt32(attribute.ConstructorArguments[0].Value), sql, roles)
                : RowAccessRule.ForColumns(aggregate, Humanized(type.Name), columns, sql, roles);
        }
    }

    /// <summary>Every <c>[AccessFunction]</c> of the sample, as the generator writes it into the exporter.</summary>
    private static IReadOnlyList<RowAccessFunction> Functions()
        => [.. Types()
            .Select(type => (Type: type, Attribute: Attribute(type, typeof(AccessFunctionAttribute<>)), Sql: Sql(type)))
            .Where(found => found.Attribute is not null && found.Sql is not null)
            .Select(found => RowAccessFunction.For(
                found.Attribute!.AttributeType.GetGenericArguments()[0].FullName!,
                Constant(found.Type, "Name") as string ?? (string)found.Attribute.ConstructorArguments[0].Value!,
                found.Sql!,
                owner: null,
                parameters: Constant(found.Type, "RowAccessParameters") as string ?? "",
                shape: (AccessFunctionShape)(Constant(found.Type, "RowAccessShape") as int? ?? 0)))];

    /// <summary>
    /// The contributions the export asks, in the order the build hands them over: the packages' first, Membership's for
    /// the rules marked <c>[MembershipRules&lt;CrewMember&gt;]</c> and Tenancy's from the catalogue, the operators and
    /// the modules' keys the sample marks, then the module's own two the exporter lists.
    /// </summary>
    private static IReadOnlyList<IRowAccessContribution> Contributions(Func<ApplicationCatalogue, ApplicationCatalogue>? application)
    {
        var membership = ExportedMarks.Marked(typeof(MembershipRulesAttribute<>)).Select(property =>
        {
            var member = property.GetCustomAttributes().Single(attribute => attribute.GetType().IsGenericType && attribute.GetType().GetGenericTypeDefinition() == typeof(MembershipRulesAttribute<>))
                .GetType().GetGenericArguments()[0];
            return (IRowAccessContribution)Activator.CreateInstance(typeof(MembershipRowAccessContribution<>).MakeGenericType(member), property.GetValue(null))!;
        });

        var tenancy = ExportedMarks.Tenancy();
        if (application is not null)
        {
            var part = (ApplicationCatalogue?)ExportedMarks.Marked(typeof(TenancyCatalogueAttribute)).SingleOrDefault()?.GetValue(null) ?? new ApplicationCatalogue();
            tenancy = new TenancyRowAccessContribution(
                ExportedMarks.Marked(typeof(TenancyPermissionsAttribute)).Select(property => (IEnumerable<Permission>)property.GetValue(null)!),
                application(part),
                tenancy.OperatorTokenRoles.ToList());
        }

        return [.. membership.Append(tenancy).Select(contribution => new MadeByTheBuild(contribution)), new UnitChangesWithItsKeys(), new CrewSeatsOfTheProjectsTenant()];
    }

    /// <summary>The types of the sample's assemblies, as the generator walks them.</summary>
    private static IEnumerable<Type> Types() => Assemblies.SelectMany(TypeScan.TypesOf);

    /// <summary>The attribute of <paramref name="type"/> made from <paramref name="definition"/>, as written, or null.</summary>
    private static CustomAttributeData? Attribute(Type type, Type definition)
        => type.GetCustomAttributesData().FirstOrDefault(attribute => attribute.AttributeType.IsGenericType && attribute.AttributeType.GetGenericTypeDefinition() == definition);

    /// <summary>The SQL the core generator wrote into <paramref name="type"/>, or null where it wrote none.</summary>
    private static string? Sql(Type type) => Constant(type, "RowAccessSql") as string;

    /// <summary>The value of the constant <paramref name="name"/> of <paramref name="type"/>, or null.</summary>
    private static object? Constant(Type type, string name)
        => type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly) is { IsLiteral: true } field ? field.GetRawConstantValue() : null;

    /// <summary>The strings of the attribute's array argument <paramref name="name"/>, without the empty ones.</summary>
    private static string[] Strings(CustomAttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values
            ? [.. values.Select(value => value.Value as string).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)]
            : [];

    /// <summary>A class's name in words, as the generator names a rule after its class: <c>NameChangesByTheSeat</c> is "Name changes by the seat".</summary>
    private static string Humanized(string name)
    {
        var words = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (i > 0 && (char.IsUpper(character) || (char.IsDigit(character) && !char.IsDigit(name[i - 1]))))
            {
                words.Append(' ');
            }

            words.Append(i > 0 && char.IsUpper(character) ? char.ToLowerInvariant(character) : character);
        }

        return words.ToString();
    }

    /// <summary>A package's contribution as the class the build writes for it holds it: the access files name the package's class.</summary>
    private sealed class MadeByTheBuild(IRowAccessContribution contribution) : IPackageRowAccessContribution
    {
        public IRowAccessContribution Contribution => contribution;

        public string Owner => contribution.Owner;

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => contribution.Contribute(context, export);
    }
}
