using DDDToolkit.EntityFramework.Options;

namespace DDDToolkit.Analyzers.Tests.Integrations;

/// <summary>
/// DDDToolkit.EntityFramework.Analyzers' integration event registration: the three
/// <c>Add{Module}IntegrationEvents()</c> methods, with every name and version the run-time registration
/// would otherwise read off attributes written out as literals.
/// </summary>
public class IntegrationEventsGeneratorTests
{
    private const string Hint = "IntegrationEventExtensions";
    private const string Services = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions";

    private const string Contracts =
        """
        using DDDToolkit.Abstractions.Attributes;

        namespace Billing.Contracts;

        [IntegrationEvent("billing.invoice-raised", Version = 2)]
        public sealed record InvoiceRaisedV2(string InvoiceId);
        """;

    private const string Module =
        """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Billing.Contracts;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.BaseTypes;
        using DDDToolkit.EntityFramework.Integration;

        namespace Sales;

        [DomainEventName("sales.order-placed")]
        public sealed record OrderPlaced(string OrderId) : DomainEvent;

        public sealed record OrderShipped(string OrderId) : DomainEvent;

        [IntegrationEvent("sales.order-placed", Version = 1)]
        public sealed record OrderPlacedV1(string OrderId);

        public sealed class PublishOrderPlaced : IOutboundIntegrationEvent<OrderPlaced, OrderPlacedV1>
        {
            public ValueTask<OrderPlacedV1?> CreateAsync(OrderPlaced placed, CancellationToken cancellationToken)
                => new(new OrderPlacedV1(placed.OrderId));
        }

        public sealed class Clock;

        [IntegrationEventConsumer("sales.invoices")]
        public sealed class RecordInvoice(Clock clock, IServiceProvider services, string? note = null) : IIntegrationEventHandler<InvoiceRaisedV2>
        {
            public Task HandleAsync(InvoiceRaisedV2 contract, IntegrationEventMessage message, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }

        public static class Outer
        {
            public sealed class Audit : IIntegrationEventHandler<OrderPlacedV1>
            {
                public Task HandleAsync(OrderPlacedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
        }
        """;

    private static GeneratorRunOutcome Run(string source, bool withContracts = true)
    {
        var host = GeneratorTestHost.Create(source).WithEntityFrameworkRuntime().WithModule("Sales");
        if (withContracts)
        {
            host = host.WithReferencedAssembly(Contracts, "Billing.Contracts");
        }

        return host.RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());
    }

    [Fact]
    public void The_outbox_registration_names_every_domain_event_and_every_outbound_class()
    {
        var result = Run(Module);

        result.ShouldCompile();
        result.ShouldContain(Hint, "public static global::DDDToolkit.EntityFramework.Options.OutboxOptions AddSalesIntegrationEvents(this global::DDDToolkit.EntityFramework.Options.OutboxOptions outbox)");
        result.ShouldContain(Hint, "outbox.RegisterEvent<global::Sales.OrderPlaced>(\"sales.order-placed\", 1);", "[DomainEventName] is the stored name");
        result.ShouldContain(Hint, "outbox.RegisterEvent<global::Sales.OrderShipped>(\"order-shipped\", 1);", "without it the convention names it, as at run time");
        result.ShouldContain(
            Hint,
            "outbox.PublishWith<global::Sales.OrderPlaced, global::Sales.OrderPlacedV1>(\"sales.order-placed\", 1, static services => new global::Sales.PublishOrderPlaced());");
    }

    [Fact]
    public void The_handler_registration_writes_out_the_consumer_the_contract_and_the_constructor()
    {
        var result = Run(Module);

        result.ShouldCompile();
        result.ShouldContain(Hint, "contracts.Register<global::Billing.Contracts.InvoiceRaisedV2>(\"billing.invoice-raised\", 2);", "a contract from another assembly is read through metadata");
        result.ShouldContain(
            Hint,
            "module.Handle<global::Billing.Contracts.InvoiceRaisedV2, global::Sales.RecordInvoice>(\"billing.invoice-raised\", \"sales.invoices\", " +
            $"static services => new global::Sales.RecordInvoice({Services}.GetRequiredService<global::Sales.Clock>(services), services, {Services}.GetService<string>(services)));");
        result.ShouldContain(
            Hint,
            "module.Handle<global::Sales.OrderPlacedV1, global::Sales.Outer.Audit>(\"sales.order-placed\", \"Sales.Outer+Audit\", static services => new global::Sales.Outer.Audit());",
            "with no [IntegrationEventConsumer] the consumer name is the CLR full name, nested types and all");
    }

    [Fact]
    public void Running_the_generated_outbox_registration_fills_the_registries_without_reading_an_attribute()
    {
        var result = Run(Module);
        result.ShouldCompile();

        var emitted = result.Emit();
        var extensions = emitted.Assembly.GetType(GeneratorTestHost.DefaultAssemblyName + ".IntegrationEvents.IntegrationEventExtensions")!;
        var register = extensions.GetMethods().Single(method => method.Name == "AddSalesIntegrationEvents" && method.GetParameters()[0].ParameterType == typeof(OutboxOptions));

        var outbox = new OutboxOptions();
        register.Invoke(null, [outbox]);

        var placed = emitted.Assembly.GetType("Sales.OrderPlaced")!;
        outbox.EventTypes.TryDescribe(placed, out var name, out var version).Should().BeTrue();
        name.Should().Be("sales.order-placed");
        version.Should().Be(1);

        outbox.IntegrationEvents.TryDescribeContract(placed, out var contract, out var contractVersion).Should().BeTrue();
        contract.Should().Be("sales.order-placed");
        contractVersion.Should().Be(1);
    }

    [Fact]
    public void A_class_it_cannot_construct_is_reported_and_left_out()
    {
        var result = Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DDDToolkit.BaseTypes;
            using DDDToolkit.EntityFramework.Integration;

            namespace Sales;

            public sealed record OrderPlacedV1(string OrderId);

            public sealed class Undecided : IIntegrationEventHandler<OrderPlacedV1>
            {
                public Undecided(string first) { }

                public Undecided(int second) { }

                public Task HandleAsync(OrderPlacedV1 contract, IntegrationEventMessage message, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """,
            withContracts: false);

        result.ShouldCompile();
        result.ShouldHaveDiagnostic("DDD00033", "Undecided").GetMessage().Should().Contain("more than one constructor");
        result.ShouldNotContain(Hint, "Undecided");
    }

    [Fact]
    public void Nothing_is_generated_without_DDDToolkit_EntityFramework()
    {
        var result = GeneratorTestHost.Create(Module.Replace("using DDDToolkit.EntityFramework.Integration;", string.Empty))
            .WithEntityFramework()
            .WithModule("Sales")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains(Hint));
    }
}
