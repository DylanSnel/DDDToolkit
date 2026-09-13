namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// Tests of the test harness itself. If these fail every other test in this project is meaningless,
/// so they check the things the rest silently relies on: that the framework references really are
/// .NET 10, that the SDK's preprocessor symbols are defined, and that the emitted assembly runs.
/// </summary>
public class HarnessTests
{
    [Fact]
    public void Snippets_compile_against_net10()
    {
        var result = GeneratorTestHost.Create(
            """
            public static class Probe
            {
                // ReadOnlySet<T> is .NET 9+; Guid.CreateVersion7 is .NET 9+.
                public static object Make() => new System.Collections.ObjectModel.ReadOnlySet<int>(new System.Collections.Generic.HashSet<int>());

                public static System.Guid Seven() => System.Guid.CreateVersion7();
            }
            """).RunCore();

        result.ShouldCompile();
        result.GeneratedSources.Should().BeEmpty("no DDDToolkit attribute is used");
    }

    [Fact]
    public void The_sdk_preprocessor_symbols_are_defined()
    {
        var conditions = string.Join("\n", GeneratorTestHost.PreprocessorSymbols.Select(symbol =>
            $"""
             #if !{symbol}
             #error {symbol} is not defined
             #endif
             """));

        GeneratorTestHost.Create(conditions + "\npublic static class Probe { }").RunCore().ShouldCompile();
    }

    [Fact]
    public void Emitted_assemblies_can_be_loaded_and_invoked()
    {
        var emitted = GeneratorTestHost.Create(
            """
            namespace Sample;

            public static class Probe
            {
                public static string Greet(string name) => "hello " + name;
            }
            """).RunCore().Emit();

        emitted.CallStatic("Sample.Probe", "Greet", "world").Should().Be("hello world");
    }

    [Fact]
    public void The_module_name_reaches_the_generators_through_analyzer_config_options()
    {
        // DDD_Module drives the name of the generated EF registration method.
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Sample;

            [EntityId<System.Guid>]
            public readonly partial record struct ThingId;
            """)
            .WithEntityFramework()
            .WithModule("Widgets")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "AddWidgetsConverters");
    }

    [Fact]
    public void Without_a_module_name_the_assembly_name_is_used()
    {
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Sample;

            [EntityId<System.Guid>]
            public readonly partial record struct ThingId;
            """)
            .WithAssemblyName("Contoso.Billing")
            .WithEntityFramework()
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("ConverterExtensions", "namespace Contoso.Billing.Converters;");
        result.ShouldContain("ConverterExtensions", "AddContosoBillingConverters");
    }
}
