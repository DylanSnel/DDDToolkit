using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.HotChocolate.Authorization;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Examples.Tenancy.Shared.Infrastructure.Paging;
using FluentAssertions;
using GreenDonut;
using GreenDonut.Data;
using Mediator;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// The layers of the sample's modules, held to what their project files, their restored dependencies and their
/// assemblies say. Each module is split into a domain, an application, an infrastructure and an API project,
/// and Tenancy and Projects into a contracts project besides. Nothing but convention keeps a domain project from
/// referencing Entity Framework, a route from asking the container for a port, or the host from naming a context,
/// so these tests do.
/// </summary>
/// <remarks>
/// What the tests hold, from the outside in:
/// <list type="bullet">
/// <item><b>The host</b> references each module's API project and nothing else of a module, and the catalogue, names nothing of an
/// infrastructure project, and takes no port, no handler and no service of a module: like a route, it sends.</item>
/// <item><b>An API project</b> has one public type, the module's entry, which composes the module: it calls the
/// infrastructure project's registration and the application project's, and maps the routes. Nothing else in the
/// project names the infrastructure project, Entity Framework or a port, and a route takes nothing the host
/// registers but the sender.</item>
/// <item><b>Only the mediator</b> reaches a handler, and a port is named only by the handlers and access types
/// that use it and the adapters that implement it.</item>
/// <item><b>The shared domain project</b> references no module, and only a module's domain and contracts projects
/// reference it.</item>
/// <item><b>The inner layers</b> reach neither storage nor HTTP; a project reaches only the layers of its module
/// below it; a module reaches another only through its contracts.</item>
/// </list>
/// The project files and the restore are the primary check for references. The compiler drops a reference an
/// assembly never uses, and the dependency manifest does not record a framework reference, so an assembly's
/// metadata can look clean while its project would let the next change reach Entity Framework or ASP.NET Core. So
/// each project's references are walked to their closure through the project files, whatever the project does
/// with them, and the packages and frameworks its restore resolved are added to it, which is where a package that
/// brings Entity Framework in under another name shows. What a type names is read from the assemblies themselves
/// (<see cref="TypeScan"/>). Only public types are named, and nothing is made visible to the tests for them.
/// </remarks>
public sealed class LayerReferenceTests
{
    private static readonly ConcurrentDictionary<Assembly, TypeScan> Scans = new();

    public static TheoryData<string> Projects => new(SampleLayout.Projects.Select(project => project.Name));

    public static TheoryData<string> InnerProjects => new(Named(Layer.Contracts, Layer.Domain, Layer.Application));

    public static TheoryData<string> ContractsAndDomainProjects => new(Named(Layer.Contracts, Layer.Domain));

    public static TheoryData<string> ApiProjects => new(Named(Layer.Api));

    public static TheoryData<string> ModulesOnTheMediator => new(SampleLayout.OnTheMediator.Keys);

    // ------------------------------------------------------------------ the projects and their references

    [Fact]
    public void The_modules_hold_these_projects_and_no_others()
    {
        var found = Directory.GetFiles(Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension);

        found.Should().BeEquivalentTo(SampleLayout.Projects.Select(project => project.Name), "every project of a module of the sample is listed with its layer, so the rules below reach it");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_project_declares_its_module(string project)
    {
        var listed = SampleLayout.Project(project);
        var assembly = listed.Anchor.Assembly;

        assembly.GetName().Name.Should().Be(project, "the anchor is a type of the project it stands for");
        assembly.GetCustomAttribute<ModuleAttribute>().Should().NotBeNull("[assembly: Module] makes {0} one of its module's projects", project)
            .And.Subject.As<ModuleAttribute>().Name.Should().Be(listed.Module);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_project_is_declared_its_module_by_the_folder_it_is_in_and_by_no_file_of_its_own(string project)
    {
        // Modules/Directory.Build.props names the module after the folder and declares it, so the folder is the one
        // place that says which module a project is of: a project moved to another module's folder is that module's.
        var listed = SampleLayout.Project(project);
        var directory = SampleLayout.DirectoryOf(listed);

        Path.GetFileName(Path.GetDirectoryName(directory)).Should().Be(listed.Module, "{0} is in its module's folder", project);
        listed.Anchor.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(metadata => metadata.Key == "DDD_Module")
            .Select(metadata => metadata.Value)
            .Should().Equal([listed.Module], "the build declared {0}'s module from its folder", project);

        SampleLayout.SourceFilesIn(directory)
            .Where(file => File.ReadAllText(Path.Combine(directory, file)) is var text
                && (text.Contains("[assembly: Module(", StringComparison.Ordinal) || text.Contains("Abstractions.Attributes.Module", StringComparison.Ordinal)))
            .Should().BeEmpty("no file of {0} declares the module its folder gives it", project);
    }

    [Theory]
    [MemberData(nameof(InnerProjects))]
    public void Contracts_domain_and_application_reach_neither_storage_nor_HTTP(string project)
    {
        Closure(project).Where(reached => SampleLayout.IsEntityFramework(reached.Key) || IsAspNetCore(reached.Key))
            .Select(Describe)
            .Should().BeEmpty("{0} knows nothing of storage or HTTP: the infrastructure project stores what it declares, and the API project serves it", project);

        SampleLayout.Project(project).Anchor.Assembly.GetReferencedAssemblies().Select(name => name.Name!)
            .Where(name => SampleLayout.IsEntityFramework(name) || IsAspNetCore(name))
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void No_project_reaches_a_layer_of_its_module_above_or_beside_it(string project)
    {
        var listed = SampleLayout.Project(project);
        var height = HeightOf(listed.Layer);

        Closure(project)
            .Where(reached => SampleLayout.Holds(reached.Key) && SampleLayout.Project(reached.Key).Module == listed.Module)
            .Where(reached => HeightOf(SampleLayout.Project(reached.Key).Layer) >= height)
            .Select(Describe)
            .Should().BeEmpty("{0} depends only on the layers of its module below it", project);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void A_module_reaches_another_only_through_its_contracts(string project)
    {
        var module = SampleLayout.Project(project).Module;

        Closure(project)
            .Where(reached => reached.Key.StartsWith("Examples.Tenancy.", StringComparison.Ordinal))
            // What the modules share is no module's: reaching it is reaching no other module.
            .Where(reached => reached.Key != SampleLayout.SharedDomain && reached.Key != SampleLayout.SharedInfrastructure && reached.Key != "Examples.Tenancy.Shared.Application")
            .Where(reached => !SampleLayout.Holds(reached.Key) || SampleLayout.Project(reached.Key).Module != module)
            .Where(reached => !SampleLayout.Holds(reached.Key) || SampleLayout.Project(reached.Key).Layer != Layer.Contracts)
            .Select(Describe)
            .Should().BeEmpty("another module's contracts are all {0} may know of it", module);
    }

    [Fact]
    public void The_shared_domain_references_no_module_and_knows_neither_storage_nor_HTTP()
    {
        var reached = ClosureOf(SampleLayout.SharedDomainFile());

        reached.Where(found => found.Key.StartsWith("Examples.", StringComparison.Ordinal)).Select(Describe)
            .Should().BeEmpty("what several modules share depends on none of them: a module references it, never the other way");
        reached.Where(found => SampleLayout.IsEntityFramework(found.Key) || IsAspNetCore(found.Key)).Select(Describe)
            .Should().BeEmpty("a shared domain type says nothing of how a module stores or serves it");

        // It claims no module, so the boundary analyzer lets every module name what it declares.
        typeof(DateRange).Assembly.GetName().Name.Should().Be(SampleLayout.SharedDomain);
        typeof(DateRange).Assembly.GetCustomAttribute<ModuleAttribute>().Should().BeNull("what belongs to no module declares none");
    }

    [Fact]
    public void Only_a_modules_domain_and_contracts_projects_reference_the_shared_domain()
    {
        // By the project files, whoever names it: an application, infrastructure or API project that needs a shared
        // type has it through its module's domain, and the host and the UI through nothing of their own.
        var referencing = SampleLayout.SampleProjectFiles()
            .Where(file => XDocument.Load(file).Descendants("ProjectReference")
                .Any(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')) == SampleLayout.SharedDomain))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        referencing.Should().NotBeEmpty("a shared project nobody references shares nothing")
            .And.OnlyContain(project => SampleLayout.Holds(project!) && (SampleLayout.Project(project!).Layer == Layer.Domain || SampleLayout.Project(project!).Layer == Layer.Contracts));
        referencing.Select(project => SampleLayout.Project(project!).Module).Distinct()
            .Should().HaveCountGreaterThan(1, "a type one module uses is that module's own, in its domain or its contracts");
    }

    [Fact]
    public void The_shared_infrastructure_references_no_module_and_only_infrastructure_projects_reference_it()
    {
        ClosureOf(SampleLayout.SharedInfrastructureFile()).Where(found => found.Key.StartsWith("Examples.", StringComparison.Ordinal)).Select(Describe)
            .Should().BeEmpty("what several modules' storage shares depends on none of them: a list hands it its query and its refusal");

        typeof(ListCursors).Assembly.GetName().Name.Should().Be(SampleLayout.SharedInfrastructure);
        typeof(ListCursors).Assembly.GetCustomAttribute<ModuleAttribute>().Should().BeNull("what belongs to no module declares none");

        // By the project files, whoever names it: it knows how a query is ordered and how a cursor is written,
        // which is storage, so no application, API, host or UI project has it.
        var referencing = SampleLayout.SampleProjectFiles()
            .Where(file => XDocument.Load(file).Descendants("ProjectReference")
                .Any(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')) == SampleLayout.SharedInfrastructure))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        referencing.Should().OnlyContain(project => SampleLayout.Holds(project!) && SampleLayout.Project(project!).Layer == Layer.Infrastructure);
        referencing.Select(project => SampleLayout.Project(project!).Module)
            .Should().BeEquivalentTo(SampleLayout.OnTheMediator.Keys, "every module pages a list, and each holds its list to its own cursors through this one check");
    }

    /// <summary>
    /// How a module is stored says nothing of how it is served. A project that knows storage, a module's
    /// infrastructure project and what the infrastructure projects share, names no HotChocolate in its project
    /// file, and its assembly references none: a list is paged by the paging library alone, and a field of a
    /// schema sends a query and is handed no context.
    /// </summary>
    /// <remarks>
    /// By the project file and the assembly, and not by the closure: every module's registration takes the host's
    /// decisions from the samples' shared hosting project, which carries what the host gives each schema and so
    /// knows HotChocolate's builder. That is the hosting's, and no infrastructure project uses it.
    /// </remarks>
    [Fact]
    public void No_project_that_knows_storage_references_HotChocolate()
    {
        var storage = SampleLayout.Projects.Where(project => project.Layer == Layer.Infrastructure)
            .Select(project => (File: SampleLayout.FileOf(project), project.Anchor.Assembly))
            .Append((File: SampleLayout.SharedInfrastructureFile(), typeof(ListCursors).Assembly))
            .ToList();

        storage.Should().HaveCount(4, "three modules, each with its migrations beside its context, and what their storage shares");
        foreach (var (file, assembly) in storage)
        {
            var project = Path.GetFileNameWithoutExtension(file);

            XDocument.Load(file).Descendants().Select(item => item.Attribute("Include")?.Value).OfType<string>()
                .Where(IsHotChocolate)
                .Should().BeEmpty("{0} knows how a module is stored, and nothing of GraphQL", project);
            assembly.GetReferencedAssemblies().Select(name => name.Name!).Where(IsHotChocolate)
                .Should().BeEmpty("{0} uses nothing of HotChocolate", project);
        }

        // The paging library is what they page with, and it is not HotChocolate: the rule sees the difference, and
        // it would see the API project, which serves the schema.
        typeof(ProjectsInfrastructure).Assembly.GetReferencedAssemblies().Select(name => name.Name).Should().Contain("GreenDonut.Data.EntityFramework");
        typeof(ProjectsModule).Assembly.GetReferencedAssemblies().Select(name => name.Name!).Where(IsHotChocolate).Should().NotBeEmpty();

        static bool IsHotChocolate(string name) => name.Contains("HotChocolate", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_closure_holds_what_the_packages_bring_in()
    {
        // No project file this one reaches names the Postgres driver: the Entity Framework provider the
        // infrastructure project names brings it in, and only the restore shows it. A package that brought Entity
        // Framework into a domain project would show the same way.
        Closure("Examples.Tenancy.Projects.Infrastructure").Should().ContainKey("Npgsql")
            .WhoseValue.Should().Be("Examples.Tenancy.Projects.Infrastructure's restore");

        // And the provider itself is named by the project's own file, as the one database its migrations are for.
        Closure("Examples.Tenancy.Projects.Infrastructure").Should().ContainKey("Npgsql.EntityFrameworkCore.PostgreSQL")
            .WhoseValue.Should().Be("Examples.Tenancy.Projects.Infrastructure");
    }

    // ------------------------------------------------------------------ the host

    [Fact]
    public void The_host_references_the_api_projects_and_the_catalogue_only()
    {
        var referenced = XDocument.Load(SampleLayout.HostProjectFile()).Descendants()
            .Where(item => item.Name.LocalName == "ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(item.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
            .Where(name => name.StartsWith("Examples.Tenancy.", StringComparison.Ordinal))
            .ToList();

        referenced.Should().BeEquivalentTo(
            [.. Named(Layer.Api), SampleLayout.Catalogue],
            "the host adds a module through the one entry its API project has, and references nothing else of it; the catalogue is the application's, and no module's");
    }

    [Fact]
    public void The_host_names_nothing_of_an_infrastructure_project_but_in_the_list_of_marked_contexts()
    {
        // The compiler records a reference only for an assembly the code names something of, so the host's references
        // say what it names. Project references flow on, so the host could name a context: its own code does not. What
        // does is the list the Supabase package's generator wrote into it for AddSupabaseMigrations(): every context
        // marked [SupabaseMigrations] with the design-time factory beside it, which the start-up check makes. So the
        // infrastructure projects the host names are the ones that hold a marked context, and only that list names
        // anything of them.
        var host = typeof(Program).Assembly;
        var named = host.GetReferencedAssemblies().Select(name => name.Name!).ToList();
        var uses = ScanOf(host).Uses.Where(use => IsOfAStorageProject(use.Type)).ToList();

        named.Should().Contain(Named(Layer.Api), "the host names each module's entry, or this proves nothing");
        named.Where(SampleLayout.IsStorageProject).Should().BeEquivalentTo(Named(Layer.Infrastructure), "each module marks its context");
        uses.Where(use => !IsWrittenBySupabase(use.By)).Select(use => use.ToString())
            .Should().BeEmpty("the host knows no module's storage: no context, no store, no registration but the entry's");
        uses.Select(use => use.Type).Distinct().Should().OnlyContain(
            type => type.IsSubclassOf(typeof(Microsoft.EntityFrameworkCore.DbContext)) || type.Name == type.GetInterfaces().Single().GetGenericArguments()[0].Name + "DesignTimeFactory",
            "the list names a marked context and the factory the build wrote beside it, and nothing else of a module's storage");
    }

    /// <summary>
    /// The host's own code is held to what a module's routes are held to: it sends a request, and takes nothing of
    /// a module that the container could hand it instead. No port, no handler, no access rule, no gate: a route of
    /// the host's own that took one would go round the check every request passes.
    /// </summary>
    /// <remarks>
    /// The use cases of the Tenancy package check their caller themselves, so they are not a way round; but the
    /// host calls them in one place only, its seeding, for what no command offers a client: ids fixed in advance
    /// and a grant that started in the past. Everything else it does to a tenant, it sends.
    /// </remarks>
    [Fact]
    public void The_host_sends_and_takes_nothing_else_of_a_module()
    {
        var uses = ScanOf(typeof(Program).Assembly).Uses.Where(use => !IsWrittenByTheMediator(use.By) && !IsWrittenBySupabase(use.By)).ToList();
        var seeding = uses.Where(use => TypeScan.Outermost(use.By) == typeof(DemoSeeder)).ToList();
        var elsewhere = uses.Where(use => TypeScan.Outermost(use.By) != typeof(DemoSeeder)).ToList();

        HostRegistrations.Services.Should().Contain(typeof(ProjectAccess), "the host registers a module's services, so one taken by its own code would be found")
            .And.Contain(typeof(IProjectGate), "its contracts' too")
            .And.Contain(typeof(TenancyUseCases<,,,,,,,,>.SeatCommands), "and the package's use cases");
        uses.Select(use => use.Type).Should().Contain(typeof(ISender), "the host does send, and the scan sees it");
        seeding.Select(use => use.Type).Should().Contain(typeof(TenancyUseCases<,,,,,,,,>.SeatCommands), "the seeding names the package's use cases, so the scan would see one named elsewhere");

        uses.Where(use => SampleLayout.IsPort(use.Type) || IsServiceOfAModule(use.Type))
            .Select(use => use.ToString())
            .Should().BeEmpty("the host adds the modules and sends their requests; what a request may do is decided on its way to its handler");
        elsewhere.Where(use => IsUseCaseOfThePackage(use.Type))
            .Select(use => use.ToString())
            .Should().BeEmpty("outside its seeding the host changes a tenant by sending a command, as a route does");
    }

    // ------------------------------------------------------------------ an API project

    /// <summary>
    /// Everything in an API project but its entry: the routes, the lambdas the compiler made of them, what they
    /// capture and their state machines. None names a type of an infrastructure project, of Entity Framework, or a
    /// port, by a parameter or by asking the container. The entry does name the infrastructure project, which is
    /// what shows the scan would see it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ApiProjects))]
    public void Only_the_entry_of_an_API_project_names_infrastructure_storage_or_a_port(string project)
    {
        var listed = SampleLayout.Project(project);
        var scan = ScanOf(listed.Anchor.Assembly);

        scan.Uses.Where(use => use.By == listed.Anchor).Select(use => use.Type)
            .Should().Contain(SampleLayout.InfrastructureOf(listed.Module), "the entry calls the infrastructure project's registration, and the scan sees it");

        scan.Uses.Where(use => use.By != listed.Anchor)
            .Where(use => IsOfAStorageProject(use.Type) || IsOfEntityFramework(use.Type) || SampleLayout.IsPort(use.Type))
            .Select(use => use.ToString())
            .Should().BeEmpty("in {0} only the entry composes; a route sends a request and knows nothing of how it is handled or stored", project);
    }

    [Theory]
    [MemberData(nameof(ApiProjects))]
    public void An_API_assembly_references_no_entity_framework_assembly(string project)
    {
        // The API project references the infrastructure project, so Entity Framework is within reach of its code.
        // Its assembly names nothing of it all the same: not even through a registration the generators would
        // otherwise write into it.
        SampleLayout.Project(project).Anchor.Assembly.GetReferencedAssemblies().Select(name => name.Name!)
            .Where(SampleLayout.IsEntityFramework)
            .Should().BeEmpty("{0} composes the module and serves it; it stores nothing", project);
    }

    [Theory]
    [MemberData(nameof(ApiProjects))]
    public void The_entry_of_an_API_project_only_composes(string project)
    {
        var listed = SampleLayout.Project(project);
        var entry = listed.Anchor;
        const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // The two registrations the GraphQL generators write into the project are public as well; they are the
        // generators', and add types and scalars to a schema, which is no way into the module.
        listed.Anchor.Assembly.GetExportedTypes().Where(type => !SampleLayout.IsWrittenByAGraphQLGenerator(type))
            .Should().Equal([entry], "the entry is the one public type {0} declares: the only way in", project);
        (entry.IsAbstract && entry.IsSealed).Should().BeTrue("the entry is a static class");
        entry.GetFields(Declared).Should().BeEmpty("the entry holds nothing");
        entry.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Should().BeEmpty("the entry has no lambda and no type of its own: no route is written in it");
        entry.GetMethods(Declared).Should().OnlyContain(method => method.IsPublic && method.IsStatic, "the entry is its public methods");
        entry.GetMethods(Declared).Select(method => method.ReturnType)
            .Should().Contain(typeof(IServiceCollection), "the entry registers the module")
            .And.Contain(typeof(IEndpointRouteBuilder), "and maps its routes");

        var named = ScanOf(entry.Assembly).Uses.Where(use => use.By == entry).ToList();
        named.Where(use => IsOfAStorageProject(use.Type)).Select(use => use.Type).Distinct()
            .Should().Equal([SampleLayout.InfrastructureOf(listed.Module)], "of the infrastructure project the entry names its registration and nothing else");
        named.Where(use => IsOfEntityFramework(use.Type) || SampleLayout.IsPort(use.Type)).Select(use => use.ToString())
            .Should().BeEmpty("the entry composes; it neither stores nor reads");
    }

    /// <summary>
    /// A route and a resolver send a request and take nothing else of the application: no handler, no use case, no
    /// port, no context, no service of the Tenancy package. What is forbidden is not a list kept here: it is every
    /// type the host itself registers of the sample, the toolkit, the Tenancy package and the mediator, the sender
    /// left out.
    /// </summary>
    /// <remarks>
    /// A data loader is among what the host registers, because a schema registers its loaders, and a lookup takes
    /// one by design: it is how the ids one answer names are asked in one question. So the loaders an API project
    /// has are not held against the resolvers that take them. HotChocolate's generator writes each from a method
    /// marked <c>[DataLoader]</c>, and calls that method in a scope of services of its own; the method is held to
    /// the rule itself, by what it takes: the keys, the sender and a token, and between the keys and the sender
    /// the paging arguments when it loads a page of each key, so no port and no context either. A method whose
    /// query answers one key takes that one key, and the generator writes a loader that remembers each answer.
    /// <para>
    /// Who answers a field's permission key (<c>IFieldKeys&lt;T&gt;</c>) is registered as well, since the handler
    /// behind <c>[Authorize]</c> asks the container for it. It is held to the same: it takes nothing when it is
    /// made, and asks through a data loader of the resolver's context.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ModulesOnTheMediator))]
    public void No_endpoint_or_resolver_takes_a_handler_or_a_port(string module)
    {
        var entry = SampleLayout.EntryOf(module);
        var loaders = TypeScan.TypesOf(entry.Assembly).Where(type => typeof(IDataLoader).IsAssignableFrom(type)).ToHashSet();
        var fieldKeys = TypeScan.TypesOf(entry.Assembly).Where(type => type.GetInterfaces().Any(IsFieldKeys)).ToHashSet();
        var named = ScanOf(entry.Assembly).Uses.Where(use => use.By != entry && !SampleLayout.IsWrittenByAGraphQLGenerator(use.By)).ToList();

        HostRegistrations.Services.Should().Contain(HostRegistrations.Requests.Select(request => request.Handler), "the host registers the handlers, so a route that took one would be found");
        named.Select(use => use.Type).Should().Contain(typeof(ISender), "the scan sees the routes' and the resolvers' parameters, or it proves nothing");
        named.Where(use => TypeScan.Outermost(use.By).Namespace?.EndsWith("." + SampleLayout.GraphQL, StringComparison.Ordinal) == true).Select(use => use.Type)
            .Should().Contain(typeof(ISender), "and the resolvers are among what it reads");
        named.Where(use => (HostRegistrations.Services.Contains(use.Type) && !loaders.Contains(use.Type) && !fieldKeys.Contains(use.Type) && use.Type != typeof(IFieldKeys<>)) || SampleLayout.IsPort(use.Type))
            .Select(use => use.ToString())
            .Should().BeEmpty("{0}'s routes and resolvers only send: what a request may do is decided on its way to its handler, and one that took a service would go round that", module);

        foreach (var loader in loaders)
        {
            HostRegistrations.Services.Should().Contain(loader, "the schema registers {0}, which is why a resolver that takes it needs this exception", loader.Name);
            SampleLayout.DataLoaderMethodOf(loader).Should().NotBeNull("{0} is written by HotChocolate's generator from a method marked [DataLoader]: nobody writes a loader class", loader.Name);
        }

        foreach (var method in SampleLayout.DataLoaderMethodsOf(entry.Assembly))
        {
            var taken = method.GetParameters().Select(parameter => parameter.ParameterType).ToList();

            // A loader of pages takes what the field was asked with, which the resolver hands it with With(paging).
            var paged = taken.Count > 1 && taken[1] == typeof(PagingArguments);

            taken.Should().HaveCount(paged ? 4 : 3, "{0} takes the keys, the sender and a token, and the paging arguments when it loads pages", method.Name);

            // The keys of one batch, answered together, or the one key of a query that answers one.
            if (taken[0].IsGenericType)
            {
                taken[0].GetGenericTypeDefinition().Should().Be(typeof(IReadOnlyList<>), "{0} takes the keys of one batch first", method.Name);
                method.ReturnType.GenericTypeArguments.Should().ContainSingle().Which.IsGenericType.Should().BeTrue("{0} answers every key of its batch at once", method.Name);
            }
            else
            {
                HostRegistrations.Services.Should().NotContain(taken[0], "{0} takes a key first, not a service", method.Name);
            }

            taken.Skip(paged ? 2 : 1).Should().Equal([typeof(ISender), typeof(CancellationToken)], "{0} sends a query as a resolver does; it reads nothing itself", method.Name);
        }

        foreach (var answers in fieldKeys)
        {
            HostRegistrations.Services.Should().Contain(answers, "the module registers {0}, which is why it needs this exception", answers.Name);
            answers.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SelectMany(constructor => constructor.GetParameters())
                .Should().BeEmpty("{0} takes nothing when it is made: it asks through a data loader of the resolver's context", answers.Name);
        }

        // Tenancy answers the names the other modules' ids stand for, a project's fields that take a read of
        // their own load through one, and Inspections answers the inspections of the projects one answer names:
        // every module has loaders, so every module is held to the above.
        loaders.Should().NotBeEmpty("{0} has data loaders", module);
    }

    // ------------------------------------------------------------------ handlers and ports

    /// <summary>
    /// A handler is reached by sending its request, and in no other way: nothing in an application, an
    /// infrastructure or an API project, nor in the host, names a handler class or a handler interface. A handler
    /// called directly would run without the steps its request passes on the way, the access check first of all.
    /// The mediator the generator writes into the host is the one thing that names them.
    /// </summary>
    [Fact]
    public void Only_the_mediator_reaches_a_handler()
    {
        var handlers = HostRegistrations.Requests.Select(request => request.Handler).ToHashSet();
        var assemblies = SampleLayout.Projects
            .Where(project => project.Layer is Layer.Application or Layer.Infrastructure or Layer.Api)
            .Select(project => project.Anchor.Assembly)
            .Append(typeof(Program).Assembly)
            .ToList();
        var uses = assemblies.SelectMany(assembly => ScanOf(assembly).Uses).Where(use => handlers.Contains(use.Type) || IsHandlerInterface(use.Type)).ToList();

        handlers.Should().NotBeEmpty();
        uses.Where(use => IsWrittenByTheMediator(use.By)).Select(use => use.Type).Should().Contain(handlers, "the generated mediator names every handler, and the scan sees it");
        uses.Where(use => !IsWrittenByTheMediator(use.By) && !handlers.Contains(TypeScan.Outermost(use.By)))
            .Select(use => use.ToString())
            .Should().BeEmpty("a handler is named by itself and by the mediator, and by nothing else");
    }

    /// <summary>
    /// A port is the application's to use and the infrastructure project's to implement. In an application project
    /// only a handler and the types of the <c>Access</c> feature name one (and a port's own declaration, which may
    /// name what it opens); in an infrastructure project only the adapters that implement it and the registration.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModulesOnTheMediator))]
    public void A_port_is_named_only_where_it_is_used_or_implemented(string module)
    {
        var handlers = HostRegistrations.Requests.Select(request => request.Handler).ToHashSet();
        var project = SampleLayout.Project(module, Layer.Application);
        var application = ScanOf(project.Anchor.Assembly).Uses.Where(use => SampleLayout.IsPort(use.Type)).ToList();
        var infrastructure = ScanOf(SampleLayout.Project(module, Layer.Infrastructure).Anchor.Assembly).Uses.Where(use => SampleLayout.IsPort(use.Type)).ToList();

        application.Should().NotBeEmpty("{0}'s handlers read and save through ports", module);
        application
            .Where(use => !handlers.Contains(TypeScan.Outermost(use.By)))
            .Where(use => !SampleLayout.IsPort(TypeScan.Outermost(use.By)))
            .Where(use => TypeScan.Outermost(use.By).Namespace != project.Name + ".Access")
            .Select(use => use.ToString())
            .Should().BeEmpty("in {0}'s application project a port is named by the handlers and the access types that use it", module);

        infrastructure.Should().NotBeEmpty("{0}'s infrastructure project implements its ports", module);
        infrastructure
            .Where(use => TypeScan.Outermost(use.By) != SampleLayout.InfrastructureOf(module))
            .Where(use => !TypeScan.Outermost(use.By).GetInterfaces().Any(SampleLayout.IsPort))
            .Select(use => use.ToString())
            .Should().BeEmpty("in {0}'s infrastructure project a port is named by its adapter and by the registration", module);
    }

    [Theory]
    [MemberData(nameof(ContractsAndDomainProjects))]
    public void Contracts_and_domain_know_no_mediator(string project)
    {
        Closure(project).Where(reached => SampleLayout.IsMediator(reached.Key))
            .Select(Describe)
            .Should().BeEmpty("{0} holds what the module is, not how its use cases are sent: commands, queries and handlers are the application project's", project);

        SampleLayout.Project(project).Anchor.Assembly.GetReferencedAssemblies().Select(name => name.Name!)
            .Where(SampleLayout.IsMediator)
            .Should().BeEmpty();
    }

    [Fact]
    public void Only_the_host_runs_the_mediator_generator()
    {
        // The generator writes the mediator and the registration of every handler into the project it runs in. Run
        // in a module, it would write a second mediator there, which knew that module's handlers only.
        var running = Directory.GetFiles(Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy"), "*.csproj", SearchOption.AllDirectories)
            .Where(file => XDocument.Load(file).Descendants().Any(item => item.Name.LocalName == "PackageReference" && item.Attribute("Include")?.Value == "Mediator.SourceGenerator"))
            .Select(Path.GetFullPath)
            .ToList();

        running.Should().Equal([SampleLayout.HostProjectFile()], "the composition root is the one project that references the generator");
        foreach (var project in SampleLayout.Projects)
        {
            Closure(project.Name).Keys.Should().NotContain("Mediator.SourceGenerator", "{0} gets the generator from no reference either", project.Name);
        }
    }

    /// <summary>
    /// A name is Tenancy's to answer. The package's directory, its lookup of a person's seats and its use cases all
    /// carry names, and all are reached through the Tenants module alone: Projects and Inspections name none of
    /// them, so the only thing of Tenancy's they can read is the read model, which has no name.
    /// </summary>
    [Fact]
    public void Only_the_tenancy_module_names_tenancys_directory()
    {
        static bool IsOfTenancysDirectory(Type type)
        {
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            return definition == typeof(ISeatDirectory<,>)
                || definition == typeof(TenantSelection<,>)
                || definition == typeof(SeatOfCaller<,>)
                || definition == typeof(TenancyUseCases<,,,,,,,,>)
                || definition.DeclaringType == typeof(TenancyUseCases<,,,,,,,,>);
        }

        var naming = SampleLayout.Projects
            .Select(project => (project.Module, Uses: ScanOf(project.Anchor.Assembly).Uses.Where(use => IsOfTenancysDirectory(use.Type)).ToList()))
            .ToList();

        naming.Where(found => found.Module != "Tenants").SelectMany(found => found.Uses).Select(use => use.ToString())
            .Should().BeEmpty("Projects and Inspections answer ids, and ask Tenancy for no name");

        // The scan would see it: the Tenants module names all three.
        var tenancy = naming.Where(found => found.Module == "Tenants").SelectMany(found => found.Uses).Select(use => use.Type).ToList();
        tenancy.Should().Contain(typeof(TenantSelection<,>))
            .And.Contain(typeof(SeatOfCaller<,>))
            .And.Contain(typeof(TenancyUseCases<,,,,,,,,>.TenancyDirectory));
    }

    [Fact]
    public void The_tenancy_packages_know_no_mediator()
    {
        // The package's use cases are plain services, which a host may call as they are or wrap in requests, as the
        // sample's Tenants module does. Nothing of that choice is the package's: the cases a request declares and
        // the check that decides them are the package's too, and the behavior that asks the check in front of a
        // handler is written into the application, by the toolkit's generator, where the application uses the mediator.
        var packages = PackagesNamed("DDDToolkit.Supporting.Tenancy*");

        packages.Select(Path.GetFileNameWithoutExtension).Should().Contain(
        [
            "DDDToolkit.Supporting.Tenancy",
            "DDDToolkit.Supporting.Tenancy.EntityFramework",
            "DDDToolkit.Supporting.Tenancy.Postgres",
        ]);
        foreach (var package in packages)
        {
            ClosureOf(package).Where(reached => SampleLayout.IsMediator(reached.Key))
                .Select(Describe)
                .Should().BeEmpty("{0} stays free of the mediator", Path.GetFileNameWithoutExtension(package));
        }
    }

    [Fact]
    public void The_membership_packages_know_no_mediator()
    {
        // The same holds for the second supporting domain: its requirements and their check are plain types over
        // the toolkit's, and whatever stands in front of an application's handlers asks them.
        var packages = PackagesNamed("DDDToolkit.Supporting.Membership*");

        packages.Select(Path.GetFileNameWithoutExtension).Should().Contain(
        [
            "DDDToolkit.Supporting.Membership",
            "DDDToolkit.Supporting.Membership.EntityFramework",
            "DDDToolkit.Supporting.Membership.Postgres",
        ]);
        foreach (var package in packages)
        {
            ClosureOf(package).Where(reached => SampleLayout.IsMediator(reached.Key))
                .Select(Describe)
                .Should().BeEmpty("{0} stays free of the mediator", Path.GetFileNameWithoutExtension(package));
        }
    }

    /// <summary>The project files of the packages under <c>Source</c> whose folder matches <paramref name="pattern"/>.</summary>
    private static List<string> PackagesNamed(string pattern)
        => [.. Directory.GetDirectories(Path.Combine(SampleLayout.RepositoryRoot(), "Source"), pattern)
            .Select(directory => Path.Combine(directory, Path.GetFileName(directory) + ".csproj"))];

    // ------------------------------------------------------------------ the scan itself

    [Fact]
    public void A_port_is_recognised_wherever_it_appears_in_a_type()
    {
        TypeScan.PartsOf(typeof(ITenancyReads)).Should().Contain(part => SampleLayout.IsPort(part));
        TypeScan.PartsOf(typeof(IInspectionStore[])).Should().Contain(part => SampleLayout.IsPort(part));
        TypeScan.PartsOf(typeof(IReadOnlyList<Lazy<IProjectStore>>)).Should().Contain(part => SampleLayout.IsPort(part), "a port inside a type argument of a type argument is still a port");
        TypeScan.PartsOf(typeof(Func<TenantsTenancy.IStore, Task>)).Should().Contain(part => SampleLayout.IsPort(part), "the Tenancy package's store is the port of Tenancy's use cases");
        TypeScan.PartsOf(typeof(Func<IProjectReading, Task>)).Should().Contain(part => SampleLayout.IsPort(part), "what a read port opens is a port too: it reads");
        TypeScan.PartsOf(typeof(Task<IInspectionReads>)).Should().Contain(part => SampleLayout.IsPort(part), "a read port that hands out no reading is a port all the same");
        TypeScan.PartsOf(typeof(Func<TenantSeats, Task>)).Should().NotContain(part => SampleLayout.IsPort(part));
        TypeScan.PartsOf(typeof(IReadOnlyList<ProjectCrewData>)).Should().NotContain(part => SampleLayout.IsPort(part), "what a port answers with is data, though it is declared next to the port");
    }

    [Fact]
    public void What_a_route_asks_the_container_for_is_found()
    {
        var scan = TypeScan.Of(TypeScan.WithNested(typeof(AskingTheContainer)));

        scan.NotResolved.Should().BeEmpty();
        scan.Uses.Where(use => use.Seen is Seen.Signature or Seen.Field).Where(use => SampleLayout.IsPort(use.Type) || IsOfAStorageProject(use.Type))
            .Should().BeEmpty("no parameter or field of the routes shows what they ask for");
        scan.Uses.Where(use => use.Seen == Seen.Code).Select(use => use.Type).Should()
            .Contain(typeof(ITenancyReads), "the code that asks the container for a port names it")
            .And.Contain(typeof(TenantsContext), "and so does the code that asks for a context");
        IsOfAStorageProject(typeof(TenantsContext)).Should().BeTrue("a context is a type of an infrastructure project");
        IsOfEntityFramework(typeof(TenantsContext).BaseType!).Should().BeTrue("and it derives from Entity Framework's");
    }

    [Fact]
    public void What_a_type_derives_from_is_constrained_to_carries_and_returns_is_read()
    {
        var uses = TypeScan.Of([typeof(NamedInDeclarations<>)]).Uses;

        uses.Should().Contain(use => use.Type == typeof(IProjectStore) && use.Seen == Seen.Declaration, "a type argument of the base type");
        uses.Should().Contain(use => use.Type == typeof(ITenancyReading) && use.Seen == Seen.Declaration, "an interface");
        uses.Should().Contain(use => use.Type == typeof(ITenancyReads) && use.Seen == Seen.Declaration, "a generic constraint");
        uses.Should().Contain(use => use.Type == typeof(TenantsContext) && use.Seen == Seen.Declaration, "a typeof in an attribute");
        uses.Should().Contain(use => use.Type == typeof(TenantsTenancy.IStore) && use.Seen == Seen.Signature, "what a method returns");
    }

    [Fact]
    public void What_a_catch_names_is_read()
    {
        var scan = TypeScan.Of(TypeScan.WithNested(typeof(CatchingWhatItNames)));

        scan.NotResolved.Should().BeEmpty();
        scan.Uses.Should().Contain(
            use => use.Type == typeof(Microsoft.EntityFrameworkCore.DbUpdateException) && use.Seen == Seen.Code,
            "a catch without a variable names its type in no instruction and no local: only the method's table of handlers has it");
        IsOfEntityFramework(typeof(Microsoft.EntityFrameworkCore.DbUpdateException)).Should().BeTrue("so a route that caught Entity Framework's failures would be found to name Entity Framework");
    }

    /// <summary>Code that names a type of Entity Framework's in a catch and nowhere else.</summary>
    private static class CatchingWhatItNames
    {
        public static bool Went(Action act)
        {
            try
            {
                act();
                return true;
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return false;
            }
        }
    }

    /// <summary>Routes that ask the container for a port and for a context, in lambdas that take only the container.</summary>
    private static class AskingTheContainer
    {
        public static Func<IServiceProvider, bool> ForAPort { get; } = services => services.GetRequiredService<ITenancyReads>() is not null;

        public static Func<IServiceProvider, bool> ForAContext { get; } = services => services.GetRequiredService<TenantsContext>() is not null;
    }

    /// <summary>A type that names a port or a context in every place a declaration can, and in no method body.</summary>
    [Names(typeof(TenantsContext))]
    private abstract class NamedInDeclarations<T> : List<IProjectStore>, ITenancyReading
        where T : ITenancyReads
    {
        public abstract DDDToolkit.Supporting.Tenancy.Access.ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> Rows { get; }

        public abstract DDDToolkit.Supporting.Tenancy.Access.IQueryExecutor Queries { get; }

        public abstract ValueTask DisposeAsync();

        public abstract TenantsTenancy.IStore Store();
    }

    [AttributeUsage(AttributeTargets.Class)]
    private sealed class NamesAttribute(Type named) : Attribute
    {
        public Type Named { get; } = named;
    }

    // ------------------------------------------------------------------ what the tests read

    private static IEnumerable<string> Named(params Layer[] layers)
        => SampleLayout.Projects.Where(project => layers.Contains(project.Layer)).Select(project => project.Name);

    /// <summary>
    /// What the types of <paramref name="assembly"/> name, read once. A token the scan could not resolve fails the
    /// test that asked: a rule about what nothing names proves nothing over code that was not read.
    /// </summary>
    private static TypeScan ScanOf(Assembly assembly)
    {
        var scan = Scans.GetOrAdd(assembly, TypeScan.Of);
        scan.NotResolved.Should().BeEmpty("every token in the code of {0} is read", assembly.GetName().Name);
        return scan;
    }

    private static bool IsOfAStorageProject(Type type) => SampleLayout.IsStorageProject(type.Assembly.GetName().Name!);

    /// <summary>
    /// Whether the host registers <paramref name="type"/> and a project of a module declares it: a handler, an
    /// access rule, a gate, an adapter. A module's entry, its requests and its keys are not registered, so the
    /// host may name those.
    /// </summary>
    private static bool IsServiceOfAModule(Type type)
        => HostRegistrations.Services.Contains(type) && SampleLayout.Holds(type.Assembly.GetName().Name!);

    /// <summary>Whether <paramref name="type"/> is a use-case class of the Tenancy package that the host registers.</summary>
    private static bool IsUseCaseOfThePackage(Type type)
        => type.DeclaringType == typeof(TenancyUseCases<,,,,,,,,>) && HostRegistrations.Services.Contains(type);

    private static bool IsOfEntityFramework(Type type) => SampleLayout.IsEntityFramework(type.Assembly.GetName().Name!);

    private static bool IsAspNetCore(string name) => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);

    /// <summary>A handler interface of the mediator, constructed or not: <c>ICommandHandler&lt;,&gt;</c> and the rest.</summary>
    private static bool IsHandlerInterface(Type type)
        => type.IsInterface
            && type.Assembly == typeof(IMessage).Assembly
            && type.Name.Contains("Handler`", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="type"/> is the toolkit's interface for answering a field's permission key, for some parent type.</summary>
    private static bool IsFieldKeys(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IFieldKeys<>);

    /// <summary>Whether the mediator's generator wrote <paramref name="type"/>, or the type it is nested in.</summary>
    private static bool IsWrittenByTheMediator(Type type)
        => TypeScan.Outermost(type).GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "Mediator.SourceGenerator";

    /// <summary>
    /// Whether the Supabase package's generator wrote <paramref name="type"/>, or the type it is nested in: in the host,
    /// the list of the marked contexts and <c>AddSupabaseMigrations()</c>.
    /// </summary>
    private static bool IsWrittenBySupabase(Type type)
        => TypeScan.Outermost(type).GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "DDDToolkit.EntityFramework.Supabase.Analyzers";

    /// <summary>
    /// How far up a layer is. A project may reach a project of its own module only further down. The API project
    /// stands on top: it composes the module, so it reaches the infrastructure project and, through it, the rest.
    /// </summary>
    private static int HeightOf(Layer layer) => layer switch
    {
        Layer.Contracts => 0,
        Layer.Domain => 1,
        Layer.Application => 2,
        Layer.Infrastructure => 3,
        Layer.Api => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
    };

    private static string Describe(KeyValuePair<string, string> reached) => $"{reached.Key} (through {reached.Value})";

    /// <summary>Everything a project of a module reaches. See <see cref="ClosureOf"/>.</summary>
    private static IReadOnlyDictionary<string, string> Closure(string project)
    {
        var file = SampleLayout.FileOf(SampleLayout.Project(project));
        File.Exists(file).Should().BeTrue("{0} is at the path its module's folder gives it", project);
        return ClosureOf(file);
    }

    /// <summary>
    /// Everything the project at <paramref name="projectFile"/> reaches: through its project file and those of
    /// the projects it references, to the end, each project, package, framework and analyzer by name, with the
    /// project whose file names it; and every package and framework its restore resolved, with the packages they
    /// bring in.
    /// </summary>
    /// <remarks>
    /// Every reference in a project file counts, whatever its condition or assets: a reference that only applies
    /// to one build still lets that build reach it. An analyzer counts as reached too, and so does what it
    /// references. A web SDK counts as a reference to ASP.NET Core, as it is one.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> ClosureOf(string projectFile)
    {
        var project = Path.GetFileNameWithoutExtension(projectFile);
        var reached = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>([Path.GetFullPath(projectFile)]);

        while (pending.TryDequeue(out var file))
        {
            if (!visited.Add(file))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            var document = XDocument.Load(file);

            if (document.Root?.Attribute("Sdk")?.Value is "Microsoft.NET.Sdk.Web")
            {
                reached.TryAdd("Microsoft.AspNetCore.App", name);
            }

            foreach (var item in document.Descendants())
            {
                if (item.Attribute("Include")?.Value is not { } include)
                {
                    continue;
                }

                switch (item.Name.LocalName)
                {
                    case "ProjectReference":
                        var referenced = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, include.Replace('\\', Path.DirectorySeparatorChar)));
                        reached.TryAdd(Path.GetFileNameWithoutExtension(referenced), name);
                        pending.Enqueue(referenced);
                        break;
                    case "PackageReference" or "FrameworkReference":
                        reached.TryAdd(include, name);
                        break;
                    case "Analyzer":
                        reached.TryAdd(Path.GetFileNameWithoutExtension(include), name);
                        break;
                }
            }
        }

        foreach (var restored in Restored(projectFile))
        {
            reached.TryAdd(restored, project + "'s restore");
        }

        reached.Remove(project);
        return reached;
    }

    /// <summary>
    /// Every project, package and framework the restore of <paramref name="projectFile"/> resolved, with what they
    /// bring in: the targets of its <c>obj/project.assets.json</c>, a project by its file's name (its package id
    /// can carry a prefix), a package by its id, and each one's framework references.
    /// </summary>
    private static IEnumerable<string> Restored(string projectFile)
    {
        var assets = Path.Combine(Path.GetDirectoryName(projectFile)!, "obj", "project.assets.json");
        File.Exists(assets).Should().BeTrue("the tests run after the solution is restored, which writes {0}", assets);

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var root = document.RootElement;
        var libraries = root.GetProperty("libraries");

        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            foreach (var entry in target.Value.EnumerateObject())
            {
                var id = entry.Name[..entry.Name.IndexOf('/', StringComparison.Ordinal)];
                yield return entry.Value.TryGetProperty("type", out var type) && type.GetString() == "project"
                    && libraries.TryGetProperty(entry.Name, out var library) && library.TryGetProperty("path", out var path)
                        ? Path.GetFileNameWithoutExtension(path.GetString()!)
                        : id;

                if (entry.Value.TryGetProperty("frameworkReferences", out var frameworks))
                {
                    foreach (var framework in frameworks.EnumerateArray())
                    {
                        yield return framework.GetString()!;
                    }
                }
            }
        }

        foreach (var framework in root.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (framework.Value.TryGetProperty("frameworkReferences", out var references))
            {
                foreach (var reference in references.EnumerateObject())
                {
                    yield return reference.Name;
                }
            }
        }
    }
}
