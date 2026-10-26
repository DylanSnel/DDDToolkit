using System.Reflection;
using DDDToolkit.Supporting.Tenancy.UseCases;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>What a project of a module is, which decides the rules it is held to.</summary>
public enum Layer
{
    /// <summary>What other modules may name: ids, keys, questions they may ask.</summary>
    Contracts,

    /// <summary>The classes and their rules.</summary>
    Domain,

    /// <summary>
    /// The use cases, each a command or a query with its handler, in the folder of the feature it belongs to, and
    /// the ports they read and save through.
    /// </summary>
    Application,

    /// <summary>
    /// The context, its migrations with the design-time factory they are found through, the adapters that
    /// implement the ports, the row access rules, and their registration.
    /// </summary>
    Infrastructure,

    /// <summary>
    /// What a host adds: the module's entry, and what serves its use cases to callers, the REST routes and a
    /// GraphQL project next to them.
    /// </summary>
    Api,
}

/// <summary>A project of a module of the sample: its name, one public type of its assembly, and its layer.</summary>
/// <param name="Name">The project's name, which is its assembly's.</param>
/// <param name="Anchor">A public type of the assembly. Nothing is made visible to the tests for it.</param>
/// <param name="Layer">The layer the project is, said here rather than read off its name.</param>
public sealed record ModuleProject(string Name, Type Anchor, Layer Layer)
{
    /// <summary>The module the project is of: the third part of its name.</summary>
    public string Module => Name.Split('.')[2];

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The Tenancy sample as the architecture tests see it: every project of every module with its layer, and what each
/// module's requests declare their access through.
/// </summary>
/// <remarks>
/// The one place a project or a module is listed. A project added to a module fails
/// <c>The_modules_hold_these_projects_and_no_others</c> until it is listed here, and is then held to its layer's
/// rules; a module whose use cases become requests is listed in <see cref="OnTheMediator"/>, and is then held to
/// the rules for requests.
/// </remarks>
public static class SampleLayout
{
    /// <summary>
    /// The modules' projects. The layer is said here, not read off the name, so a project whose name ends
    /// otherwise, such as a GraphQL project, is held to the rules of the layer it is. The anchor of an API project
    /// is the module's entry, and that of an infrastructure project its registration.
    /// </summary>
    public static IReadOnlyList<ModuleProject> Projects { get; } =
    [
        new("Examples.Tenancy.Tenants.Contracts", typeof(TenantId), Layer.Contracts),
        new("Examples.Tenancy.Tenants.Domain", typeof(Tenant), Layer.Domain),
        new("Examples.Tenancy.Tenants.Application", typeof(TenantSeats), Layer.Application),
        new("Examples.Tenancy.Tenants.Infrastructure", typeof(TenantsInfrastructure), Layer.Infrastructure),
        new("Examples.Tenancy.Tenants.Api", typeof(TenantsModule), Layer.Api),
        new("Examples.Tenancy.Projects.Contracts", typeof(ProjectId), Layer.Contracts),
        new("Examples.Tenancy.Projects.Domain", typeof(Project), Layer.Domain),
        new("Examples.Tenancy.Projects.Application", typeof(VisibleProjects), Layer.Application),
        new("Examples.Tenancy.Projects.Infrastructure", typeof(ProjectsInfrastructure), Layer.Infrastructure),
        new("Examples.Tenancy.Projects.Api", typeof(ProjectsModule), Layer.Api),
        new("Examples.Tenancy.Inspections.Domain", typeof(Inspection), Layer.Domain),
        new("Examples.Tenancy.Inspections.Application", typeof(ProjectInspections), Layer.Application),
        new("Examples.Tenancy.Inspections.Infrastructure", typeof(InspectionsInfrastructure), Layer.Infrastructure),
        new("Examples.Tenancy.Inspections.Api", typeof(InspectionsModule), Layer.Api),
    ];

    /// <summary>
    /// The project that holds what two or more modules use and none owns. It is no module's, so it is not among
    /// <see cref="Projects"/>: a module's domain and contracts projects may reference it, and it references no module.
    /// </summary>
    public const string SharedDomain = "Examples.Tenancy.Shared.Domain";

    /// <summary>The project file of <see cref="SharedDomain"/>, beside the modules' folder.</summary>
    public static string SharedDomainFile()
        => Path.GetFullPath(Path.Combine(RepositoryRoot(), "Examples", "Tenancy", "Shared", SharedDomain, SharedDomain + ".csproj"));

    /// <summary>
    /// The project that holds what the modules' infrastructure projects do the same way and none of them owns:
    /// the check a paged read makes of the marker it is asked with.
    /// </summary>
    public const string SharedInfrastructure = "Examples.Tenancy.Shared.Infrastructure";

    /// <summary>The project file of <see cref="SharedInfrastructure"/>, beside the modules' folder.</summary>
    public static string SharedInfrastructureFile()
        => Path.GetFullPath(Path.Combine(RepositoryRoot(), "Examples", "Tenancy", "Shared", SharedInfrastructure, SharedInfrastructure + ".csproj"));

    /// <summary>The modules, in the order the host adds them.</summary>
    public static IReadOnlyList<string> Modules { get; } = [.. Projects.Select(project => project.Module).Distinct()];

    /// <summary>
    /// The modules whose use cases are commands and queries of the mediator, each with the interface its requests
    /// declare their access through and the pipeline behavior that holds them to what they declare.
    /// </summary>
    /// <remarks>
    /// Found, not listed: a module is one of them when the <c>Access</c> namespace of its application project holds
    /// its vocabulary, <c>I{Module}Request</c>, which the module writes, and <c>{Module}AccessBehavior&lt;,&gt;</c>,
    /// which the toolkit's generator writes beside the interface and names after it. So a module that gains
    /// its first command or query is held to the rules for requests without being added anywhere, and
    /// <c>Every_command_and_query_declares_its_access</c> fails for a request of a module that
    /// has no such vocabulary.
    /// </remarks>
    public static IReadOnlyDictionary<string, (Type Request, Type Behavior)> OnTheMediator { get; } = Modules
        .Select(module => (Module: module, Application: Project(module, Layer.Application).Anchor.Assembly))
        .Select(found => (
            found.Module,
            Request: found.Application.GetType($"{found.Application.GetName().Name}.Access.I{found.Module}Request"),
            Behavior: found.Application.GetType($"{found.Application.GetName().Name}.Access.{found.Module}AccessBehavior`2")))
        .Where(found => found.Request is not null && found.Behavior is not null)
        .ToDictionary(found => found.Module, found => (found.Request!, found.Behavior!), StringComparer.Ordinal);

    /// <summary>The two folders a feature keeps its use cases in.</summary>
    public const string Commands = "Commands", Queries = "Queries";

    /// <summary>
    /// Every port the modules declare: each interface of a module's application project that a type of its
    /// infrastructure project implements, the nested and private ones included.
    /// </summary>
    private static readonly Lazy<IReadOnlySet<Type>> Ports = new(() => Modules
        .SelectMany(module =>
        {
            var application = Project(module, Layer.Application).Anchor.Assembly;
            return TypeScan.TypesOf(Project(module, Layer.Infrastructure).Anchor.Assembly)
                .SelectMany(adapter => adapter.GetInterfaces())
                .Where(implemented => implemented.Assembly == application);
        })
        .ToHashSet());

    /// <summary>The project with this name.</summary>
    public static ModuleProject Project(string name) => Projects.Single(project => project.Name == name);

    /// <summary>The project of <paramref name="module"/> at <paramref name="layer"/>.</summary>
    public static ModuleProject Project(string module, Layer layer) => Projects.Single(project => project.Module == module && project.Layer == layer);

    /// <summary>Whether a project of this name is listed.</summary>
    public static bool Holds(string name) => Projects.Any(project => project.Name == name);

    /// <summary>The module's entry: the one public type of its API project.</summary>
    public static Type EntryOf(string module) => Project(module, Layer.Api).Anchor;

    /// <summary>The registration of the module's infrastructure project, which the entry calls.</summary>
    public static Type InfrastructureOf(string module) => Project(module, Layer.Infrastructure).Anchor;

    /// <summary>
    /// Whether <paramref name="type"/> is a port: an interface a module's application project declares and its
    /// infrastructure project implements, or the Tenancy package's store, which its use cases load and save
    /// through. What a port answers with, the records next to it, is data, not a port: whoever holds it reaches no
    /// storage through it.
    /// </summary>
    /// <remarks>
    /// A port is told by what it is, not by where it sits. One that a single feature uses lives in that feature's
    /// folder, and one that several share in a folder at the project's root named for what it gives them, such as
    /// <c>StoredProjects</c>, so no namespace says "this is a port"; that an adapter of the infrastructure project
    /// implements it does.
    /// </remarks>
    public static bool IsPort(Type type)
        => Ports.Value.Contains(type)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TenancyUseCases<,,,,,,,,>.IStore));

    /// <summary>
    /// The names no folder and no namespace at a project's root may have: each says what kind of class is inside,
    /// where a folder there says which thing its classes are about.
    /// </summary>
    public static IReadOnlyList<string> KindNames { get; } = [Commands, Queries, "Handlers", "Validators", "Ports", "Dtos"];

    /// <summary>
    /// The names no folder at the root of a contracts, domain, application or API project may have: a layer's, or
    /// the one an infrastructure project gives its storage. A folder there says which thing its classes are
    /// about; a layer is a project, and <c>Persistence</c> is where the infrastructure project keeps the adapters.
    /// </summary>
    public static IReadOnlyList<string> LayerNames { get; } = ["Contracts", "Domain", "Application", "Infrastructure", "Persistence", "Api"];

    /// <summary>
    /// The folder of an application project in which the access check lives. It is a feature of its own, whether
    /// or not it has a query: the requirement, the behavior that checks it and what the check reads are in it.
    /// </summary>
    public const string Access = "Access";

    /// <summary>
    /// The feature a type of an application or an API project belongs to: the first part of its namespace after
    /// the project's own, which is the folder it is in; <see langword="null"/> for a type at the project's root.
    /// </summary>
    public static string? FeatureOf(Type type)
    {
        var project = type.Assembly.GetName().Name!;
        return type.Namespace is { } name && name.StartsWith(project + ".", StringComparison.Ordinal)
            ? name[(project.Length + 1)..].Split('.')[0]
            : null;
    }

    /// <summary>The folder of an API project that holds a feature's routes, and the one that holds its GraphQL.</summary>
    public const string Rest = "Rest", GraphQL = "GraphQL";

    /// <summary>
    /// Whether a generator wrote <paramref name="type"/> into an API project for its GraphQL schema: the
    /// registration of the module's ids as scalars and of its classes of one schema, which the toolkit's generator
    /// writes, or what HotChocolate's own writes for the project's operations, the registration and the
    /// <c>Query</c> and <c>Mutation</c> types that hold them as fields, the data loader it writes from a method
    /// marked <c>[DataLoader]</c>, with the loader's interface, and the types of its connections. They are classes
    /// nobody of the sample wrote, so a rule about what an API project declares leaves them out.
    /// </summary>
    public static bool IsWrittenByAGraphQLGenerator(Type type)
    {
        var declared = TypeScan.Outermost(type);
        return (declared.Namespace == declared.Assembly.GetName().Name + ".GraphQl" && declared.Name == "HotChocolateExtensions")
            || (declared.Namespace == "Microsoft.Extensions.DependencyInjection" && GeneratedByHotChocolate.Any(ending => declared.Name.EndsWith(ending, StringComparison.Ordinal)))
            || DataLoaderMethodOf(declared) is not null
            || IsAGeneratedConnectionType(declared);
    }

    /// <summary>
    /// Whether <paramref name="type"/> is the object type HotChocolate's generator wrote for a connection class
    /// of the sample, or for its edges: a field that pages answers a class derived from HotChocolate's connection,
    /// and the type of that class is generated beside it.
    /// </summary>
    private static bool IsAGeneratedConnectionType(Type type)
        => type.BaseType is { IsGenericType: true } described
            && described.GetGenericTypeDefinition() == typeof(global::HotChocolate.Types.ObjectType<>)
            && (typeof(global::HotChocolate.Types.Pagination.IConnection).IsAssignableFrom(described.GenericTypeArguments[0])
                || typeof(global::HotChocolate.Types.Pagination.IEdge).IsAssignableFrom(described.GenericTypeArguments[0]));

    /// <summary>How the classes HotChocolate's generator writes for a project end, after the name the project gave it.</summary>
    private static readonly string[] GeneratedByHotChocolate = ["TypesRequestExecutorBuilderExtensions", "TypesQueryType", "TypesMutationType", "TypesSubscriptionType"];

    /// <summary>
    /// The method HotChocolate's generator wrote the data loader <paramref name="loader"/> from, or
    /// <see langword="null"/> when nobody generated it: a loader's class, and its interface with an <c>I</c> in
    /// front, are named after a method marked <c>[DataLoader]</c> in the same namespace, <c>GetSeatByIdAsync</c>
    /// giving <c>SeatByIdDataLoader</c>.
    /// </summary>
    public static MethodInfo? DataLoaderMethodOf(Type loader)
    {
        if (!typeof(global::GreenDonut.IDataLoader).IsAssignableFrom(loader))
        {
            return null;
        }

        return DataLoaderMethodsOf(loader.Assembly).FirstOrDefault(method =>
            method.DeclaringType!.Namespace == loader.Namespace
            && LoaderNameOf(method) is var name
            && (loader.Name == name || (loader.IsInterface && loader.Name == "I" + name)));
    }

    /// <summary>Every method of an assembly marked <c>[DataLoader]</c>: what HotChocolate's generator writes a loader from.</summary>
    public static IReadOnlyList<MethodInfo> DataLoaderMethodsOf(Assembly assembly)
        => [.. TypeScan.TypesOf(assembly)
            .SelectMany(type => type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(method => method.IsDefined(typeof(global::GreenDonut.DataLoaderAttribute), inherit: false))];

    /// <summary>The name HotChocolate's generator gives the loader of a method: the method's, without <c>Get</c> and <c>Async</c>.</summary>
    private static string LoaderNameOf(MethodInfo method)
    {
        var name = method.Name;
        name = name.StartsWith("Get", StringComparison.Ordinal) ? name[3..] : name;
        name = name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
        return name + "DataLoader";
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a field of a schema's <c>Query</c>, <c>Mutation</c> or
    /// <c>Subscription</c> type: it says so itself, with HotChocolate's attribute, or it is a public method of a
    /// class of paged fields (<see cref="IsAClassOfPagedFields"/>), whose every public method is a field, or a public
    /// static method of a class of one schema (<see cref="SchemasOf"/>), whose every such method is one.
    /// </summary>
    public static bool IsOperation(MethodInfo method)
        => method.IsDefined(typeof(global::HotChocolate.QueryAttribute), inherit: false)
            || method.IsDefined(typeof(global::HotChocolate.MutationAttribute), inherit: false)
            || method.IsDefined(typeof(global::HotChocolate.SubscriptionAttribute), inherit: false)
            || (method is { IsPublic: true, DeclaringType: { } declaring } && IsAClassOfPagedFields(declaring))
            || (method is { IsPublic: true, IsStatic: true, DeclaringType: { } ofOneSchema } && SchemasOf(ofOneSchema).Count > 0
                && !method.IsDefined(typeof(global::HotChocolate.GraphQLIgnoreAttribute), inherit: false));

    /// <summary>
    /// The schemas a class of fields is marked for with <c>[GraphQLSchema]</c>, by name: the one it belongs to, and
    /// none for a class every schema has.
    /// </summary>
    public static IReadOnlyList<string> SchemasOf(Type type)
        => [.. type.GetCustomAttributes<global::DDDToolkit.HotChocolate.Attributes.GraphQLSchemaAttribute>(inherit: false).Select(schema => schema.Name).Distinct()];

    /// <summary>
    /// Whether <paramref name="type"/> is a class marked <c>[QueryType]</c> that holds a paged field: the one
    /// case in which the sample marks the class and not the method. HotChocolate writes a paged field's connection
    /// type only for a class it generates the type of; a method marked <c>[Query]</c> is bound without it. Such a
    /// class holds paged fields and nothing else, and is named <c>{Feature}PagedQueries</c>:
    /// <c>GraphQLDeclarationTests</c> holds it to both.
    /// </summary>
    public static bool IsAClassOfPagedFields(Type type)
        => type.IsDefined(typeof(global::HotChocolate.Types.QueryTypeAttribute), inherit: false)
            && type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.IsDefined(typeof(global::HotChocolate.Types.UseConnectionAttribute), inherit: false));

    /// <summary>Whether an assembly or package of this name is Entity Framework, a provider of it, or built on it.</summary>
    public static bool IsEntityFramework(string name)
        => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || name.StartsWith("Npgsql", StringComparison.Ordinal)
        || name.Contains(".EntityFramework", StringComparison.Ordinal);

    /// <summary>Whether a package or assembly of this name is the mediator: its abstractions or its generator.</summary>
    public static bool IsMediator(string name) => name == "Mediator" || name.StartsWith("Mediator.", StringComparison.Ordinal);

    /// <summary>
    /// Whether a project of this name is one that knows how a module is stored: its infrastructure project, or a
    /// project of the sample nobody listed whose name says it is infrastructure.
    /// </summary>
    public static bool IsStorageProject(string name)
        => Projects.FirstOrDefault(project => project.Name == name) is { } known
            ? known.Layer == Layer.Infrastructure
            : name.StartsWith("Examples.Tenancy.", StringComparison.Ordinal) && name.Contains(".Infrastructure", StringComparison.Ordinal);

    /// <summary>
    /// The directory that holds DDDToolkit.slnx, found from where the tests run. Not from the source file's path:
    /// a CI build maps source paths, so the path the compiler recorded does not exist on the runner.
    /// </summary>
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DDDToolkit.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No DDDToolkit.slnx above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>The project file of a module's project, at the path its module's folder gives it.</summary>
    public static string FileOf(ModuleProject project)
        => Path.GetFullPath(Path.Combine(RepositoryRoot(), "Examples", "Tenancy", "Modules", project.Module, project.Name, project.Name + ".csproj"));

    /// <summary>
    /// The project file of every project of the sample: the modules' projects, and the host, the UI and the
    /// AppHost next to them.
    /// </summary>
    public static IReadOnlyList<string> SampleProjectFiles()
        => [.. Directory.GetFiles(Path.Combine(RepositoryRoot(), "Examples", "Tenancy"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    /// <summary>The folder of a module's project.</summary>
    public static string DirectoryOf(ModuleProject project) => Path.GetDirectoryName(FileOf(project))!;

    /// <summary>
    /// The source files under <paramref name="directory"/> that somebody wrote, each by its path from there with
    /// forward slashes: what the build leaves in <c>bin</c> and <c>obj</c> is not among them.
    /// </summary>
    public static IReadOnlyList<string> SourceFilesIn(string directory)
        => [.. Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith("bin/", StringComparison.Ordinal) && !file.StartsWith("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    /// <summary>The host's project file.</summary>
    public static string HostProjectFile() => ProgramProjectFile("Examples.Tenancy.Host");

    /// <summary>
    /// The project that holds the application's permission catalogue. It is no module's, so it is not among
    /// <see cref="Projects"/>: the host runs with it, and the program that exports writes the policies from it.
    /// </summary>
    public const string Catalogue = "Examples.Tenancy.Catalogue";

    /// <summary>The program that writes the files under <c>Examples/Tenancy/supabase/migrations</c>, and nothing else.</summary>
    public const string Exporter = "Examples.Tenancy.Exporter";

    /// <summary>The project file of a project of the sample that is no module's: the host, the exporter, the catalogue.</summary>
    public static string ProgramProjectFile(string project)
        => Path.GetFullPath(Path.Combine(RepositoryRoot(), "Examples", "Tenancy", project, project + ".csproj"));
}
