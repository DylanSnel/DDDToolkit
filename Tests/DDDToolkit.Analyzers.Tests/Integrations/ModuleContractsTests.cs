using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// A module's contracts project says once that every public type of it is published to the other modules:
/// <c>[assembly: ModuleContracts]</c>, or <c>DDD_ModuleContracts</c> set to true, from which the toolkit's generator writes
/// that attribute. No type of it then needs a <c>[ModuleContract]</c> of its own, and one that has it keeps it.
/// <para>
/// The first half is about the attribute the property writes: only for <c>true</c>, never beside one the project
/// declares, and never because of the project's name; DDD00068 where the project cannot name the attribute; and the
/// project's own <c>{Module}EventNames</c>, written the same by both ways of saying it. The second half is what another module makes of it, which is
/// what the attribute is for: it names every public type of the project without DDD00022, and nothing else; it still
/// may not hold the project's entities (DDD00023). Converters and GraphQL bindings of another module's published ids
/// follow the same reading, in <see cref="EntityFrameworkGeneratorTests"/> and <see cref="HotChocolateGeneratorTests"/>.
/// </para>
/// </summary>
public class ModuleContractsTests
{
    /// <summary>Crm's contracts project: an id, a read model, an interface, an enum and keys, none of them marked.</summary>
    private const string CrmContracts =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Crm.Contracts;

        [EntityId<Guid>("CUS")]
        public readonly partial record struct CustomerId;

        public sealed record CustomerSummary(CustomerId Id, string Name);

        public interface ICustomerDirectory
        {
            CustomerSummary? Find(CustomerId id);
        }

        public enum CustomerTier
        {
            Standard,
            Gold,
        }

        public static class CustomerKeys
        {
            public const string View = "crm.customers.view";

            public static class Addresses
            {
                public const string Edit = "crm.addresses.edit";
            }
        }
        """;

    /// <summary>Sales, naming every type of Crm's contracts.</summary>
    private const string SalesNamingEverything =
        """
        using Crm.Contracts;

        namespace Sales;

        public sealed class InvoiceHeader(ICustomerDirectory directory)
        {
            public CustomerTier Tier { get; init; } = CustomerTier.Gold;

            public string Describe(CustomerId customer)
                => directory.Find(customer)?.Name ?? CustomerKeys.View + CustomerKeys.Addresses.Edit;
        }
        """;

    private const string Declared = "[assembly: global::DDDToolkit.Abstractions.Attributes.ModuleContractsAttribute]";

    /// <summary>A public domain event of Crm's contracts project, which {Module}EventNames names.</summary>
    private const string CustomerRenamed =
        """
        using DDDToolkit.BaseTypes;

        namespace Crm.Contracts;

        public sealed record CustomerRenamed(CustomerId Id, string Name) : DomainEvent;
        """;

    /// <summary>
    /// What a project sees of a DDDToolkit.Abstractions from before <c>[assembly: ModuleContracts]</c>, written out
    /// in its source: <c>[ModuleContract]</c> is there, and the assembly attribute is not.
    /// </summary>
    private const string OlderAbstractions =
        """
        namespace DDDToolkit.Abstractions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface | System.AttributeTargets.Enum, Inherited = false)]
            public sealed class ModuleContractAttribute : System.Attribute
            {
            }
        }
        """;

    /// <summary>A contracts type that needs nothing of the toolkit.</summary>
    private const string CrmSummary =
        """
        namespace Crm.Contracts;

        public sealed record CustomerSummary(string Name);
        """;

    /// <summary>Crm's contracts project, a project of module Crm as the build declares it, and nothing more.</summary>
    private static GeneratorTestHost Contracts(string source = CrmContracts, string assemblyName = "Crm.Contracts")
        => GeneratorTestHost.Create(source, "Contracts.cs").WithAssemblyName(assemblyName).WithModuleFromTheBuild("Crm");

    /// <summary>Module Sales, referencing Crm's contracts project as <paramref name="crm"/> builds it, with the module analyzer.</summary>
    private static GeneratorRunOutcome Sales(string source, Func<GeneratorTestHost, GeneratorTestHost> crm, string crmAssembly = "Crm.Contracts")
        => GeneratorTestHost.Create(source, "Sales.cs")
            .WithModuleFromTheBuild("Sales")
            .WithReferencedProject(crmAssembly, project => crm(project.WithModuleFromTheBuild("Crm")))
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

    /// <summary>The <c>[assembly: ModuleContracts]</c> attributes the compiled assembly carries, as reflection reads them.</summary>
    private static int ContractsAttributesOf(GeneratorRunOutcome result)
        => result.Emit().Assembly.GetCustomAttributes<ModuleContractsAttribute>().Count();

    // ------------------------------------------------------------------ the attribute the property writes

    [Fact]
    public void DDD_ModuleContracts_makes_the_project_its_modules_contracts_and_the_assembly_carries_it()
    {
        var result = Contracts().WithBuildProperty("DDD_ModuleContracts", "true").RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain("ModuleContracts.g.cs", Declared);
        ContractsAttributesOf(result).Should().Be(1, "another module's analyzer and generators read it from the compiled assembly");
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public void DDD_ModuleContracts_counts_when_it_is_true_as_MSBuild_compares_it(string value)
        => Contracts().WithBuildProperty("DDD_ModuleContracts", value).RunCore().ShouldContain("ModuleContracts.g.cs", Declared);

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("1")]
    public void Anything_but_true_makes_no_contracts(string value)
    {
        var result = Contracts().WithBuildProperty("DDD_ModuleContracts", value).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("ModuleContracts.g.cs");
        ContractsAttributesOf(result).Should().Be(0);
    }

    [Theory]
    [InlineData("a file of the project")]
    [InlineData("an AssemblyAttribute item")]
    public void An_attribute_the_project_declares_is_kept_and_nothing_is_written_beside_it(string declaredBy)
    {
        var (source, path) = declaredBy == "a file of the project"
            ? ("[assembly: DDDToolkit.Abstractions.Attributes.ModuleContracts]", "Module.cs")
            : ("// <auto-generated/>\n[assembly: DDDToolkit.Abstractions.Attributes.ModuleContractsAttribute]", "obj/Debug/net10.0/Crm.Contracts.AssemblyInfo.cs");

        var result = Contracts().WithSource(source, path).WithBuildProperty("DDD_ModuleContracts", "true").RunCore();

        result.ShouldCompile();
        result.CompilationDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS0579", "a second [assembly: ModuleContracts] does not compile");
        result.HintNames.Should().NotContain("ModuleContracts.g.cs");
        ContractsAttributesOf(result).Should().Be(1, "{0} declares it, and nothing else does", declaredBy);
    }

    [Theory]
    [InlineData("Crm.Contracts")]
    [InlineData("Contracts")]
    public void A_project_is_never_its_modules_contracts_because_of_its_name(string assemblyName)
    {
        // A module may be about contracts of another kind, and its project of that name its domain.
        var result = Contracts(assemblyName: assemblyName).RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("ModuleContracts.g.cs");
        ContractsAttributesOf(result).Should().Be(0);
    }

    [Fact]
    public void The_attribute_is_cached_across_an_unrelated_edit()
    {
        var first = Contracts().WithBuildProperty("DDD_ModuleContracts", "true").RunCore();

        var second = first.RunAgain((compilation, options) => compilation.AddSyntaxTrees(
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace Crm.Contracts;\n\npublic static class Unrelated;", options, "Unrelated.cs")));

        second.ShouldContain("ModuleContracts.g.cs", Declared);
        second.Driver.GetRunResult().Results
            .Single(result => result.Generator.GetGeneratorType() == typeof(ModuleContractsGenerator))
            .TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(run => run.Outputs).Select(output => output.Reason)
            .Should().OnlyContain(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }

    // ------------------------------------------------------------------ a property that cannot be carried out

    [Theory]
    [InlineData("an older DDDToolkit.Abstractions")]
    [InlineData("no DDDToolkit.Abstractions")]
    public void A_project_that_cannot_name_the_attribute_hears_DDD00068_at_its_project_file(string references)
    {
        // The two packages are referenced separately, so the analyzers can be newer than the attribute. Without this
        // the explicit choice is dropped, and the first sign is DDD00022 in the other modules, pointing away from it.
        var host = Contracts(CrmSummary).WithoutTheToolkitAssemblies().WithBuildProperty("DDD_ModuleContracts", "true");
        if (references == "an older DDDToolkit.Abstractions")
        {
            host = host.WithSource(OlderAbstractions, "OlderAbstractions.cs");
        }

        var result = host.RunCore();

        result.ShouldCompile();
        result.HintNames.Should().NotContain("ModuleContracts.g.cs", "there is no attribute to write it with");
        var reported = result.ReportedDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "DDD00068").Subject;
        reported.Severity.Should().Be(DiagnosticSeverity.Warning, "the project compiles; it publishes less than it asked for");
        reported.Location.GetLineSpan().Path.Should().Be(host.ProjectFile, "no line of code is wrong");
        reported.GetMessage().Should().Be(
            "DDD_ModuleContracts is true, and the project publishes nothing by it: the toolkit's generator writes [assembly: ModuleContracts] from it, and no DDDToolkit.Abstractions the project references declares that attribute. Reference the DDDToolkit.Abstractions of the same version as DDDToolkit.Analyzers, or mark each type the other modules may name [ModuleContract].");
    }

    [Theory]
    [InlineData("")]
    [InlineData("false")]
    public void A_project_that_cannot_name_the_attribute_and_does_not_ask_for_it_hears_nothing(string value)
    {
        var result = Contracts(CrmSummary).WithoutTheToolkitAssemblies().WithBuildProperty("DDD_ModuleContracts", value).RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00068");
    }

    // ------------------------------------------------------------------ the project's own generated code

    [Fact]
    public void EventNames_is_written_the_same_whether_the_build_or_a_file_of_the_project_says_it()
    {
        // No generator sees the attribute written from the property, so the one that marks {Module}EventNames reads the
        // property too; otherwise the class would carry [ModuleContract] by one way of saying it and not by the other.
        var byTheBuild = Contracts()
            .WithSource(CustomerRenamed, "CustomerRenamed.cs")
            .WithBuildProperty("DDD_ModuleContracts", "true")
            .RunCore();
        var byAFile = Contracts()
            .WithSource(CustomerRenamed, "CustomerRenamed.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.ModuleContracts]", "AssemblyInfo.cs")
            .RunCore();

        byTheBuild.ShouldCompile();
        byAFile.ShouldCompile();
        byAFile.ShouldContain("EventNames", "[global::DDDToolkit.Abstractions.Attributes.ModuleContract]", "the event is public in a contracts project, so every name the class holds is published");
        byTheBuild.Source("EventNames").Should().Be(byAFile.Source("EventNames"));
    }

    [Fact]
    public void EventNames_of_a_contracts_project_is_not_published_while_it_names_an_internal_event()
    {
        var result = Contracts()
            .WithSource(CustomerRenamed.Replace("public sealed record", "internal sealed record", StringComparison.Ordinal), "CustomerRenamed.cs")
            .WithBuildProperty("DDD_ModuleContracts", "true")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain("EventNames", "public const string CustomerRenamed = \"crm.customer-renamed\";");
        result.ShouldNotContain("EventNames", "ModuleContract", "the project keeps the event to itself");
    }

    // ------------------------------------------------------------------ what another module makes of it

    [Fact]
    public void Another_module_names_every_public_type_of_a_contracts_project_that_says_so_itself()
    {
        var result = Sales(SalesNamingEverything, crm => crm
            .WithSource(CrmContracts, "Contracts.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.ModuleContracts]", "AssemblyInfo.cs"));

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void Another_module_names_every_public_type_of_a_contracts_project_its_build_declared()
    {
        // No file of Crm's says it: a Directory.Build.props set DDD_ModuleContracts, and the compiled assembly carries
        // the attribute the generator wrote from it.
        var result = Sales(SalesNamingEverything, crm => crm
            .WithSource(CrmContracts, "Contracts.cs")
            .WithBuildProperty("DDD_ModuleContracts", "true"));

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
    }

    [Fact]
    public void A_project_named_Contracts_that_says_nothing_publishes_nothing()
    {
        var result = Sales(SalesNamingEverything, crm => crm.WithSource(CrmContracts, "Contracts.cs"));

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "ICustomerDirectory").GetMessage().Should().Contain("module 'Crm'");
        result.ShouldHaveDiagnostic("DDD00022", at: "CustomerTier");
        result.ShouldHaveDiagnostic("DDD00022", at: "CustomerId");
        result.ShouldHaveDiagnostic("DDD00022", at: "CustomerKeys");
        result.ShouldHaveDiagnostic("DDD00022", at: "Addresses");
    }

    [Fact]
    public void A_type_a_contracts_project_keeps_internal_is_not_published_even_to_a_friend()
    {
        // The other module may name it, since the project lets it in, and it is still none of the contract.
        var result = Sales(
            """
            namespace Sales;

            public sealed class InvoiceHeader
            {
                public string Describe() => Crm.Contracts.CustomerLedger.Name + Crm.Contracts.CustomerKeys.View;
            }
            """,
            crm => crm
                .WithSource(CrmContracts, "Contracts.cs")
                .WithSource(
                    """
                    [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DDDToolkit.Sample")]

                    namespace Crm.Contracts;

                    internal static class CustomerLedger
                    {
                        public const string Name = "ledger";
                    }
                    """,
                    "Ledger.cs")
                .WithBuildProperty("DDD_ModuleContracts", "true"));

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00022", at: "CustomerLedger");
        result.Count("DDD00022").Should().Be(1, "the public keys beside it are published");
    }

    [Fact]
    public void A_types_own_ModuleContract_keeps_working_beside_the_projects()
    {
        var result = Sales(SalesNamingEverything, crm => crm
            .WithSource(CrmContracts.Replace("[EntityId<Guid>(\"CUS\")]", "[ModuleContract]\n[EntityId<Guid>(\"CUS\")]", StringComparison.Ordinal), "Contracts.cs")
            .WithBuildProperty("DDD_ModuleContracts", "true"));

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("saying it twice is no mistake, and no rule reads it as one");
    }

    [Fact]
    public void An_entity_of_a_contracts_project_is_published_and_still_not_held()
    {
        var result = Sales(
            """
            using System;
            using DDDToolkit.Abstractions.Attributes;
            using Crm.Contracts;

            namespace Sales;

            [AggregateRoot<Guid>("INV")]
            public partial class Invoice
            {
                public Customer? Buyer { get; private set; }
            }
            """,
            crm => crm
                .WithSource(
                    """
                    using DDDToolkit.Abstractions.Attributes;

                    namespace Crm.Contracts;

                    [AggregateRoot<CustomerId>]
                    public partial class Customer
                    {
                        public string Name { get; private set; } = string.Empty;
                    }
                    """,
                    "Customer.cs")
                .WithSource(CrmContracts, "Contracts.cs")
                .WithBuildProperty("DDD_ModuleContracts", "true"));

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00022");
        result.ShouldHaveDiagnostic("DDD00023", at: "Buyer");
    }
}
