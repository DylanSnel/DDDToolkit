using System.Reflection;
using FluentAssertions;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// Where a use case lives. In a module's application project the feature comes first and the kind second: one
/// folder per feature, named by a noun of the domain, and inside it <c>Commands</c> and <c>Queries</c>. The API
/// project uses the same feature names, with a feature's routes in <c>Rest</c> and its GraphQL in <c>GraphQL</c>.
/// No folder at a project's root is named after a kind of class.
/// </summary>
/// <remarks>
/// A namespace follows its folder in the application and API projects, so most of this is asked of the assemblies:
/// the requests are read from the host's container, as <see cref="AccessDeclarationTests"/> reads them, and what a
/// route sends from the code of the API projects (<see cref="TypeScan"/>). That the folders are there, under the
/// names the namespaces say, is asked of the source tree. So a use case, a feature or a module that is added is
/// held to these rules without being added here.
/// </remarks>
public sealed class FeatureFolderTests
{
    public static TheoryData<string> Modules => new(SampleLayout.Modules);

    private static IReadOnlyList<HandledRequest> Requests => HostRegistrations.Requests;

    [Fact]
    public void Every_request_lives_with_its_feature_among_its_commands_or_its_queries()
    {
        Requests.Should().NotBeEmpty("the host handles requests, and they are found in its container");

        foreach (var request in Requests)
        {
            var application = ApplicationOf(request);
            var kind = request.IsQuery ? SampleLayout.Queries : SampleLayout.Commands;
            var feature = SampleLayout.FeatureOf(request.Type);

            feature.Should().NotBeNull("{0} is in a feature's folder, not at the root of {1}", request.Type.Name, application.Name);
            request.Type.Namespace.Should().Be(
                $"{application.Name}.{feature}.{kind}",
                "{0} is a {1} of the feature {2}: the feature first, then the kind", request.Type.Name, request.IsQuery ? "query" : "command", feature);
            SampleLayout.KindNames.Should().NotContain(feature, "a feature is named by a noun of the domain, not by a kind of class");
        }

        // Both kinds occur, and a feature may hold both, or this proves little.
        var kinds = Requests.GroupBy(request => (request.Module, Feature: SampleLayout.FeatureOf(request.Type))).ToList();
        kinds.Should().Contain(feature => feature.Any(request => request.IsQuery) && feature.Any(request => !request.IsQuery), "a feature keeps its commands and its queries side by side");
        kinds.Should().Contain(feature => feature.All(request => !request.IsQuery), "and a feature with commands only has no folder of queries");
    }

    [Fact]
    public void A_request_and_its_handler_share_a_namespace_and_a_file()
    {
        foreach (var request in Requests)
        {
            request.Handler.Namespace.Should().Be(request.Type.Namespace, "{0} is next to the request it handles", request.Handler.Name);

            // The folders are the namespace: Crew/Queries/AllCrewMembers.cs holds the query, what only it answers
            // with, and its handler.
            var folders = request.Type.Namespace![(ApplicationOf(request).Name.Length + 1)..].Split('.');
            var file = Path.Combine([SampleLayout.DirectoryOf(ApplicationOf(request)), .. folders, request.Type.Name + ".cs"]);

            File.Exists(file).Should().BeTrue("{0} is in a file of its own, at the path its namespace says: {1}", request.Type.Name, file);
            File.ReadAllText(file).Should().MatchRegex($@"\bclass {request.Handler.Name}\b", "a use case is one file: the request and its handler");
        }
    }

    [Fact]
    public void No_folder_named_after_a_kind_of_class_sits_at_a_projects_root()
    {
        var projects = SampleLayout.SampleProjectFiles().Select(file => Path.GetDirectoryName(file)!).ToList();

        projects.Should().HaveCountGreaterThan(SampleLayout.Projects.Count, "the host and the UI are held to this too");
        foreach (var project in projects)
        {
            Directory.GetDirectories(project).Select(Path.GetFileName)
                .Where(folder => SampleLayout.KindNames.Contains(folder, StringComparer.OrdinalIgnoreCase))
                .Should().BeEmpty("a folder at the root of {0} says which thing its classes are about, not what kind they are", Path.GetFileName(project));
        }
    }

    [Fact]
    public void No_namespace_named_after_a_kind_of_class_sits_at_a_projects_root()
    {
        var assemblies = SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(Program).Assembly).ToList();

        foreach (var assembly in assemblies)
        {
            var root = assembly.GetName().Name!;

            TypeScan.TypesOf(assembly)
                .Where(type => SampleLayout.FeatureOf(type) is { } first && SampleLayout.KindNames.Contains(first, StringComparer.OrdinalIgnoreCase))
                .Select(type => type.FullName)
                .Should().BeEmpty("no namespace right below {0} is named after a kind of class", root);
        }

        // The rule would see one: a request's own kind is in its namespace, one level down.
        Requests.Select(request => request.Type.Namespace!.Split('.')[^1]).Distinct()
            .Should().BeEquivalentTo([SampleLayout.Commands, SampleLayout.Queries]);
    }

    /// <summary>
    /// A layer is a project, so no folder inside one carries a layer's name, nor the name the infrastructure
    /// project gives its storage: <c>Persistence</c> at the root of an application project says where its classes
    /// are implemented, not what they are about. What several features share has a folder named for what it holds,
    /// <c>StoredProjects</c> for the ports the use cases read and save projects through, and holds nothing but
    /// those ports: what a port answers with is a feature's, and lives with that feature.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void A_folder_of_an_application_project_is_a_feature_or_holds_the_ports_several_share(string module)
    {
        foreach (var project in SampleLayout.Projects.Where(project => project.Module == module && project.Layer != Layer.Infrastructure))
        {
            Directory.GetDirectories(SampleLayout.DirectoryOf(project)).Select(Path.GetFileName)
                .Where(folder => SampleLayout.LayerNames.Contains(folder, StringComparer.OrdinalIgnoreCase))
                .Should().BeEmpty("a folder at the root of {0} is named for what it holds, not for a layer or for where its classes are implemented", project.Name);
        }

        var application = SampleLayout.Project(module, Layer.Application);
        var features = Requests.Where(request => request.Module == module).Select(request => SampleLayout.FeatureOf(request.Type)).Append(SampleLayout.Access).ToHashSet();
        var shared = TypeScan.TypesOf(application.Anchor.Assembly)
            .Where(type => !type.IsNested && SampleLayout.FeatureOf(type) is { } folder && !features.Contains(folder))
            .ToList();

        shared.Where(type => !SampleLayout.IsPort(type)).Select(type => type.FullName)
            .Should().BeEmpty("a folder of {0} that is no feature holds the ports several features share, and nothing else", application.Name);

        // Projects and Tenants each have such a folder, or the rule would hold of nothing; Inspections' ports are one feature's.
        if (module != "Inspections")
        {
            shared.Should().NotBeEmpty("{0} keeps the ports its features share in a folder of their own", application.Name);
        }
    }

    /// <summary>
    /// The API project is laid out as the application project is. Everything in it but the entry is a feature's,
    /// under the feature's own name: its routes in <c>{Feature}/Rest</c>, in the one class
    /// <c>{Feature}Endpoints</c>, and its GraphQL in <c>{Feature}/GraphQL</c>, the fields in
    /// <c>{Feature}Queries</c>, <c>{Feature}PagedQueries</c> and <c>{Feature}Mutations</c> with the types they
    /// answer beside them. The fields of a schema the module serves beside its source schema are named after it as
    /// well, <c>{Feature}{Schema}Queries</c>. What the features share is at the project's root, in <c>GraphQL</c> the
    /// registration of the module's schemas and another module's entities as this one names them, and in
    /// <c>Rest</c> what the routes of several features read or write the same way. A request a feature's routes or
    /// fields send is one of that same feature of the application project.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void A_feature_with_routes_has_the_same_name_in_the_API_project(string module)
    {
        var api = SampleLayout.Project(module, Layer.Api);
        var requests = Requests.Where(request => request.Module == module).ToDictionary(request => request.Type);
        var features = requests.Keys.Select(SampleLayout.FeatureOf).ToHashSet();

        var served = TypeScan.TypesOf(api.Anchor.Assembly)
            .Where(type => type.Namespace is { } name && name.StartsWith(api.Name, StringComparison.Ordinal))
            .Select(TypeScan.Outermost)
            .Distinct()
            .Where(type => type != api.Anchor && !SampleLayout.IsWrittenByAGraphQLGenerator(type))
            .ToList();

        served.Should().Contain(type => type.Namespace!.EndsWith("." + SampleLayout.Rest, StringComparison.Ordinal), "{0} serves routes", module)
            .And.Contain(type => type.Namespace!.EndsWith("." + SampleLayout.GraphQL, StringComparison.Ordinal), "and GraphQL");

        // The module's own schema, the one the gateway composes, as the class that registers it names it.
        var sourceSchema = served
            .Where(type => type.Namespace == $"{api.Name}.{SampleLayout.GraphQL}")
            .Select(type => type.GetField("SourceSchemaName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string)
            .Should().ContainSingle(name => name != null, "{0} registers one source schema", module).Subject;

        foreach (var type in served)
        {
            var feature = SampleLayout.FeatureOf(type);
            var sent = TypeScan.Of(TypeScan.WithNested(type)).Uses.Select(use => use.Type).Where(requests.ContainsKey).Distinct().ToList();

            if (feature is SampleLayout.GraphQL or SampleLayout.Rest)
            {
                // The module's own: what puts the features' GraphQL together and what several of them name, and
                // what the routes of several features share, such as how a version travels in a header.
                type.Namespace.Should().Be($"{api.Name}.{feature}", "{0} is the module's, not a feature's", type.Name);
                File.Exists(Path.Combine(SampleLayout.DirectoryOf(api), feature, type.Name + ".cs"))
                    .Should().BeTrue("{0} is in the file its namespace says", type.Name);
                sent.Should().BeEmpty("{0} sends nothing: a route or a field of a feature does", type.Name);
                type.Name.Should().NotEndWith("Endpoints", "a route is a feature's").And.NotEndWith("Queries").And.NotEndWith("Mutations");
                continue;
            }

            features.Should().Contain(feature, "{0} is in the folder of a feature of {1}'s application project", type.Name, module);
            var kind = type.Namespace![(api.Name.Length + feature!.Length + 2)..];

            kind.Should().BeOneOf([SampleLayout.Rest, SampleLayout.GraphQL], "a feature's routes are in its folder under Rest, and its GraphQL under GraphQL");
            File.Exists(Path.Combine(SampleLayout.DirectoryOf(api), feature, kind, type.Name + ".cs"))
                .Should().BeTrue("{0} is in the file its namespace says", type.Name);

            var isFields = type.GetMethods().Any(SampleLayout.IsOperation);
            if (kind == SampleLayout.Rest)
            {
                type.Name.Should().Be($"{feature}Endpoints", "a feature's routes are in one class, named after the feature");
            }
            else if (isFields)
            {
                // The fields that page are in a class of their own, the one kind HotChocolate needs marked as a class.
                // So are the fields of one schema only, and those of another schema than the module's source schema
                // are named after it as well: SeatsAdminQueries. The lookups the gateway alone asks are the source
                // schema's alone, and need no other name: DirectoryQueries.
                var schema = SampleLayout.SchemasOf(type) is [var only] && only != sourceSchema ? char.ToUpperInvariant(only[0]) + only[1..] : string.Empty;
                type.Name.Should().BeOneOf(
                    [$"{feature}{schema}Queries", $"{feature}{schema}Mutations", $"{feature}{schema}PagedQueries"],
                    "a feature's fields are in classes named after the feature, the one schema they belong to if they do, and what they hold");
                (type.Name == $"{feature}PagedQueries").Should().Be(SampleLayout.IsAClassOfPagedFields(type), "{0} is named for whether its fields page", type.Name);
            }

            // The routes and the fields are held to their feature. A type they answer is not: a mutation of one
            // feature answers what it changed, which another feature's query reads.
            if (kind == SampleLayout.Rest || isFields)
            {
                // The lookups hand their ids to data loaders, which send for them.
                if (!type.GetMethods().Any(method => method.IsDefined(typeof(LookupAttribute), inherit: false)))
                {
                    sent.Should().NotBeEmpty("{0} sends requests, and the scan sees it", type.Name);
                }

                sent.Where(request => SampleLayout.FeatureOf(request) != feature)
                    .Select(request => $"{type.Name} sends {request.Name}, of the feature {SampleLayout.FeatureOf(request)}")
                    .Should().BeEmpty("the routes and the fields of a feature send that feature's requests");
            }
        }

        // Nothing is served that is not sent: a request is reached through the routes of its own feature, or it
        // has no route: the two commands that are system work, the query that only a mutation's answer asks, the
        // queries a loader asks for the ids of a whole request, a lookup's and the project roles a crew names, and
        // the two that ask about many projects at once, which only the data loaders of a project's fields send.
        var routed = served.Where(type => type.Namespace!.EndsWith("." + SampleLayout.Rest, StringComparison.Ordinal)).ToList();
        var byRoute = routed.SelectMany(endpoints => TypeScan.Of(TypeScan.WithNested(endpoints)).Uses.Select(use => use.Type)).Where(requests.ContainsKey).ToHashSet();
        requests.Keys.Except(byRoute).Select(request => request.Name)
            .Should().BeSubsetOf(
                [nameof(MarkTenantAsDemo), nameof(SetUpProjectRoles), nameof(InspectionDetail), nameof(ProjectsById), nameof(ProjectRolesById), nameof(InspectionsOfProjects), nameof(ProjectsOpenToRecording)],
                "every other use case of {0} has a route", module);

        // And every command a route sends, a mutation sends too: the two APIs offer the same changes.
        var byField = served.Except(routed).SelectMany(fields => TypeScan.Of(TypeScan.WithNested(fields)).Uses.Select(use => use.Type)).Where(requests.ContainsKey).ToHashSet();
        requests.Values.Where(request => !request.IsQuery && byRoute.Contains(request.Type) && !byField.Contains(request.Type)).Select(request => request.Type.Name)
            .Should().BeEmpty("every command of {0} that has a route has a mutation", module);
    }

    private static ModuleProject ApplicationOf(HandledRequest request)
        => SampleLayout.Projects.Single(project => project.Layer == Layer.Application && project.Anchor.Assembly == request.Type.Assembly);
}
