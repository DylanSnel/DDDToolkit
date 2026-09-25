using System.Reflection;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Options;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The names events get when nobody names them, and the <c>{Module}EventNames</c> constants the core generator
/// writes for them. The last tests compile a module and read the same types back by reflection, because the
/// point of one convention compiled into both the generators and the runtime is that they cannot disagree.
/// </summary>
public class EventNamesGenerationTests
{
    private const string Hint = "EventNames";

    private const string Sales =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.BaseTypes;

        [assembly: Module("Sales")]

        namespace Sales;

        public sealed record OrderPlaced(string OrderId) : DomainEvent;

        public sealed record OrderPlacedV2(string OrderId, decimal Total) : DomainEvent;

        [DomainEventName("sales.order-dispatched")]
        public sealed record OrderShipped(string OrderId) : DomainEvent;

        public sealed record HTTPCallbackReceived(string Url) : DomainEvent;
        """;

    private const string SalesContracts =
        """
        using DDDToolkit.Abstractions.Attributes;

        [assembly: Module("Sales")]

        namespace Sales.Contracts;

        [IntegrationEvent]
        public sealed record OrderPlacedV1(string OrderId);

        [IntegrationEvent]
        public sealed record OrderPlacedV2(string OrderId, decimal Total);

        [IntegrationEvent(Version = 3)]
        public sealed record InvoiceRaised(string InvoiceId);
        """;

    [Fact]
    public void Every_name_gets_a_constant_named_after_it_without_the_module()
    {
        var result = GeneratorTestHost.Create(Sales).RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveDiagnostic("DDD00036");
        result.ShouldContain(Hint, "namespace DDDToolkit.Sample;");
        result.ShouldContain(Hint, "public static class SalesEventNames", "named after [assembly: Module]");
        result.ShouldContain(Hint, "public const string OrderPlaced = \"sales.order-placed\";", "one constant for both versions of one event");
        result.ShouldContain(Hint, "public const string OrderDispatched = \"sales.order-dispatched\";", "a pinned name is named after the name, not the class");
        result.ShouldContain(Hint, "public const string HttpCallbackReceived = \"sales.http-callback-received\";");
        result.ShouldNotContain(Hint, "OrderShipped =", "the constant follows the name, so a renamed class keeps its constant");
        result.ShouldContain(
            Hint,
            "/// <summary><c>sales.order-placed</c>: <see cref=\"global::Sales.OrderPlaced\"/> (version 1), <see cref=\"global::Sales.OrderPlacedV2\"/> (version 2).</summary>");
    }

    [Fact]
    public void A_contracts_assembly_publishes_its_constants_to_other_modules()
    {
        var result = GeneratorTestHost.Create(SalesContracts).WithAssemblyName("Sales.Contracts").RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "namespace Sales.Contracts;");
        result.ShouldContain(Hint, "[global::DDDToolkit.Abstractions.Attributes.ModuleContract]", "every name it holds is a published contract's");
        result.ShouldContain(Hint, "public const string OrderPlaced = \"sales.order-placed\";");
        result.ShouldContain(Hint, "public const string InvoiceRaised = \"sales.invoice-raised\";");
    }

    [Fact]
    public void A_domain_assembly_keeps_its_constants_to_itself()
        => GeneratorTestHost.Create(Sales).RunCore().ShouldNotContain(Hint, "ModuleContract", "domain events are the module's own business");

    [Fact]
    public void Outside_a_module_the_name_is_the_class_name_alone_and_the_class_is_named_after_DDD_Module()
    {
        var result = GeneratorTestHost.Create(
                """
                using DDDToolkit.BaseTypes;

                namespace Shop;

                public sealed record OrderPlaced(string OrderId) : DomainEvent;
                """)
            .WithModule("Shop")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "public static class ShopEventNames");
        result.ShouldContain(Hint, "public const string OrderPlaced = \"order-placed\";");
    }

    [Fact]
    public void Nothing_is_generated_for_an_assembly_without_events()
    {
        var result = GeneratorTestHost.Create(
                """
                using DDDToolkit.Abstractions.Attributes;

                namespace Shop;

                [ValueObject]
                public partial record Money(decimal Amount);
                """)
            .RunCore();

        result.ShouldCompile();
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains(Hint));
    }

    [Fact]
    public void Abstract_and_generic_events_get_no_constant()
    {
        var result = GeneratorTestHost.Create(
                """
                using DDDToolkit.BaseTypes;

                namespace Shop;

                public abstract record ShopEvent : DomainEvent;

                public sealed record Wrapped<T>(T Value) : DomainEvent;

                public sealed record OrderPlaced(string OrderId) : ShopEvent;
                """)
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "OrderPlaced = \"order-placed\"");
        result.ShouldNotContain(Hint, "ShopEvent =");
        result.ShouldNotContain(Hint, "Wrapped =");
    }

    [Fact]
    public void The_generated_registration_and_the_runtime_name_every_event_alike()
    {
        var result = GeneratorTestHost.Create(Sales)
            .WithEntityFrameworkRuntime()
            .WithModule("Sales")
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());
        result.ShouldCompile();

        var emitted = result.Emit();

        // What the generated Add{Module}IntegrationEvents registers, read back from the outbox it filled.
        var extensions = emitted.Assembly.GetType(GeneratorTestHost.DefaultAssemblyName + ".IntegrationEvents.IntegrationEventExtensions")!;
        var register = extensions.GetMethods().Single(method => method.Name == "AddSalesIntegrationEvents" && method.GetParameters()[0].ParameterType == typeof(OutboxOptions));
        var outbox = new OutboxOptions();
        register.Invoke(null, [outbox]);

        foreach (var name in new[] { "Sales.OrderPlaced", "Sales.OrderPlacedV2", "Sales.OrderShipped", "Sales.HTTPCallbackReceived" })
        {
            var type = emitted.Type(name);
            outbox.EventTypes.TryDescribe(type, out var generatedName, out var generatedVersion).Should().BeTrue();

            generatedName.Should().Be(DomainEventName.Of(type), $"the generator and the runtime name {name} alike");
            generatedVersion.Should().Be(IntegrationEventContract.VersionOf(type), $"the generator and the runtime version {name} alike");
        }

        outbox.EventTypes.Resolve("sales.order-placed").Should().Be(emitted.Type("Sales.OrderPlacedV2"), "the newest version is the one new events are written as");

        // And what the generated constants say, against the same types.
        var constants = emitted.Type("DDDToolkit.Sample.SalesEventNames");
        Constant(constants, "OrderPlaced").Should().Be(DomainEventName.Of(emitted.Type("Sales.OrderPlaced")));
        Constant(constants, "OrderDispatched").Should().Be(DomainEventName.Of(emitted.Type("Sales.OrderShipped")));
    }

    [Fact]
    public void A_contract_from_another_module_is_named_after_that_module()
    {
        const string Handler =
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.BaseTypes;
            using DDDToolkit.EntityFramework.Integration;
            using Sales.Contracts;

            [assembly: Module("Billing")]

            namespace Billing;

            public sealed class BillOrder : IIntegrationEventHandler<OrderPlacedV2>, IIntegrationEventHandler<InvoiceRaised>
            {
                public Task HandleAsync(OrderPlacedV2 contract, IntegrationEventMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

                public Task HandleAsync(InvoiceRaised contract, IntegrationEventMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """;

        var result = GeneratorTestHost.Create(Handler)
            .WithEntityFrameworkRuntime()
            .WithModule("Billing")
            .WithReferencedAssembly(SalesContracts, "Sales.Contracts")
            .RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        result.ShouldContain("IntegrationEventExtensions", "contracts.Register<global::Sales.Contracts.OrderPlacedV2>(\"sales.order-placed\", 2);", "the module is read off the contract's own assembly");
        result.ShouldContain("IntegrationEventExtensions", "contracts.Register<global::Sales.Contracts.InvoiceRaised>(\"sales.invoice-raised\", 3);", "no suffix leaves the version to [IntegrationEvent(Version = 3)]");
    }

    private static string? Constant(Type type, string name)
        => (string?)type.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue();
}
