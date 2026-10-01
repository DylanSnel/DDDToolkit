using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00014: the MSBuild properties the generators read never reached the compiler, because the props file
/// that declares them was not imported. Whatever the project sets for <c>DDD_Module</c> is then ignored.
/// <para>
/// The near miss is the case every project that sets no <c>DDD_Module</c> is in: the property is declared
/// and empty. That has to stay silent, or the warning would fire in every project that is set up correctly.
/// build/verify-package-consumption.sh proves the same against the packed packages, where the declaration
/// really does come from the props file.
/// </para>
/// </summary>
public class BuildPropertyDiagnosticTests
{
    private const string Contracts =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Acme.Billing.Contracts;

        [IntegrationEvent]
        public sealed record InvoiceSent(string InvoiceId);
        """;

    private const string NoEvents =
        """
        namespace Acme.Billing.Contracts;

        public sealed record InvoiceSummary(string InvoiceId);
        """;

    [Fact]
    public void A_property_that_never_reached_the_compiler_reports_DDD00014_once_and_names_the_assembly()
    {
        var result = GeneratorTestHost.Create(Contracts)
            .WithAssemblyName("Acme.Billing.Contracts")
            .WithoutBuildProperties()
            .RunCore();

        result.Count("DDD00014").Should().Be(1, "it is about the project, not about each event");

        var diagnostic = result.ReportedDiagnostics.Single(diagnostic => diagnostic.Id == "DDD00014");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning, "the code still compiles; only the names are not the ones asked for");
        diagnostic.Location.Should().Be(Location.None, "no line of the project's code is wrong");
        diagnostic.GetMessage().Should().Contain("'Acme.Billing.Contracts'").And.Contain("DDD_Module is ignored");

        result.ShouldCompile();
        result.ShouldContain("EventNames", "public static class AcmeBillingContractsEventNames", "the fallback the warning is about");
    }

    [Fact]
    public void It_is_reported_before_the_project_has_anything_to_name()
        => GeneratorTestHost.Create(NoEvents)
            .WithoutBuildProperties()
            .RunCore()
            .Count("DDD00014").Should().Be(1, "the setup is what is wrong, and the first event should not be what reveals it");

    [Fact]
    public void A_property_that_is_declared_and_not_set_is_silent()
    {
        // What the props file gives a project that sets no DDD_Module: the key is there and its value is empty.
        var result = GeneratorTestHost.Create(Contracts).WithAssemblyName("Acme.Billing.Contracts").RunCore();

        result.ShouldNotHaveDiagnostic("DDD00014");
        result.ShouldContain("EventNames", "public static class AcmeBillingContractsEventNames", "named after the assembly because the project asked for nothing else");
    }

    [Fact]
    public void A_property_that_is_set_is_silent_and_names_the_class()
    {
        var result = GeneratorTestHost.Create(Contracts)
            .WithAssemblyName("Acme.Billing.Contracts")
            .WithModule("Billing")
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00014");
        result.ShouldContain("EventNames", "public static class BillingEventNames");
    }

    [Fact]
    public void A_module_is_silent_because_its_class_is_named_after_the_module()
    {
        var result = GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;

                [assembly: Module("Billing")]

                namespace Acme.Billing.Contracts;

                [IntegrationEvent]
                public sealed record InvoiceSent(string InvoiceId);
                """)
            .WithAssemblyName("Acme.Billing.Contracts")
            .WithoutBuildProperties()
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00014");
        result.ShouldContain("EventNames", "public static class BillingEventNames", "[assembly: Module] names it without the property");
    }

    [Fact]
    public void NoWarn_silences_it_for_a_project_that_means_to_build_without_the_properties()
        => GeneratorTestHost.Create(Contracts)
            .WithoutBuildProperties()
            .WithNoWarn("DDD00014")
            .RunCore()
            .ShouldNotHaveDiagnostic("DDD00014");
}
