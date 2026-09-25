namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// The four rules about event names: DDD00034 (the class name's version and <c>Version</c> disagree),
/// DDD00035 (a suffix that cannot be a version), DDD00036 (two events of one module under one name and
/// version) and DDD00037 (two names that would share a constant).
/// <para>
/// The near misses matter as much as the probes. A domain event and the contract it is published as share a
/// name on purpose, and so does every version of one event; reporting either would punish the design the
/// convention exists for.
/// </para>
/// </summary>
public class EventNameDiagnosticTests
{
    private const string Preamble =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.BaseTypes;

        [assembly: Module("Ordering")]


        """;

    private static GeneratorRunOutcome Run(string source) => GeneratorTestHost.Create(Preamble + source).RunCore();

    // ------------------------------------------------------------------ DDD00034

    [Fact]
    public void A_Version_that_disagrees_with_the_class_name_reports_DDD00034_at_the_Version()
    {
        var result = Run(
            """
            namespace Ordering.Contracts;

            [IntegrationEvent(Version = 3)]
            public sealed record OrderPlacedV2(string OrderId);
            """);

        var diagnostic = result.ShouldHaveDiagnostic("DDD00034", at: "Version = 3");
        diagnostic.GetMessage().Should().Contain("version 2 by its name").And.Contain("Version = 3");
        diagnostic.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("[IntegrationEvent(Version = 2)]\npublic sealed record OrderPlacedV2(string OrderId);", "the two agree")]
    [InlineData("[IntegrationEvent(Version = 4)]\npublic sealed record OrderPlaced(string OrderId);", "a name without a suffix leaves the version to the attribute")]
    [InlineData("[IntegrationEvent]\npublic sealed record OrderPlacedV2(string OrderId);", "the name alone says it")]
    [InlineData("[IntegrationEvent(\"ordering.placed\", Version = 2)]\npublic sealed record OrderPlacedV2(string OrderId);", "a pinned name with an agreeing version")]
    public void A_Version_that_agrees_or_stands_alone_is_fine(string declaration, string because)
    {
        var result = Run("namespace Ordering.Contracts;\n\n" + declaration);

        result.ShouldNotHaveDiagnostic("DDD00034");
        result.ShouldCompile();
        _ = because;
    }

    // ------------------------------------------------------------------ DDD00035

    [Theory]
    [InlineData("OrderPlacedV0", "0")]
    [InlineData("OrderPlacedV01", "01")]
    public void A_suffix_that_cannot_be_a_version_reports_DDD00035(string className, string digits)
    {
        var result = Run($"namespace Ordering;\n\npublic sealed record {className}(string OrderId) : DomainEvent;");

        result.ShouldHaveDiagnostic("DDD00035", at: className).GetMessage().Should().Contain($"'V{digits}'");
    }

    [Theory]
    [InlineData("Level2Reached")]
    [InlineData("OrderPlacedV10")]
    [InlineData("Ipv6Assigned")]
    public void Digits_that_are_not_a_malformed_version_are_fine(string className)
        => Run($"namespace Ordering;\n\npublic sealed record {className}(string OrderId) : DomainEvent;").ShouldNotHaveDiagnostic("DDD00035");

    [Fact]
    public void A_class_that_is_not_an_event_is_not_read_for_a_version()
        => Run("namespace Ordering;\n\npublic sealed class ApiV0;").ShouldNotHaveDiagnostic("DDD00035");

    // ------------------------------------------------------------------ DDD00036

    [Fact]
    public void Two_domain_events_with_one_class_name_in_one_module_report_DDD00036_on_both()
    {
        var result = GeneratorTestHost.Create(
                Preamble + "namespace Ordering.Orders;\n\npublic sealed record OrderPlaced(string OrderId) : DomainEvent;",
                "Orders.cs")
            .WithSource("using DDDToolkit.BaseTypes;\n\nnamespace Ordering.Returns;\n\npublic sealed record OrderPlaced(string OrderId) : DomainEvent;", "Returns.cs")
            .RunCore();

        result.Count("DDD00036").Should().Be(2, "neither of the two is more wrong than the other");

        var diagnostic = result.ReportedDiagnostics.First(candidate => candidate.Id == "DDD00036" && candidate.Location.GetLineSpan().Path == "Returns.cs");
        diagnostic.GetMessage().Should()
            .Contain("'Ordering.Returns.OrderPlaced' and 'Ordering.Orders.OrderPlaced'")
            .And.Contain("'ordering.order-placed' version 1")
            .And.Contain("[DomainEventName(\"ordering.returns-order-placed\")]");
        diagnostic.Properties["SuggestedName"].Should().Be("ordering.returns-order-placed", "the fix pins the name with the namespace it is in");
        diagnostic.Properties["PinWith"].Should().Be("DomainEventName");
    }

    [Fact]
    public void Two_contracts_under_one_name_and_version_report_DDD00036_and_pin_in_IntegrationEvent()
    {
        var result = Run(
            """
            namespace Ordering.Contracts
            {
                [IntegrationEvent]
                public sealed record OrderPlacedV1(string OrderId);
            }

            namespace Ordering.Legacy
            {
                [IntegrationEvent]
                public sealed record OrderPlaced(string OrderId);
            }
            """);

        var diagnostic = result.ShouldHaveDiagnostic("DDD00036", at: "OrderPlaced");
        diagnostic.GetMessage().Should().Contain("'ordering.order-placed' version 1", "no suffix is version 1, the same as V1");
        diagnostic.Properties["PinWith"].Should().Be("IntegrationEvent");
        diagnostic.Properties["SuggestedName"].Should().Be("ordering.legacy-order-placed");
    }

    [Fact]
    public void A_pinned_name_that_collides_is_reported_without_a_suggestion()
    {
        var result = Run(
            """
            namespace Ordering;

            [DomainEventName("ordering.placed")]
            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            [DomainEventName("ordering.placed")]
            public sealed record PlacedOrder(string OrderId) : DomainEvent;
            """);

        result.Count("DDD00036").Should().Be(2);
        result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00036")
            .Should().OnlyContain(diagnostic => !diagnostic.Properties.ContainsKey("SuggestedName"), "a name chosen by hand is changed by hand");
    }

    [Fact]
    public void A_domain_event_and_the_contract_it_is_published_as_may_share_a_name()
    {
        var result = Run(
            """
            namespace Ordering
            {
                public sealed record OrderPlaced(string OrderId) : DomainEvent;
            }

            namespace Ordering.Contracts
            {
                [IntegrationEvent]
                public sealed record OrderPlacedV1(string OrderId);
            }
            """);

        result.ShouldNotHaveDiagnostic("DDD00036");
        result.ShouldCompile();
    }

    [Fact]
    public void Two_versions_of_one_event_may_share_a_name()
    {
        var result = Run(
            """
            namespace Ordering;

            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            public sealed record OrderPlacedV2(string OrderId, decimal Total) : DomainEvent;
            """);

        result.ShouldNotHaveDiagnostic("DDD00036");
        result.ShouldCompile();
    }

    [Fact]
    public void One_class_name_in_two_modules_is_two_names()
    {
        var result = GeneratorTestHost.Create(Preamble + "namespace Ordering;\n\npublic sealed record Cancelled(string Id) : DomainEvent;")
            .WithReferencedAssembly(
                "using DDDToolkit.Abstractions.Attributes;\nusing DDDToolkit.BaseTypes;\n\n[assembly: Module(\"Payments\")]\n\nnamespace Payments;\n\npublic sealed record Cancelled(string Id) : DomainEvent;",
                "Payments")
            .RunCore();

        result.ShouldNotHaveDiagnostic("DDD00036");
        result.ShouldContain("EventNames", "Cancelled = \"ordering.cancelled\"");
    }

    // ------------------------------------------------------------------ DDD00037

    [Fact]
    public void Two_names_that_give_one_constant_report_DDD00037_and_keep_the_first()
    {
        var result = Run(
            """
            namespace Ordering;

            [DomainEventName("ordering.order-placed")]
            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            [DomainEventName("ordering.order.placed")]
            public sealed record PlacedOrder(string OrderId) : DomainEvent;
            """);

        result.ShouldHaveDiagnostic("DDD00037", at: "PlacedOrder").GetMessage().Should().Contain("'OrderPlaced'");
        result.ShouldContain("EventNames", "public const string OrderPlaced = \"ordering.order-placed\";");
        result.ShouldNotContain("EventNames", "ordering.order.placed\";");
        result.ShouldCompile();
    }
}
