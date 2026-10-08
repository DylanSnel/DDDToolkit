using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.Invariants;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// The folders of the Tenancy sample, read from the source tree. A domain project is laid out per aggregate:
/// <c>Aggregates/{Plural}/{Aggregate}.cs</c>, with its child entities, its events, its invariants and its value
/// objects in folders beside it. A project's root holds only its entry points, a file declares one type, and the
/// namespace of a type is its folder, in every project of the sample and in these tests.
/// </summary>
/// <remarks>
/// Nothing but convention keeps a folder in step with what is in it, so these tests do: which types are
/// aggregates, entities, events and invariants is read from the assemblies, and where each one's file is, and
/// which namespace it declares, from the tree. A type is found by its file's name, which is why a file is named
/// after the one type it declares.
/// <para>
/// One kind of file cannot follow its folder: an invariant is nested in the class it is about, as a partial of
/// it, and a partial has its class's namespace. So a file in <c>Invariants</c> declares the namespace of that
/// class, and the tests hold it to exactly that.
/// </para>
/// <para>
/// Two more things the tree is held to, which no compiler is: every project of the repository, not only the
/// sample's, has a name and a folder short enough for a build under Visual Studio, and nothing the sample's
/// projects say names a database it does not run on.
/// </para>
/// </remarks>
public sealed partial class SourceTreeTests
{
    /// <summary>
    /// The files every project may have at its root, next to its registration: the usings, and a Module.cs with the
    /// project's assembly attributes, HotChocolate's module attribute in an API project and Tenancy's switch in
    /// Tenants' domain project. The toolkit's module is declared by the folder, in Modules/Directory.Build.props, and
    /// by no file of a project.
    /// </summary>
    private static readonly string[] EntryFiles = ["Module.cs", "GlobalUsings.cs"];

    /// <summary>The folders an aggregate keeps what belongs to it in, beside its own file.</summary>
    private static readonly string[] AggregateFolders = ["Entities", "Events", "Invariants", "ValueObjects"];

    /// <summary>The folders at a domain project's root: the aggregates, and what several of them share.</summary>
    private static readonly string[] DomainFolders = ["Aggregates", "ValueObjects", "Services"];

    public static TheoryData<string> Projects => new(SampleLayout.Projects.Select(project => project.Name));

    public static TheoryData<string> DomainProjects => new(SampleLayout.Projects.Where(project => project.Layer == Layer.Domain).Select(project => project.Name));

    /// <summary>Every project of the sample, the modules' and the host, the UI and the AppHost next to them, by its name.</summary>
    public static TheoryData<string> SampleProjects => new(SampleLayout.SampleProjectFiles().Select(Path.GetFileNameWithoutExtension)!);

    /// <summary>The sample's projects, and the project these tests are in, which is held to the same layout.</summary>
    public static TheoryData<string> SampleProjectsAndTheseTests => new([.. SampleLayout.SampleProjectFiles().Select(Path.GetFileNameWithoutExtension)!, TheseTests]);

    /// <summary>This test project's name, which is its folder's and its root namespace.</summary>
    private const string TheseTests = "Examples.Tenancy.Tests";

    [Theory]
    [MemberData(nameof(DomainProjects))]
    public void A_domain_project_has_no_source_file_at_its_root_but_its_entry_points(string project)
    {
        var directory = SampleLayout.DirectoryOf(SampleLayout.Project(project));

        Directory.GetFiles(directory, "*.cs").Select(Path.GetFileName)
            .Should().BeSubsetOf(EntryFiles, "what {0} declares is in the folder of its aggregate, or of what its aggregates share", project);
        FoldersOf(directory).Should().BeSubsetOf(DomainFolders, "a domain project's root holds its aggregates, and what several of them share");
        FoldersOf(directory).Should().Contain("Aggregates");
    }

    [Theory]
    [MemberData(nameof(DomainProjects))]
    public void Every_aggregate_is_under_Aggregates_with_what_belongs_to_it_beside_it(string project)
    {
        var listed = SampleLayout.Project(project);
        var directory = SampleLayout.DirectoryOf(listed);
        var files = SampleLayout.SourceFilesIn(directory);
        var types = TypeScan.TypesOf(listed.Anchor.Assembly)
            .Where(type => type.Namespace is { } name && (name == project || name.StartsWith(project + ".", StringComparison.Ordinal)))
            .ToList();

        var roots = types.Where(type => typeof(IAggregateRoot).IsAssignableFrom(type)).ToList();
        var entities = types.Where(type => typeof(IEntity).IsAssignableFrom(type) && !typeof(IAggregateRoot).IsAssignableFrom(type)).ToList();
        var events = types.Where(type => typeof(DomainEvent).IsAssignableFrom(type)).ToList();
        var invariants = types.Where(IsInvariant).ToList();
        var values = types.Where(type => !type.IsNested && (type.IsEnum || typeof(IEntityId).IsAssignableFrom(type))).ToList();

        // An aggregate Tenancy's switch wrote has no file: it is in the project's root namespace, where the switch
        // writes it, and its documentation says so. Every other aggregate is somebody's file.
        var written = roots.Where(root => root.Namespace == project && files.All(file => Path.GetFileName(file) != root.Name + ".cs")).ToList();

        roots.Should().NotBeEmpty("{0} declares an aggregate", project);
        foreach (var root in roots.Except(written))
        {
            FileOf(root, files).Should().MatchRegex($"^Aggregates/[^/]+/{root.Name}[.]cs$", "{0} is an aggregate: its file is right in its own folder under Aggregates", root.Name);
        }

        foreach (var entity in entities)
        {
            FileOf(entity, files).Should().MatchRegex($"^Aggregates/[^/]+/Entities/{entity.Name}[.]cs$", "{0} is a child entity: it is in its aggregate's Entities folder", entity.Name);
        }

        foreach (var raised in events)
        {
            FileOf(raised, files).Should().MatchRegex($"^Aggregates/[^/]+/Events/{raised.Name}[.]cs$", "{0} is a domain event: it is in its aggregate's Events folder, in a file of its own", raised.Name);
        }

        foreach (var invariant in invariants)
        {
            FileOf(invariant, files).Should().MatchRegex(
                $"^Aggregates/[^/]+/Invariants/{invariant.Name}[.]cs$",
                "{0} is an invariant: it is in its aggregate's Invariants folder, in a file of its own, as a partial of the class it is nested in", invariant.Name);
        }

        // An id or a status somebody wrote here has a file; one the generator wrote, such as a child entity's id,
        // has none, and is not asked about.
        foreach (var value in values.Where(value => files.Any(file => Path.GetFileName(file) == value.Name + ".cs")))
        {
            FileOf(value, files).Should().MatchRegex(
                $"^(Aggregates/[^/]+/)?ValueObjects/{value.Name}[.]cs$",
                "{0} is an id or a status: it is in the ValueObjects folder of its aggregate, or of the module when several aggregates share it", value.Name);
        }

        // And the other way round: an aggregate's folder holds the aggregate, its refusals and the marker of their
        // texts in English and Dutch beside it, and the four folders, and nothing else. The folder of an aggregate the
        // switch wrote holds what the application adds to it, such as the organization's units, and not the aggregate.
        foreach (var folder in Directory.GetDirectories(Path.Combine(directory, "Aggregates")))
        {
            var here = Directory.GetFiles(folder, "*.cs").Select(Path.GetFileNameWithoutExtension).ToList();
            var aggregates = here.Where(file => roots.Any(root => root.Name == file)).ToList();

            if (aggregates.Count == 0)
            {
                written.Select(root => root.Name + "s").Should().Contain(Path.GetFileName(folder), "{0} is the folder of an aggregate, written by the switch when it has no file", Path.GetFileName(folder));
            }
            else
            {
                aggregates.Should().ContainSingle("{0} is the folder of one aggregate", Path.GetFileName(folder));
            }

            here.Where(file => roots.All(root => root.Name != file) && !file!.EndsWith("Refusals", StringComparison.Ordinal) && !file.EndsWith("Failures", StringComparison.Ordinal))
                .Should().BeEmpty("beside an aggregate's own file there are only its refusals and the marker of their texts: everything else of it has a folder");
            FoldersOf(folder).Should().BeSubsetOf(AggregateFolders, "{0} keeps what belongs to it in these folders", Path.GetFileName(folder));
        }

        // Every kind is there to be found, across the sample's domain projects, or the rules above prove nothing.
        if (project == "Examples.Tenancy.Tenants.Domain")
        {
            written.Should().BeEquivalentTo([typeof(Examples.Tenancy.Tenants.Domain.Organization), typeof(Examples.Tenancy.Tenants.Domain.Role)],
                "Tenants' domain project has the switch write the two classes it adds nothing to");
        }

        if (project == "Examples.Tenancy.Projects.Domain")
        {
            entities.Should().Contain(typeof(CrewMember));
            events.Should().Contain(typeof(CrewRoleGiven));
            invariants.Should().Contain(typeof(Project.OwnerStaysOnTheCrewWithARole));
            values.Should().Contain(typeof(ProjectState));
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void A_projects_root_holds_only_its_entry_points(string project)
    {
        var listed = SampleLayout.Project(project);
        var types = TypeScan.TypesOf(listed.Anchor.Assembly);

        foreach (var file in Directory.GetFiles(SampleLayout.DirectoryOf(listed), "*.cs").Select(Path.GetFileName).Except(EntryFiles))
        {
            // The one other file a root may hold is the project's registration: a static class the module's entry
            // calls, or the entry itself.
            var declared = types.SingleOrDefault(type => !type.IsNested && type.Name == Path.GetFileNameWithoutExtension(file));

            declared.Should().NotBeNull("{0} at the root of {1} declares the type it is named after", file, project);
            (declared!.IsAbstract && declared.IsSealed).Should().BeTrue("{0} is at the root of {1}, so it is the project's registration, a static class", file, project);
            declared.GetMethods().Select(method => method.ReturnType).Should().Contain(
                typeof(IServiceCollection),
                "{0} is at the root of {1}, so it registers the project; anything else belongs in a folder", file, project);
        }
    }

    /// <summary>
    /// One type per file, named after it, so a type is found where its name says. A use case is the exception: the
    /// request, what only it answers with and its handler are one file, in a feature's <c>Commands</c> or
    /// <c>Queries</c> folder.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleProjects))]
    public void A_file_declares_one_type_unless_it_is_a_use_case(string project)
    {
        var directory = DirectoryOfProject(project);
        var files = SampleLayout.SourceFilesIn(directory)
            .Where(file => !EntryFiles.Contains(Path.GetFileName(file)) && !file.Contains("/Migrations/", StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            var declared = TypeDeclaration().Matches(File.ReadAllText(Path.Combine(directory, file))).Select(match => match.Groups["name"].Value).Distinct().ToList();

            // A program is its statements: it declares nothing, or only the class the tests name it by.
            if (file == "Program.cs" && declared.Count == 0)
            {
                continue;
            }
            var folder = Path.GetFileName(Path.GetDirectoryName(file));

            var name = Path.GetFileNameWithoutExtension(file);

            if (folder is SampleLayout.Commands or SampleLayout.Queries)
            {
                declared.Should().Contain(name, "{0} is named after the request it holds", file);
                continue;
            }

            if (folder == "Invariants")
            {
                // An invariant is nested in the class it is about, so its file declares a partial of that class
                // around the one rule the file is named after.
                declared.Should().ContainSingle("{0} holds one rule, in a partial of the class it is nested in", file);
                File.ReadAllText(Path.Combine(directory, file)).Should().MatchRegex($@"\n    public sealed (partial )?class {name}\b", "{0} is named after the rule it holds", file);
                continue;
            }

            // A class of extension methods may be named after its file with "Extensions" behind it, since a class
            // cannot carry the name of the method it adds.
            declared.Should().ContainSingle("{0} declares one type", file)
                .Which.Should().Match(type => type == name || type == name + "Extensions", "{0} is named after the type it declares", file);
        }
    }

    /// <summary>
    /// The namespace of a type is its folder: the project's name, then the folders its file is in. So a type is
    /// found from its name, a file moved to another folder says so in its first lines, and a using says which part
    /// of a project a file leans on.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleProjectsAndTheseTests))]
    public void The_namespace_of_a_type_is_its_folder(string project)
    {
        var directory = DirectoryOfProject(project);
        var files = SampleLayout.SourceFilesIn(directory).ToDictionary(file => file, file => File.ReadAllText(Path.Combine(directory, file)));
        var declared = files
            .Select(file => (File: file.Key, Namespace: NamespaceDeclaration().Match(file.Value)))
            .Where(file => file.Namespace.Success)
            .ToDictionary(file => file.File, file => file.Namespace.Groups["name"].Value);

        // A program that is only its statements declares none: the exporter is its project file and one such file.
        if (files.Keys.Any(file => file != "Program.cs"))
        {
            declared.Should().NotBeEmpty("{0} declares types", project);
        }

        foreach (var (file, name) in declared)
        {
            var folders = Path.GetDirectoryName(file)!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (folders is [.., "Invariants"])
            {
                // A partial of the class the rule is nested in: its namespace is that class's, wherever its file is.
                var owner = TypeDeclaration().Match(files[file]).Groups["name"].Value;
                var ownerFile = declared.Keys.Should().ContainSingle(
                    other => Path.GetFileName(other) == owner + ".cs",
                    "{0} is a partial of {1}, which has a file of its own", file, owner).Subject;

                name.Should().Be(declared[ownerFile], "{0} is a partial of {1}, and a partial has its class's namespace", file, owner);
                continue;
            }

            name.Should().Be(string.Join('.', folders.Prepend(project)), "{0} is in that folder", file);
        }

        // What declares no namespace is a project's entry files and its program, and nothing else.
        files.Keys.Except(declared.Keys).Select(Path.GetFileName)
            .Should().BeSubsetOf([.. EntryFiles, "Program.cs"], "every other file of {0} declares the namespace of its folder", project);
    }

    [Fact]
    public void No_test_class_sits_at_the_root_of_this_project()
    {
        var directory = DirectoryOfProject(TheseTests);

        Directory.GetFiles(directory, "*.cs").Select(Path.GetFileName)
            .Should().Equal(["GlobalUsings.cs"], "a test is in the folder of what it tests: a module and its feature, the host, the persistence, the UI, or the architecture");
    }

    /// <summary>
    /// A sample is an application, and an application is not in the toolkit's namespace. Inside
    /// <c>DDDToolkit.*</c> a name such as <c>HotChocolate</c> is the toolkit's namespace of that name before it is
    /// anybody else's, which the code a generator writes into a project cannot know. So no file of the sample, nor
    /// of the two projects the samples share, declares a namespace that starts with the toolkit's.
    /// </summary>
    [Fact]
    public void No_file_of_the_sample_or_of_the_shared_projects_declares_a_namespace_of_the_toolkit()
    {
        var examples = Path.Combine(SampleLayout.RepositoryRoot(), "Examples");
        string[] folders = [Path.Combine(examples, "Tenancy"), Path.Combine(examples, "Shared")];

        var declared = folders
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(examples, file).Replace('\\', '/'))
            .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
            .SelectMany(file => NamespaceDeclaration().Matches(File.ReadAllText(Path.Combine(examples, file))).Select(match => (File: file, Namespace: match.Groups["name"].Value)))
            .ToList();

        declared.Select(found => found.Namespace).Should().Contain("Examples.Tenancy.Host.Access", "the scan reads the sample")
            .And.Contain("Examples.Hosting", "and what the samples share");
        declared.Where(found => found.Namespace == "DDDToolkit" || found.Namespace.StartsWith("DDDToolkit.", StringComparison.Ordinal))
            .Select(found => $"{found.File}: {found.Namespace}")
            .Should().BeEmpty("a sample's namespaces begin with Examples, as its projects' names do");
    }

    /// <summary>
    /// How long the longest path a build writes for a project may be, counted from the repository's root:
    /// <c>{project folder}/obj/Debug/{framework}/{project name}.GeneratedMSBuildEditorConfig.editorconfig</c>, which
    /// carries the project's name twice. The framework is the one the project file names: an analyzer's
    /// <c>netstandard2.0</c> is seven characters longer than <c>net10.0</c>.
    /// </summary>
    private const int PathBudget = 200;

    /// <summary>
    /// Visual Studio's MSBuild cannot write a path over 259 characters. A project whose name and folder push the
    /// longest file of its build past that fails there, in the design-time build: its restore then sees no central
    /// package versions, and every project that references it reports a missing file. The command line builds it
    /// all the same, so nothing but this test says so before somebody opens the solution. Held for every project
    /// of the repository: the budget leaves the folder the repository is checked out in 58 characters, and the
    /// separator after it.
    /// </summary>
    [Fact]
    public void Every_project_of_the_repository_fits_the_path_budget()
    {
        var root = SampleLayout.RepositoryRoot();
        var projects = ProjectFilesUnder(root)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        projects.Should().Contain(["Source/DDDToolkit/DDDToolkit.csproj", $"Tests/{TheseTests}/{TheseTests}.csproj", "Examples/Tenancy/Examples.Tenancy.Host/Examples.Tenancy.Host.csproj"], "the scan reads the packages, the tests and the samples");

        // Measured under the framework each project builds for, so an analyzer is not measured short.
        LongestBuildPathOf(root, "Source/DDDToolkit.Analyzers/DDDToolkit.Analyzers.csproj")
            .Should().Be("Source/DDDToolkit.Analyzers/obj/Debug/netstandard2.0/DDDToolkit.Analyzers.GeneratedMSBuildEditorConfig.editorconfig");
        LongestBuildPathOf(root, "Source/DDDToolkit/DDDToolkit.csproj")
            .Should().Be("Source/DDDToolkit/obj/Debug/net10.0/DDDToolkit.GeneratedMSBuildEditorConfig.editorconfig");

        var tooLong = projects
            .Select(project => LongestBuildPathOf(root, project))
            .Where(path => path.Length > PathBudget)
            .Select(path => $"{path.Length}: {path}")
            .ToList();

        tooLong.Should().BeEmpty(
            "the longest path a build writes for a project is at most {0} characters below the repository's root: Visual Studio cannot write a path over 259, so a longer one fails to build in any checkout whose own folder takes what is left. Give the project, or the folder it is in, a shorter name",
            PathBudget);
    }

    /// <summary>
    /// The sample runs on Postgres, as Supabase runs it, and on nothing else: no project file and no source file
    /// of it, nor its pages, its texts, its settings and the file of requests beside its host, names SQLite, as a
    /// package, a provider or a word in a comment or a text. What the sample's own files say is read, not what
    /// its packages bring in: the samples' shared hosting project keeps its SQLite for the other sample.
    /// </summary>
    [Fact]
    public void The_sample_names_no_SQLite()
    {
        var sample = Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy");
        string[] read = [".cs", ".csproj", ".http", ".razor", ".resx", ".json"];
        var files = Directory.GetFiles(sample, "*", SearchOption.AllDirectories)
            .Where(file => read.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(sample, file).Replace('\\', '/'))
            .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
            .ToList();

        files.Should().Contain(
            [
                "Examples.Tenancy.Host/Storage/SampleStorage.cs",
                "Examples.Tenancy.Host/Examples.Tenancy.Host.http",
                "Examples.Tenancy.Host/appsettings.Development.json",
                "Examples.Tenancy.Ui/Languages/UiTexts.resx",
                "Modules/Projects/Examples.Tenancy.Projects.Infrastructure/Examples.Tenancy.Projects.Infrastructure.csproj",
            ],
            "the scan reads the host, its requests and its settings, the UI's texts and the modules' project files");
        files.Where(file => File.ReadAllText(Path.Combine(sample, file)).Contains("sqlite", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("the sample has one database: a file that names another describes a sample that is gone");
    }

    /// <summary>
    /// Tenancy's use cases are nested in one class generic over the Tenants module's five classes and four ids. The
    /// toolkit's generator closes it over them in the domain project that declares them, as <c>TenancyUseCases</c>,
    /// which every project above it sees, so no file of the sample, nor of these tests, closes it again: an alias
    /// written by hand is the nine types once more, in every project that names a use case.
    /// </summary>
    [Fact]
    public void No_file_of_the_sample_or_of_these_tests_closes_Tenancys_use_cases_itself()
    {
        var root = SampleLayout.RepositoryRoot();
        string[] folders = [Path.Combine(root, "Examples", "Tenancy"), DirectoryOfProject(TheseTests)];
        var files = folders
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
            .ToDictionary(file => file, file => File.ReadAllText(Path.Combine(root, file)));

        files.Where(file => file.Value.Contains("TenancyUseCases.", StringComparison.Ordinal)).Select(file => file.Key)
            .Should().Contain(
                [
                    "Examples/Tenancy/Modules/Tenants/Examples.Tenancy.Tenants.Application/Seats/Commands/SuspendTenantSeat.cs",
                    "Examples/Tenancy/Modules/Tenants/Examples.Tenancy.Tenants.Api/Seats/GraphQL/KeyOfMineType.cs",
                    "Examples/Tenancy/Examples.Tenancy.Host/Seeding/DemoSeeder.cs",
                    $"Tests/{TheseTests}/Infrastructure/SampleTenants.cs",
                ],
                "the scan reads the projects that name the use cases: the application's, the API's, the host's and these tests'");
        files.Where(file => ClosesTenancysUseCases().IsMatch(file.Value)).Select(file => file.Key)
            .Should().BeEmpty("every project that sees the module's classes sees TenancyUseCases, and names the use cases through it");

        // What a name nested in it stands for is the package's own type, closed over the module's classes and ids:
        // the type the container registered. The class itself is the domain project's, and is only that name.
        typeof(TenancyUseCases).Assembly.Should().BeSameAs(typeof(Tenant).Assembly);
        (typeof(TenancyUseCases).IsAbstract && typeof(TenancyUseCases).Namespace is null).Should().BeTrue();
        Type[] module =
        [
            typeof(Tenant), typeof(TenantId), typeof(Examples.Tenancy.Tenants.Domain.Organization), typeof(OrganizationUnit),
            typeof(OrganizationUnitId), typeof(Seat), typeof(SeatId), typeof(Examples.Tenancy.Tenants.Domain.Role), typeof(RoleId),
        ];
        typeof(TenancyUseCases).BaseType!.GetGenericTypeDefinition().Should().Be(typeof(DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<,,,,,,,,>));
        typeof(TenancyUseCases).BaseType!.GetGenericArguments().Should().Equal(module);

        // Reflection writes a type nested in a generic class with the outer class's type arguments as its own.
        typeof(TenancyUseCases.SeatCommands).DeclaringType.Should().Be(typeof(DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases<,,,,,,,,>));
        typeof(TenancyUseCases.SeatCommands).GetGenericArguments().Should().Equal(module);
    }

    /// <summary>
    /// What is called rather than named is closed over the Tenants module's ids: the system work and the current
    /// caller wherever its classes are seen, through <c>TenancyUseCases</c>, and the registrations in the module's
    /// infrastructure project, where they are generated. So no file of a project that sees those classes names
    /// Tenancy's ids where a closed form exists: the Tenants module's own, the host, the export and these tests. A type
    /// generic over the ids keeps them, as the Tenants module's reads name the selection and the read source. Projects
    /// and Inspections see the ids alone, which say nothing of being Tenancy's, so they name them where they register;
    /// where the ids are arguments, C# infers them there too.
    /// </summary>
    [Fact]
    public void No_project_that_sees_the_tenancy_classes_names_tenancys_ids_where_a_closed_form_exists()
    {
        var root = SampleLayout.RepositoryRoot();
        var sample = Path.Combine(root, "Examples", "Tenancy");
        string[] seeing =
        [
            Path.Combine(sample, "Modules", "Tenants"),
            DirectoryOfProject("Examples.Tenancy.Host"),
            DirectoryOfProject("Examples.Tenancy.Exporter"),
            DirectoryOfProject(TheseTests),
        ];

        Dictionary<string, string> FilesUnder(params string[] folders)
            => folders
                .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
                .ToDictionary(file => file, file => File.ReadAllText(Path.Combine(root, file)));

        FilesUnder(seeing)
            .SelectMany(file => NamesTenancysIdsInACall().Matches(file.Value).Select(found => file.Key + ": " + found.Value))
            .Should().BeEmpty("TenancyUseCases.BeginSystem(), TenancyUseCases.CurrentCaller() and outbox.AddTenancyDomainEvents() name no id");

        // The scan would see it: the modules that see only the ids name them where they register.
        FilesUnder(Path.Combine(sample, "Modules", "Projects"), Path.Combine(sample, "Modules", "Inspections"))
            .Where(file => NamesTenancysIdsInACall().IsMatch(file.Value)).Select(file => file.Key)
            .Should().Contain(
            [
                "Examples/Tenancy/Modules/Projects/Examples.Tenancy.Projects.Infrastructure/ProjectsInfrastructure.cs",
                "Examples/Tenancy/Modules/Inspections/Examples.Tenancy.Inspections.Infrastructure/InspectionsInfrastructure.cs",
            ]);
    }

    /// <summary>
    /// Every project file under <paramref name="directory"/>, wherever it is, but for what a build wrote
    /// (<c>bin</c>, <c>obj</c>), what a package manager fetched and the folders whose name starts with a dot.
    /// </summary>
    private static IEnumerable<string> ProjectFilesUnder(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.csproj"))
        {
            yield return file;
        }

        foreach (var folder in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(folder);
            if (name is "bin" or "obj" or "node_modules" || name.StartsWith('.'))
            {
                continue;
            }

            foreach (var file in ProjectFilesUnder(folder))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// The longest path a build writes for the project at <paramref name="projectFile"/>, from the repository's
    /// root: the editor configuration the compiler is handed, in the project's intermediate folder, under the
    /// longest framework the project file names. One that names none is measured under <c>net10.0</c>, which the
    /// <c>Directory.Build.props</c> above it gives it.
    /// </summary>
    private static string LongestBuildPathOf(string root, string projectFile)
    {
        var folder = projectFile[..projectFile.LastIndexOf('/')];
        var framework = TargetFrameworks().Matches(File.ReadAllText(Path.Combine(root, projectFile)))
            .SelectMany(match => match.Groups["names"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .DefaultIfEmpty("net10.0")
            .MaxBy(name => name.Length);
        return $"{folder}/obj/Debug/{framework}/{Path.GetFileNameWithoutExtension(projectFile)}.GeneratedMSBuildEditorConfig.editorconfig";
    }

    /// <summary>The frameworks a project file builds for, one or several separated by semicolons.</summary>
    [GeneratedRegex(@"<TargetFrameworks?>(?<names>[^<]+)</TargetFrameworks?>")]
    private static partial Regex TargetFrameworks();

    /// <summary>
    /// A type declared at the start of a line: in a file with a file-scoped namespace that is every type that is
    /// not nested in another. Two types of one name, generic and not, count as one.
    /// </summary>
    [GeneratedRegex(@"^(?:public|internal)\s+(?:(?:static|sealed|abstract|partial|readonly)\s+)*(?:class|record\s+struct|record|interface|enum|struct)\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex TypeDeclaration();

    /// <summary>
    /// Tenancy's use cases closed over types: <c>TenancyUseCases&lt;</c> and a type's name. What a test compares with,
    /// the open <c>TenancyUseCases&lt;,,,,,,,,&gt;</c>, closes it over nothing.
    /// </summary>
    [GeneratedRegex(@"TenancyUseCases<\s*[\w.]")]
    private static partial Regex ClosesTenancysUseCases();

    /// <summary>
    /// A call of Tenancy's that names one of its ids where a closed form of it exists: system work or the current
    /// caller with type arguments, or a registration with an id among them.
    /// </summary>
    [GeneratedRegex(@"TenancyWork\.Begin\w*<|TenancyCallers\.Current<|AddTenancy\w*<[^>(]*\b(?:TenantId|SeatId|OrganizationUnitId|RoleId)\b")]
    private static partial Regex NamesTenancysIdsInACall();

    /// <summary>The namespace a file declares: file-scoped, or with braces as a migration's files have it.</summary>
    [GeneratedRegex(@"^namespace\s+(?<name>[\w.]+)\s*[;{]?\s*$", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

    /// <summary>The folder of a project of the sample, or of this test project.</summary>
    private static string DirectoryOfProject(string project)
        => project == TheseTests
            ? Path.Combine(SampleLayout.RepositoryRoot(), "Tests", TheseTests)
            : Path.GetDirectoryName(SampleLayout.SampleProjectFiles().Single(file => Path.GetFileNameWithoutExtension(file) == project))!;

    private static bool IsInvariant(Type type)
        => type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IInvariant<>));

    private static IEnumerable<string?> FoldersOf(string directory)
        => Directory.GetDirectories(directory).Select(Path.GetFileName).Where(folder => folder is not ("bin" or "obj"));

    /// <summary>The one file named after <paramref name="type"/>, by its path from the project's folder.</summary>
    private static string FileOf(Type type, IReadOnlyList<string> files)
        => files.Where(file => Path.GetFileName(file) == type.Name + ".cs").Should()
            .ContainSingle("{0} has a file of its own, named after it", type.Name).Subject;
}
