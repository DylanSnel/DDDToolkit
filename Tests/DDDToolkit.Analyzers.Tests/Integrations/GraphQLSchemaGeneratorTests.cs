namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// A class marked <c>[GraphQLSchema(name, operation)]</c> belongs to one GraphQL schema: DDDToolkit.HotChocolate.Analyzers
/// registers its public static methods as fields of that operation type in <c>Add{Module}GraphQlRuntimeBindings()</c>,
/// for a builder of that name, and for no other. The class carries nothing HotChocolate's own generator registers,
/// which would put it into every schema, and DDD00062 says where it does, and where a field would be lost.
/// </summary>
public class GraphQLSchemaGeneratorTests
{
    private const string Preamble =
        """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.HotChocolate.Attributes;
        using HotChocolate;
        using HotChocolate.Language;
        using HotChocolate.Types;

        namespace Sample;


        """;

    /// <summary>The bindings, where the registration is written.</summary>
    private const string Generated = "BindingExtensions";

    private const string Method = "public static global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder AddSalesGraphQlRuntimeBindings(this global::HotChocolate.Execution.Configuration.IRequestExecutorBuilder builder)";

    private const string Flags = "const global::System.Reflection.BindingFlags Flags = global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static;";

    private static GeneratorRunOutcome Run(string source, string module = "Sales")
        => GeneratorTestHost.Create(Preamble + source)
            .WithHotChocolate()
            .WithModule(module)
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

    [Fact]
    public void A_class_of_the_admin_schema_is_registered_by_the_bindings_for_a_builder_of_that_name_only()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static int GetLedgerOf(Guid customer) => 1;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, Method);
        result.ShouldContain(Generated, "switch (builder.Name)");
        result.ShouldContain(Generated, "case \"admin\":");
        result.ShouldContain(Generated, "builder.AddTypeExtension<QueryFieldsOfAdmin_0>();");
        result.ShouldContain(Generated, "private sealed class QueryFieldsOfAdmin_0 : global::HotChocolate.Types.ObjectTypeExtension");
        result.ShouldContain(Generated, Flags);
        result.ShouldContain(Generated, "descriptor.Name(global::HotChocolate.Types.OperationTypeNames.Query);");
        result.ShouldContain(Generated, "descriptor.Field(typeof(global::Sample.LedgerAdminQueries).GetMethod(\"GetLedgerOf\", Flags)!);");
        result.ShouldNotHaveDiagnostic("DDD00062");
    }

    [Fact]
    public void The_operation_type_is_made_sure_of_in_the_schema_as_HotChocolates_registration_makes_sure_of_it()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Mutation)]
            internal static class LedgerAdminMutations
            {
                public static int LedgerClose(Guid customer) => 1;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "builder.AddTypeExtension<MutationFieldsOfAdmin_0>();");
        result.ShouldContain(Generated, "builder.ConfigureSchema(static schema => schema.TryAddRootType(");
        result.ShouldContain(Generated, "static () => new global::HotChocolate.Types.ObjectType(static type => type.Name(global::HotChocolate.Types.OperationTypeNames.Mutation)),");
        result.ShouldContain(Generated, "global::HotChocolate.Language.OperationType.Mutation));");
        result.ShouldContain(Generated, "descriptor.Name(global::HotChocolate.Types.OperationTypeNames.Mutation);");
    }

    [Fact]
    public void A_class_marked_for_two_schemas_is_registered_for_each_of_them()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            [GraphQLSchema("support", OperationType.Query)]
            internal static class LedgerQueries
            {
                public static int GetLedgerOf(Guid customer) => 1;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "case \"admin\":");
        result.ShouldContain(Generated, "builder.AddTypeExtension<QueryFieldsOfAdmin_0>();");
        result.ShouldContain(Generated, "case \"support\":");
        result.ShouldContain(Generated, "builder.AddTypeExtension<QueryFieldsOfSupport_1>();");
    }

    [Fact]
    public void The_classes_of_one_schema_share_its_extension_in_the_order_of_their_names()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class ZoneAdminQueries
            {
                public static int GetZone() => 1;
            }

            [GraphQLSchema("admin", OperationType.Query)]
            internal static class AreaAdminQueries
            {
                public static int GetArea() => 1;

                public static int GetAreas() => 2;
            }
            """);

        result.ShouldCompile();
        var source = result.Source(Generated);
        source.Should().Contain("AddTypeExtension<QueryFieldsOfAdmin_0>", Exactly.Once(), "one extension of the admin schema's query type holds every class of it").And.NotContain("QueryFieldsOfAdmin_1");
        source.IndexOf("\"GetArea\"", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("\"GetAreas\"", StringComparison.Ordinal), "a class's fields are in the order they are written");
        source.IndexOf("\"GetAreas\"", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("\"GetZone\"", StringComparison.Ordinal), "the classes are in the order of their names");
    }

    [Fact]
    public void Only_a_public_static_method_that_is_not_ignored_is_a_field()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static int GetLedgerOf(Guid customer) => Helper();

                [GraphQLIgnore]
                public static int GetIgnored() => 1;

                internal static int GetInternal() => 1;

                private static int Helper() => 1;

                public static T GetGeneric<T>() => default!;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "\"GetLedgerOf\"");
        result.ShouldNotContain(Generated, "GetIgnored");
        result.ShouldNotContain(Generated, "GetInternal");
        result.ShouldNotContain(Generated, "Helper");
        result.ShouldNotContain(Generated, "GetGeneric");
    }

    [Fact]
    public void A_method_with_a_name_of_its_own_is_found_by_its_name_whatever_types_its_parameters_have()
    {
        // HotChocolate's generator writes a data loader's interface, and the toolkit's does not see what another
        // generator writes: a method that takes one is found all the same, and the bindings compile.
        var result = GeneratorTestHost.Create(Preamble +
                """
                [GraphQLSchema("admin", OperationType.Query)]
                internal static class LedgerAdminQueries
                {
                    public static Task<string> GetLedgerAsync(Guid id, ILedgerByIdDataLoader ledgers, CancellationToken cancellationToken) => Task.FromResult("");

                    public static Task<int> GetLedgerCountAsync() => Task.FromResult(1);
                }
                """)
            .WithHotChocolate()
            .WithModule("Sales")
            .RunCoreAnd([.. GeneratorTestHost.HotChocolateGenerators(), new DataLoaderInterfaceGenerator()]);

        result.ShouldCompile();
        result.ShouldContain(Generated, "descriptor.Field(typeof(global::Sample.LedgerAdminQueries).GetMethod(\"GetLedgerAsync\", Flags)!);");
        result.ShouldContain(Generated, "descriptor.Field(typeof(global::Sample.LedgerAdminQueries).GetMethod(\"GetLedgerCountAsync\", Flags)!);");
        result.ShouldNotHaveDiagnostic("DDD00062");
    }

    [Fact]
    public void A_method_whose_name_another_has_is_found_by_its_parameter_types_as_well_written_as_typeof_takes_them()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                [GraphQLName("ledgerByName")]
                public static string GetLedger(string? name, IReadOnlyList<string?> tags, (int Year, int Month) period, int? limit, CancellationToken cancellationToken) => "";

                [GraphQLName("ledgerById")]
                public static string GetLedger(Guid id) => "";

                [GraphQLName("anyLedger")]
                public static string GetLedger() => "";

                [GraphQLIgnore]
                public static int GetCount() => 1;

                public static int GetCount<T>() => 2;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "GetMethod(\"GetLedger\", Flags, new global::System.Type[] { typeof(string), typeof(global::System.Collections.Generic.IReadOnlyList<string>), typeof(global::System.ValueTuple<int, int>), typeof(int?), typeof(global::System.Threading.CancellationToken) })!");
        result.ShouldContain(Generated, "GetMethod(\"GetLedger\", Flags, new global::System.Type[] { typeof(global::System.Guid) })!");
        result.ShouldContain(Generated, "GetMethod(\"GetLedger\", Flags, global::System.Type.EmptyTypes)!");
        result.ShouldNotContain(Generated, "\"GetCount\"", "an ignored method and a generic one are no fields, though they share a name");
        result.ShouldNotHaveDiagnostic("DDD00062");
    }

    [Fact]
    public void A_method_whose_name_another_has_and_whose_parameter_another_generator_writes_is_refused()
    {
        // The two can only be told apart by their parameters, and the type of one is not there yet for this generator.
        var result = GeneratorTestHost.Create(Preamble +
                """
                [GraphQLSchema("admin", OperationType.Query)]
                internal static class LedgerAdminQueries
                {
                    [GraphQLName("ledgerById")]
                    public static Task<string> GetLedgerAsync(Guid id, ILedgerByIdDataLoader ledgers) => Task.FromResult("");

                    [GraphQLName("ledgerByName")]
                    public static Task<string> GetLedgerAsync(string name) => Task.FromResult(name);
                }
                """)
            .WithHotChocolate()
            .WithModule("Sales")
            .RunCoreAnd([.. GeneratorTestHost.HotChocolateGenerators(), new DataLoaderInterfaceGenerator()]);

        result.ShouldHaveDiagnostic("DDD00062", at: "GetLedgerAsync").GetMessage()
            .Should().Contain("its method 'GetLedgerAsync' has a name another public static method of the class has").And.Contain("'ledgers'").And.Contain("give the method a name of its own");
        result.Count("DDD00062").Should().Be(1);
        result.CompilationErrors.Should().BeEmpty("a refused class is named by no generated code");
    }

    [Fact]
    public void The_registration_follows_what_the_bindings_bind_in_the_same_method()
    {
        var result = Run(
            """
            [EntityId<Guid>("LDG")]
            public readonly partial record struct LedgerId;

            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static LedgerId GetLedger(LedgerId id) => id;
            }
            """);

        result.ShouldCompile();
        var source = result.Source(Generated);
        source.Should().Contain(Method, Exactly.Once(), "one method binds the ids and registers the classes: the one every schema calls");
        source.IndexOf("builder.BindRuntimeType<global::Sample.LedgerId", StringComparison.Ordinal)
            .Should().BePositive().And.BeLessThan(source.IndexOf("switch (builder.Name)", StringComparison.Ordinal), "the ids are bound for every schema, the classes for theirs");
        source.IndexOf("switch (builder.Name)", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("return builder;", StringComparison.Ordinal));
        result.ShouldContain(Generated, "descriptor.Field(typeof(global::Sample.LedgerAdminQueries).GetMethod(\"GetLedger\", Flags)!);");
    }

    [Fact]
    public void Without_anything_of_its_own_to_bind_it_gets_the_bindings_for_its_classes_and_calls_those_of_the_modules_other_projects()
    {
        var result = GeneratorTestHost.Create(Preamble.Replace("namespace Sample;", "namespace Sales.Api;") +
                """
                [GraphQLSchema("admin", OperationType.Query)]
                internal static class LedgerAdminQueries
                {
                    public static int GetLedgerCount() => 1;
                }
                """,
                "Api.cs")
            .WithAssemblyName("Sales.Api")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Sales\")]", "Module.cs")
            .WithHotChocolate()
            .WithReferencedProject(
                "Sales.Model",
                project => project
                    .WithSource(
                        """
                        using System;
                        using DDDToolkit.Abstractions.Attributes;

                        [assembly: Module("Sales")]

                        namespace Sales.Model;

                        [EntityId<Guid>("LDG")]
                        public readonly partial record struct LedgerId;
                        """,
                        "Model.cs"),
                GeneratorTestHost.HotChocolateGenerators())
            .RunCoreAnd(GeneratorTestHost.HotChocolateGenerators());

        // The model project binds its own id, so this one would write no bindings: it does for its class, and its
        // method calls the model's first, as the bindings of a project with ids of its own do.
        result.ShouldCompile();
        result.ShouldContain(Generated, "namespace Sales.Api.GraphQl;");
        result.ShouldContain(Generated, "global::Sales.Model.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(builder);");
        result.ShouldContain(Generated, "case \"admin\":");
    }

    [Fact]
    public void A_project_without_a_marked_class_gets_the_bindings_it_had()
    {
        var result = Run(
            """
            [EntityId<Guid>("LDG")]
            public readonly partial record struct LedgerId;

            internal static class LedgerQueries
            {
                [Query]
                public static int GetLedgerCount() => 1;
            }
            """);

        result.ShouldCompile();
        result.ShouldNotContain(Generated, "switch (builder.Name)", "a project that marks nothing keeps exactly what it had");
        result.ShouldNotContain(Generated, "GraphQLSchema");
        result.ShouldNotContain(Generated, "ObjectTypeExtension");
    }

    [Fact]
    public void A_project_with_neither_an_id_nor_a_marked_class_gets_no_bindings()
        => Run(
                """
                internal static class LedgerQueries
                {
                    [Query]
                    public static int GetLedgerCount() => 1;
                }
                """)
            .ShouldCompile()
            .GeneratedSources.Should().NotContain(source => source.HintName.Contains(Generated, StringComparison.Ordinal));

    [Fact]
    public void The_extensions_are_private_classes_of_the_bindings_and_add_nothing_an_application_can_name()
    {
        var emitted = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static int GetLedgerCount() => 1;
            }
            """,
            module: "Ledger").Emit();

        var bindings = emitted.Type(GeneratorTestHost.DefaultAssemblyName + ".GraphQl.HotChocolateExtensions");
        bindings.GetMethod("AddLedgerGraphQlRuntimeBindings")!.IsPublic.Should().BeTrue();
        bindings.GetNestedTypes(System.Reflection.BindingFlags.Public).Should().BeEmpty("the extensions are the bindings' own");
        bindings.GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Select(type => type.Name).Where(name => !name.StartsWith('<'))
            .Should().Equal("QueryFieldsOfAdmin_0");
    }

    [Fact]
    public void A_method_an_application_wrote_with_the_name_the_shop_sample_gives_its_own_is_no_clash()
    {
        // The shop sample writes Add{Module}GraphQL() by hand for each module, over the bindings: marking a class of
        // such a module asks nothing of it, and the method it wrote registers the class through the bindings it calls.
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static int GetLedgerCount() => 1;
            }

            public static class SalesGraphQL
            {
                public static HotChocolate.Execution.Configuration.IRequestExecutorBuilder AddSalesGraphQL(this HotChocolate.Execution.Configuration.IRequestExecutorBuilder graphql)
                    => DDDToolkit.Sample.GraphQl.HotChocolateExtensions.AddSalesGraphQlRuntimeBindings(graphql);
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "case \"admin\":");
        result.AllSources.Should().NotContain("AddSalesGraphQL(", "the toolkit writes no method of that name");
    }

    [Fact]
    public void A_subscriptions_stream_that_a_field_names_is_no_field()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Subscription)]
            internal static class LedgerAdminSubscriptions
            {
                public static async IAsyncEnumerable<int> SubscribeToTicks([EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    yield return 1;
                }

                [Subscribe(With = nameof(SubscribeToTicks))]
                public static int OnTick([EventMessage] int tick) => tick;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "descriptor.Name(global::HotChocolate.Types.OperationTypeNames.Subscription);");
        result.ShouldContain(Generated, "GetMethod(\"OnTick\"");
        result.ShouldNotContain(Generated, "GetMethod(\"SubscribeToTicks\"", "the stream is what the field subscribes to, not a field of its own");
    }

    [Fact]
    public void A_record_is_registered_as_a_class_is()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            public sealed record LedgerAdminQueries
            {
                public static int GetLedgerCount() => 1;
            }
            """);

        result.ShouldCompile();
        result.ShouldContain(Generated, "descriptor.Field(typeof(global::Sample.LedgerAdminQueries).GetMethod(\"GetLedgerCount\", Flags)!);");
        result.ShouldNotHaveDiagnostic("DDD00062");
    }

    // ------------------------------------------------------------------ DDD00062

    [Theory]
    [InlineData("QueryType")]
    [InlineData("MutationType")]
    [InlineData("ExtendObjectType(\"Query\")")]
    public void A_class_HotChocolates_generator_registers_as_well_is_refused(string marker)
    {
        var result = Run(
            "[GraphQLSchema(\"admin\", OperationType.Query)]\n[" + marker + "]\ninternal static class LedgerAdminQueries\n{\n    public static int GetLedgerCount() => 1;\n}\n");

        var diagnostic = result.ShouldHaveDiagnostic("DDD00062", at: marker);
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("'Sample.LedgerAdminQueries' is marked [GraphQLSchema]").And.Contain("registers in every schema");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains(Generated, StringComparison.Ordinal), "a class with an error is registered nowhere, and the project has nothing else to bind");
    }

    [Fact]
    public void A_method_HotChocolates_generator_makes_a_field_of_every_schema_is_refused()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                [Query]
                public static int GetLedgerCount() => 1;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "GetLedgerCount").GetMessage()
            .Should().Contain("its method 'GetLedgerCount' is marked [Query]");
        result.Count("DDD00062").Should().Be(1, "what is reported is the reason the class has no field, and nothing more");
    }

    [Fact]
    public void A_record_HotChocolates_generator_registers_as_well_is_refused_as_a_class_is()
    {
        // HotChocolate's generator reads a record's [Query] methods, and would put this one into every schema.
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            public sealed record LedgerAdminQueries
            {
                [Query]
                public static int GetLedgerCount() => 42;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "GetLedgerCount").GetMessage()
            .Should().Contain("'Sample.LedgerAdminQueries' is marked [GraphQLSchema]").And.Contain("is marked [Query]");
    }

    [Fact]
    public void A_class_derived_from_one_of_HotChocolates_type_classes_is_refused()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal sealed class LedgerAdminQueries : ObjectTypeExtension
            {
                public static int GetLedgerCount() => 1;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "LedgerAdminQueries").GetMessage()
            .Should().Contain("derives from ObjectTypeExtension");
    }

    [Fact]
    public void An_attribute_that_names_no_schema_is_refused()
    {
        var result = Run(
            """
            [GraphQLSchema(" ", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static int GetLedgerCount() => 1;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "GraphQLSchema(\" \", OperationType.Query)").GetMessage()
            .Should().Contain("names no schema");
    }

    [Fact]
    public void A_generic_class_a_private_one_and_a_file_class_are_refused()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries<T>
            {
                public static int GetLedgerCount() => 1;
            }

            internal static class Outer
            {
                [GraphQLSchema("admin", OperationType.Query)]
                private static class HiddenAdminQueries
                {
                    public static int GetHidden() => 1;
                }
            }

            [GraphQLSchema("admin", OperationType.Query)]
            file static class FileAdminQueries
            {
                public static int GetFileLocal() => 1;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "LedgerAdminQueries").GetMessage().Should().Contain("is generic");
        result.ShouldHaveDiagnostic("DDD00062", at: "HiddenAdminQueries").GetMessage().Should().Contain("private or protected");
        result.ShouldHaveDiagnostic("DDD00062", at: "FileAdminQueries").GetMessage().Should().Contain("is a file class");
        result.CompilationErrors.Should().BeEmpty("none of the three is named by generated code, which would not compile");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains(Generated, StringComparison.Ordinal), "none of the classes is registered");
    }

    [Fact]
    public void A_class_whose_methods_are_instance_methods_is_refused_and_each_is_named()
    {
        // HotChocolate's [QueryType] classes may hold instance methods: one moved here as it was would lose them.
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            public sealed class LedgerAdminQueries
            {
                public int GetLedgerCount() => 1;

                public string GetLedgerName() => "";

                public override string ToString() => "ledger";
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "GetLedgerCount").GetMessage()
            .Should().Contain("its method 'GetLedgerCount' is an instance method, which no schema calls").And.Contain("make it static");
        result.ShouldHaveDiagnostic("DDD00062", at: "GetLedgerName");
        result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00062").Should().HaveCount(2, "an override is no field anywhere");
    }

    [Fact]
    public void A_class_with_no_field_is_refused_and_nothing_is_written_for_it()
    {
        var result = Run(
            """
            [EntityId<Guid>("LDG")]
            public readonly partial record struct LedgerId;

            [GraphQLSchema("ops", OperationType.Mutation)]
            internal static class LedgerOpsMutations
            {
                [GraphQLIgnore]
                public static int Ignored() => 1;

                internal static int Internal() => 1;
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "LedgerOpsMutations").GetMessage().Should().Contain("has no field");
        result.ShouldNotContain(Generated, "switch (builder.Name)", "no extension, and no root type that would have no field");
        result.ShouldNotContain(Generated, Flags);
        result.CompilationDiagnostics.Where(diagnostic => diagnostic.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning && diagnostic.Location.SourceTree?.FilePath.Contains(Generated, StringComparison.Ordinal) == true)
            .Should().BeEmpty("the bindings compile without a warning when a class is refused");
    }

    [Fact]
    public void Two_overloads_are_one_field_and_are_refused()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static string GetLedger(string name) => name;

                public static string GetLedger(Guid id) => "";
            }
            """);

        var diagnostic = result.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00062").Subject;
        diagnostic.GetMessage().Should().Contain("its method 'LedgerAdminQueries.GetLedger(Guid)' is the field 'ledger' of the Query type of the schema \"admin\"")
            .And.Contain("as 'LedgerAdminQueries.GetLedger(string)' is").And.Contain("[GraphQLName]");
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains(Generated, StringComparison.Ordinal), "neither is registered");
    }

    [Fact]
    public void Two_methods_of_two_classes_that_are_one_field_of_one_schema_are_refused_and_a_name_tells_two_apart()
    {
        var result = Run(
            """
            [GraphQLSchema("admin", OperationType.Query)]
            internal static class LedgerAdminQueries
            {
                public static Task<string> GetLedgerAsync(Guid id) => Task.FromResult("");
            }

            [GraphQLSchema("admin", OperationType.Query)]
            internal static class OtherAdminQueries
            {
                public static string Ledger(Guid id) => "";
            }

            [GraphQLSchema("admin", OperationType.Query)]
            internal static class NamedAdminQueries
            {
                public static string GetBook(string name) => name;

                [GraphQLName("bookById")]
                public static string GetBook(Guid id) => "";
            }

            [GraphQLSchema("support", OperationType.Query)]
            internal static class SupportQueries
            {
                public static string GetLedger(Guid id) => "";
            }
            """);

        result.ShouldHaveDiagnostic("DDD00062", at: "Ledger").GetMessage()
            .Should().Contain("'Sample.OtherAdminQueries' is marked [GraphQLSchema]").And.Contain("the field 'ledger'").And.Contain("'LedgerAdminQueries.GetLedgerAsync(Guid)'");
        result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00062").Should().ContainSingle("[GraphQLName] tells two methods apart, and one name in two schemas is two fields");
        result.ShouldContain(Generated, "GetMethod(\"GetBook\"");
        result.ShouldContain(Generated, "case \"support\":");
        result.ShouldNotContain(Generated, "LedgerAdminQueries", "a class of two methods that are one field is registered nowhere");
    }

    [Fact]
    public void An_unmarked_class_with_HotChocolates_attributes_is_none_of_this_diagnostics_business()
    {
        var result = Run(
            """
            [QueryType]
            internal static partial class LedgerQueries
            {
                public static int GetLedgerCount() => 1;
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00062");
    }

    /// <summary>
    /// Writes an interface as HotChocolate's generator writes a data loader's: from what it reads of the
    /// compilation, so the toolkit's generators, which run beside it, do not see it, while the compilation that comes
    /// out of them has it. What a generator writes before it reads anything, as an attribute, every generator sees.
    /// </summary>
    private sealed class DataLoaderInterfaceGenerator : Microsoft.CodeAnalysis.IIncrementalGenerator
    {
        public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext context)
            => context.RegisterSourceOutput(context.CompilationProvider, static (output, _) => output.AddSource(
                "LedgerDataLoader.g.cs",
                "namespace Sample;\n\npublic interface ILedgerByIdDataLoader { }\n"));
    }
}
