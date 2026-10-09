using Examples.Tenancy.Tests.Projects.Access;
using Examples.Tenancy.Tests.Supabase;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// A test class that needs a container says so on the class itself, with the two traits the builds without Docker
/// filter on: <c>Category=Samples</c>, and the sample whose run it belongs to. Which classes need one is read from
/// what they take: a fixture that starts Supabase's images, or one that works on the stack the Supabase CLI starts.
/// </summary>
/// <remarks>
/// Nothing but convention keeps that true, and what goes wrong without it is silent or slow. A class without the
/// traits is asked for Supabase's images by the main build and by the build at the dependency floors, which have
/// no time for them and may have no Docker; a class with the wrong sample is left out of the run that would have
/// started what it needs.
/// <para>
/// And no test makes a host on a database itself. Such a host is made where its database is known, by the
/// fixtures under <c>Infrastructure</c>, so a test that needs one takes a fixture the first rule sees. A test
/// that made its own would start it in a build that has no database for it, on whatever connection string it
/// thought of. The host without a database (<see cref="SampleFactory.WithoutDatabase"/>) is anyone's to make:
/// it needs nothing.
/// </para>
/// </remarks>
public sealed class ContainerTraitTests
{
    /// <summary>The fixtures that need a container, each with the sample its classes run under.</summary>
    private static readonly IReadOnlyDictionary<Type, string> Fixtures = new Dictionary<Type, string>
    {
        [typeof(SampleHosts)] = "Tenancy.Supabase",
        [typeof(SampleSupabaseStack)] = "Tenancy.Supabase",
        [typeof(SupabaseCliStack)] = "Tenancy.SupabaseCli",
    };

    /// <summary>Every class of this project that declares a test.</summary>
    private static readonly Type[] TestClasses =
    [
        .. typeof(ContainerTraitTests).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.GetMethods().Any(method => method.IsDefined(typeof(FactAttribute), inherit: true)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal),
    ];

    [Fact]
    public void A_class_that_takes_a_fixture_on_containers_carries_the_samples_traits()
    {
        var onContainers = TestClasses
            .Select(type => (Type: type, Samples: SamplesOf(type)))
            .Where(found => found.Samples.Count > 0)
            .ToList();

        onContainers.Select(found => found.Type).Should().Contain(
            [typeof(AreaManagerScenarios), typeof(SampleOnPostgresTests), typeof(SampleOnTheCliStackTests)],
            "the scan finds the classes of each fixture");

        foreach (var (type, samples) in onContainers)
        {
            var sample = samples.Should().ContainSingle("{0} takes the fixtures of one sample", type.Name).Subject;

            TraitsOf(type).Should().BeEquivalentTo(
                [("Category", "Samples"), ("Sample", sample)],
                "{0} takes a fixture that needs Docker, so only the run that asks for {1} runs it", type.Name, sample);
        }
    }

    [Fact]
    public void Only_the_fixtures_make_a_host_on_a_database()
    {
        // What making such a host looks like in source: the constructor, which is private, and the factory method
        // that takes the settings a connection string is among. Put together from the names, so this file holds
        // neither.
        string[] makesAHost = [$"new {nameof(SampleFactory)}(", $"{nameof(SampleFactory)}.{nameof(SampleFactory.In)}("];

        var directory = Path.Combine(SampleLayout.RepositoryRoot(), "Tests", typeof(ContainerTraitTests).Assembly.GetName().Name!);
        var fixtures = typeof(SampleFactory).Namespace!.Split('.')[^1] + "/";
        var making = SampleLayout.SourceFilesIn(directory)
            .Where(file => makesAHost.Any(File.ReadAllText(Path.Combine(directory, file)).Contains))
            .ToList();

        // The scan finds what it looks for: the two fixtures that make a host are among the files it names.
        making.Should().Contain([fixtures + nameof(SampleOnPostgres) + ".cs", fixtures + nameof(SupabaseCliStack) + ".cs"], "the fixtures are where a host is made");

        making.Where(file => !file.StartsWith(fixtures, StringComparison.Ordinal))
            .Should().BeEmpty("a test asks a fixture for its host, which knows the database it runs on");
    }

    /// <summary>The samples of the fixtures a constructor of <paramref name="type"/> takes; none for a class that takes no such fixture.</summary>
    private static List<string> SamplesOf(Type type)
        => [.. type.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => Fixtures.ContainsKey(parameter.ParameterType))
            .Select(parameter => Fixtures[parameter.ParameterType])
            .Distinct(StringComparer.Ordinal)];

    private static IEnumerable<(string Name, string Value)> TraitsOf(Type type)
        => type.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
            .Select(attribute => ((string)attribute.ConstructorArguments[0].Value!, (string)attribute.ConstructorArguments[1].Value!));
}
