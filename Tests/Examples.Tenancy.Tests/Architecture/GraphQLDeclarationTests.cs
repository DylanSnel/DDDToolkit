using System.Reflection;
using FluentAssertions;
using GreenDonut;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Execution;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Pagination;
using HotChocolate.Types.Relay;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// How a module declares its GraphQL schema, held to what HotChocolate offers, for every module from this one
/// place: a type is declared over the record the application's queries answer with, a field that takes a read
/// of its own loads through a generated data loader and says what it weighs, a list pages as a connection with
/// HotChocolate's own page sizes, and a class is marked <c>[QueryType]</c> only to hold a paged field. Nothing
/// of that is written by hand a second time.
/// </summary>
/// <remarks>
/// Each of the older ways still compiles, and each was in this sample once: an output record with a mapping
/// beside it, a loader class written by hand, a cursor and a page of a module's own. So nothing but a test keeps
/// them from coming back. What a module has of its own is listed with it in <see cref="Held"/>, each with why,
/// and those lists only shrink.
/// </remarks>
public sealed class GraphQLDeclarationTests(SampleWithoutDatabase sample) : IClassFixture<SampleWithoutDatabase>
{
    /// <summary>What each module's schema is held to, beside the rules all of them share.</summary>
    private static readonly IReadOnlyDictionary<string, Declared> Held = new Dictionary<string, Declared>(StringComparer.Ordinal)
    {
        ["Tenants"] = new(
            Loaders: ["GetSeatByIdAsync", "GetOrganizationUnitByIdAsync", "GetRoleByIdAsync", "GetHeldUnitsByKeyAsync"],
            PagedFields: ["Query.accessHistory", "Query.tenantAccessHistory"],
            ClassesOfPagedFields: ["HistoryPagedQueries", "OperatorsPagedQueries"],
            RecordsOfTheSchemasOwn: new Dictionary<string, string>(StringComparer.Ordinal),
            AnswersOfThePackage: true)
        {
            PagedByThePackage = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Query.tenants"] = "the package's directory of tenants pages by a marker of its own and answers its own page, as the operators' route does; the sample has no query to page",
            },
        },
        ["Projects"] = new(
            Loaders: ["GetProjectByIdAsync", "GetCrewByProjectIdAsync", "GetAbilitiesByProjectIdAsync", "GetHeldKeysByProjectIdAsync", "GetProjectRoleByIdAsync"],
            PagedFields: ["Query.projects"],
            ClassesOfPagedFields: ["OverviewPagedQueries"],
            RecordsOfTheSchemasOwn: new Dictionary<string, string>(StringComparer.Ordinal),
            AnswersOfThePackage: false),
        ["Inspections"] = new(
            Loaders: ["GetInspectionsByProjectIdAsync", "GetOpenToRecordingByProjectIdAsync"],
            PagedFields: ["Project.inspections"],
            ClassesOfPagedFields: [],
            RecordsOfTheSchemasOwn: new Dictionary<string, string>(StringComparer.Ordinal),
            AnswersOfThePackage: false),
    };

    /// <summary>The modules: all of them.</summary>
    public static TheoryData<string> Modules => new(SampleLayout.OnTheMediator.Keys);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void Every_module_is_held_to_these_rules()
        => Held.Keys.Should().BeEquivalentTo(SampleLayout.Projects.Select(project => project.Module).Distinct(), "a module that is not listed here would be held to nothing");

    [Theory]
    [MemberData(nameof(Modules))]
    public void No_data_loader_is_written_by_hand(string module)
    {
        var api = SampleLayout.Project(module, Layer.Api);
        var directory = SampleLayout.DirectoryOf(api);
        string[] written = ["BatchDataLoader<", "GroupedDataLoader<", "CacheDataLoader<", "DataLoaderBase<"];
        Type[] byHand = [typeof(BatchDataLoader<,>), typeof(GroupedDataLoader<,>), typeof(CacheDataLoader<,>)];

        // No file of the project names a loader's base class: what the generator writes is not in the tree.
        SampleLayout.SourceFilesIn(directory)
            .Where(file => written.Any(File.ReadAllText(Path.Combine(directory, file)).Contains))
            .Should().BeEmpty("{0} derives no loader: a static method marked [DataLoader] is one, and HotChocolate's generator writes the class", module);

        // The module has loaders, or the rule would hold of nothing: the methods they are written from, by name.
        SampleLayout.DataLoaderMethodsOf(api.Anchor.Assembly).Select(method => method.Name)
            .Should().BeEquivalentTo(Held[module].Loaders, "these are the reads {0}'s fields load through", module);

        var loaders = TypeScan.TypesOf(api.Anchor.Assembly).Where(type => typeof(IDataLoader).IsAssignableFrom(type)).ToList();
        loaders.Where(loader => loader.IsClass).Should().HaveCount(Held[module].Loaders.Length, "each method marked [DataLoader] has the loader HotChocolate's generator wrote from it");
        loaders.Where(loader => SampleLayout.DataLoaderMethodOf(loader) is null || BasesOf(loader).Any(byHand.Contains)).Select(loader => loader.Name)
            .Should().BeEmpty("a data loader is a static method marked [DataLoader]; HotChocolate's generator writes the class");
        loaders.Where(loader => loader.IsPublic).Select(loader => loader.Name)
            .Should().BeEmpty("a loader is the module's own, like the fields that take it");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_field_that_loads_through_a_data_loader_says_what_it_weighs(string module)
    {
        // HotChocolate weighs a field with a resolver that reads as if every row read on its own. One behind a
        // loader reads once for the whole answer, and says so, or a page of rows that each ask it is estimated to
        // cost more than a request may.
        var behindALoader = OwnTypesOf(module)
            .SelectMany(type => type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.GetParameters().Any(parameter => typeof(IDataLoader).IsAssignableFrom(parameter.ParameterType)))
            .Where(method => !method.IsDefined(typeof(NodeResolverAttribute), inherit: false))
            .ToList();

        behindALoader.Should().NotBeEmpty("{0} has fields that take a loader", module);
        behindALoader.Where(method => !method.IsDefined(typeof(CostAttribute), inherit: false)).Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Should().BeEmpty("a field of {0} that takes a data loader carries [Cost]", module);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task A_list_pages_as_a_connection_with_HotChocolates_sizes_and_no_cursor_or_page_is_the_modules_own(string module)
    {
        // By name, in every project of the module: a cursor, or a page under a name of the module's making.
        var own = SampleLayout.Projects.Where(project => project.Module == module)
            .SelectMany(project => TypeScan.TypesOf(project.Anchor.Assembly))
            .Where(type => type.Namespace?.StartsWith("Examples.Tenancy.", StringComparison.Ordinal) == true)
            .ToList();

        own.Where(type => IsACursorOrAPageByItsName(type.Name)).Select(type => type.FullName)
            .Should().BeEmpty("{0} pages with GreenDonut's PagingArguments and Page<T>, and makes no cursor and no page of its own", module);
        new[] { "ProjectCursor", "InspectionCursor", "PageOfProjects", "PageOfInspections", "ProjectListPage" }
            .Should().OnlyContain(name => IsACursorOrAPageByItsName(name), "the rule would see them: these are the names the modules' own had");

        // In its schema: a field that takes a cursor answers a connection, and pages both ways.
        var paged = (await SchemaOfAsync(module)).Types.OfType<IObjectTypeDefinition>()
            .SelectMany(type => type.Fields.Select(field => (Type: type, Field: field)))
            .Where(found => !found.Field.IsIntrospectionField && found.Field.Arguments.Any(argument => argument.Name == "after"))
            .ToList();

        // A list the Tenancy package pages itself is the exception: its marker and its page are the package's.
        var byThePackage = Held[module].PagedByThePackage.Keys;
        paged.Select(found => $"{found.Type.Name}.{found.Field.Name}").Should().BeEquivalentTo([.. Held[module].PagedFields, .. byThePackage], "these are {0}'s paged lists", module);
        foreach (var (_, field) in paged.Where(found => !byThePackage.Contains($"{found.Type.Name}.{found.Field.Name}")))
        {
            var answered = field.Type.NamedType();
            answered.Name.Should().EndWith("Connection", "{0} takes a cursor", field.Name);
            answered.Should().BeAssignableTo<IObjectTypeDefinition>().Which.Fields.Select(of => of.Name).Should().Contain(["nodes", "edges", "pageInfo"]);
            field.Arguments.Select(argument => argument.Name).Should().Contain(["first", "after", "last", "before"], "a connection pages both ways");
        }

        // And in its code: a paged field says its page sizes itself, and they are HotChocolate's own, ten and fifty,
        // which its cost limits are made for. Said on the field they are in the schema whatever a default becomes
        // elsewhere. The route of the same list keeps its own sizes, and the query its own cap.
        var connections = OwnTypesOf(module)
            .SelectMany(type => type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .SelectMany(method => method.GetCustomAttributesData().Where(attribute => attribute.AttributeType == typeof(UseConnectionAttribute)).Select(attribute => (Method: method, Attribute: attribute)))
            .ToList();

        connections.Should().HaveCount(Held[module].PagedFields.Length, "each paged list of {0} is a method marked [UseConnection]", module);
        connections.Select(found => (
                Field: $"{found.Method.DeclaringType!.Name}.{found.Method.Name}",
                Default: found.Attribute.NamedArguments.Where(named => named.MemberName == nameof(UseConnectionAttribute.DefaultPageSize)).Select(named => named.TypedValue.Value).SingleOrDefault(),
                Largest: found.Attribute.NamedArguments.Where(named => named.MemberName == nameof(UseConnectionAttribute.MaxPageSize)).Select(named => named.TypedValue.Value).SingleOrDefault()))
            .Should().OnlyContain(found => Equals(found.Default, 10) && Equals(found.Largest, 50), "a paged field of {0} says its sizes, and they are HotChocolate's own", module);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_class_is_marked_as_a_query_type_only_to_hold_a_paged_field(string module)
    {
        // A field of an operation type is a static method marked [Query] or [Mutation]. The one exception is a
        // class that holds a paged root field: HotChocolate writes the connection type of a paged field only for
        // a class it generates the type of. The exception reaches no further than that: such a class holds paged
        // fields and nothing else, so a field that does not page, a lookup or a list the package pages itself, is
        // a method marked [Query] like any other.
        var marked = TypeScan.TypesOf(SampleLayout.Project(module, Layer.Api).Anchor.Assembly).Where(type => type.IsDefined(typeof(QueryTypeAttribute), inherit: false)).ToList();

        marked.Select(type => type.Name).Should().BeEquivalentTo(Held[module].ClassesOfPagedFields, "these are {0}'s classes of paged root fields", module);
        marked.Where(type => !SampleLayout.IsAClassOfPagedFields(type)).Select(type => type.Name)
            .Should().BeEmpty("a class of {0} marked [QueryType] holds a paged field", module);
        marked.SelectMany(type => type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static))
            .Where(method => !method.IsDefined(typeof(UseConnectionAttribute), inherit: false))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Should().BeEmpty("a class of {0} marked [QueryType] holds paged fields only: a field that does not page is a method marked [Query]", module);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task A_type_is_declared_over_the_applications_answer_and_no_record_copies_one(string module)
    {
        var api = SampleLayout.Project(module, Layer.Api).Anchor.Assembly;
        var application = SampleLayout.Project(module, Layer.Application).Anchor.Assembly;
        const BindingFlags Statics = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var own = OwnTypesOf(module);

        own.Where(type => type.Name.EndsWith("Output", StringComparison.Ordinal)).Select(type => type.Name)
            .Should().BeEmpty("{0} answers with the application's records; a record that copies one is a second place to change", module);
        own.Where(type => type.GetMethods(Statics).Any(method => method.Name is "From" or "Of" && method.ReturnType == type)).Select(type => type.Name)
            .Should().BeEmpty("no type of {0}'s schema is made from another by a mapping", module);

        // A class marked [ObjectType<T>] is static, is named after the type it declares, and declares it over T:
        // an answer of the module's application project, a record of the Tenancy package where the module answers
        // with those, or a reference to another module's entity, which is its key and nothing else.
        var objectTypes = (await SchemaOfAsync(module)).Types.OfType<ObjectType>().ToDictionary(type => type.Name, StringComparer.Ordinal);
        var declared = own.Select(type => (Class: type, Over: Declares(type))).Where(found => found.Over is not null).ToList();

        declared.Should().NotBeEmpty("{0} declares its types in type classes", module);
        foreach (var (typeClass, over) in declared)
        {
            (typeClass.IsAbstract && typeClass.IsSealed).Should().BeTrue("{0} is a static class: it holds what the schema adds to {1}, and no data", typeClass.Name, over!.Name);
            typeClass.Name.Should().EndWith("Type", "a type class is named after the type it declares");
            objectTypes.Should().ContainKey(typeClass.Name[..^"Type".Length], "{0} declares the type it is named after", typeClass.Name)
                .WhoseValue.RuntimeType.Should().Be(over, "{0} declares it over the record it names", typeClass.Name);

            if (over!.Assembly == api)
            {
                IsAReferenceByKey(over).Should().BeTrue("{0} is declared over an answer of the application, or over a reference of this project, which holds the key alone", typeClass.Name);
                typeClass.IsDefined(typeof(EntityKeyAttribute), inherit: false).Should().BeTrue("{0} is an entity the gateway resolves by its key", typeClass.Name);
            }
            else
            {
                Assembly[] answers = Held[module].AnswersOfThePackage ? [application, typeof(TenancyUseCases.KeyReach).Assembly] : [application];
                answers.Should().Contain(over.Assembly, "{0} is declared over what {1}'s application layer answers", typeClass.Name, module);
            }
        }

        // What is left of records the API project declares for the schema's sake is listed, each with its reason:
        // a reference by key, a connection and what a generator wrote are none of them.
        objectTypes.Values
            .Where(type => type.RuntimeType.Assembly == api && !SampleLayout.IsWrittenByAGraphQLGenerator(type.RuntimeType))
            .Where(type => !IsAReferenceByKey(type.RuntimeType) && !typeof(IConnection).IsAssignableFrom(type.RuntimeType))
            .Select(type => type.Name)
            .Should().BeEquivalentTo(Held[module].RecordsOfTheSchemasOwn.Keys, "every other type of {0}'s schema is a record its application layer answers", module);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void The_application_project_names_nothing_of_HotChocolate_but_the_paging_primitives(string module)
    {
        // What a paged query takes and answers comes from a package of primitives that depends on nothing. The
        // schema, the resolvers and the loaders stay the API project's.
        var referenced = SampleLayout.Project(module, Layer.Application).Anchor.Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToList();

        referenced.Where(name => name.StartsWith("HotChocolate", StringComparison.Ordinal) || (name.StartsWith("GreenDonut", StringComparison.Ordinal) && name != "GreenDonut.Data.Primitives"))
            .Should().BeEmpty("a use case of {0} knows no schema", module);
        referenced.Should().Contain("GreenDonut.Data.Primitives", "{0} has a paged query, so the rule would see a page", module);
    }

    /// <summary>What <paramref name="module"/> wrote into its API project for its schema: the types of its GraphQL folders, and nothing a generator wrote.</summary>
    private static List<Type> OwnTypesOf(string module)
        => [.. TypeScan.TypesOf(SampleLayout.Project(module, Layer.Api).Anchor.Assembly)
            .Where(type => type.Namespace?.EndsWith("." + SampleLayout.GraphQL, StringComparison.Ordinal) == true && type.DeclaringType is null)
            .Where(type => !SampleLayout.IsWrittenByAGraphQLGenerator(type))];

    /// <summary>The source schema of <paramref name="module"/>, as the host builds it.</summary>
    private async Task<global::HotChocolate.ISchemaDefinition> SchemaOfAsync(string module)
    {
        return (await sample.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync(module.ToLowerInvariant(), Cancellation)).Schema;
    }

    private static bool IsACursorOrAPageByItsName(string name)
        => name.EndsWith("Cursor", StringComparison.Ordinal) || name.StartsWith("PageOf", StringComparison.Ordinal) || name.EndsWith("ListPage", StringComparison.Ordinal);

    /// <summary>The record a class marked <c>[ObjectType&lt;T&gt;]</c> declares a type over, or <see langword="null"/>.</summary>
    private static Type? Declares(Type type)
        => type.GetCustomAttributesData()
            .Select(attribute => attribute.AttributeType)
            .FirstOrDefault(attribute => attribute.IsGenericType && attribute.GetGenericTypeDefinition() == typeof(ObjectTypeAttribute<>))
            ?.GetGenericArguments()[0];

    /// <summary>Whether <paramref name="type"/> is another module's entity as this one names it: a record whose one member is the key.</summary>
    private static bool IsAReferenceByKey(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => property.Name)
            .SequenceEqual(["Id"]);

    private static IEnumerable<Type> BasesOf(Type type)
    {
        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            yield return parent.IsGenericType ? parent.GetGenericTypeDefinition() : parent;
        }
    }

    /// <summary>What one module's schema has of its own.</summary>
    /// <param name="Loaders">The methods marked <c>[DataLoader]</c> its loaders are written from.</param>
    /// <param name="PagedFields">Its paged lists, each as the type and the field.</param>
    /// <param name="ClassesOfPagedFields">Its classes marked <c>[QueryType]</c>: those that hold a paged root field.</param>
    /// <param name="RecordsOfTheSchemasOwn">
    /// The types of its schema that are records of the API project itself, each with why it has no record of the
    /// application behind it.
    /// </param>
    /// <param name="AnswersOfThePackage">Whether its application layer answers with records of the Tenancy package as well as its own.</param>
    private sealed record Declared(
        string[] Loaders,
        string[] PagedFields,
        string[] ClassesOfPagedFields,
        IReadOnlyDictionary<string, string> RecordsOfTheSchemasOwn,
        bool AnswersOfThePackage)
    {
        /// <summary>
        /// Its fields that take a marker and answer no connection, each with why: a list the Tenancy package pages
        /// itself, whose page the field hands on as the route does.
        /// </summary>
        public IReadOnlyDictionary<string, string> PagedByThePackage { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
