namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// <c>Add{Module}Handlers()</c>, which the core generator writes for the toolkit's mediator: one
/// <c>AddNotificationHandler&lt;TNotification, THandler&gt;</c> per handler and notification type, closed when the
/// project compiles.
/// </summary>
public class HandlersGenerationTests
{
    private const string Hint = "HandlerExtensions";

    private const string Shop =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.BaseTypes;
        using DDDToolkit.Mediator;
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        [assembly: Module("Shop")]

        namespace Shop;

        public sealed record OrderPlaced(string OrderId) : DomainEvent;

        public sealed record OrderShipped(string OrderId) : DomainEvent;

        internal sealed class ReserveStock : INotificationHandler<OrderPlaced>
        {
            public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
        }

        public sealed class TellTheCustomer : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped>
        {
            public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;

            public ValueTask Handle(OrderShipped notification, CancellationToken cancellationToken) => default;
        }
        """;

    [Fact]
    public void Every_handler_is_registered_once_for_each_notification_type_it_handles()
    {
        var result = GeneratorTestHost.Create(Shop).WithDependencyInjection().RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "namespace DDDToolkit.Sample;", "the assembly's own namespace, so the module calls it without a using");
        result.ShouldContain(Hint, "public static class HandlerExtensions");
        result.ShouldContain(Hint, "AddShopHandlers(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", "named after [assembly: Module]");
        result.ShouldContain(Hint, "AddNotificationHandler<global::Shop.OrderPlaced, global::Shop.ReserveStock>(services);", "an internal handler can be named from its own project");
        result.ShouldContain(Hint, "AddNotificationHandler<global::Shop.OrderPlaced, global::Shop.TellTheCustomer>(services);");
        result.ShouldContain(Hint, "AddNotificationHandler<global::Shop.OrderShipped, global::Shop.TellTheCustomer>(services);");
    }

    [Fact]
    public void A_closed_generic_notification_of_a_package_is_registered_as_its_handler_names_it()
    {
        const string package =
            """
            using DDDToolkit.BaseTypes;

            namespace Package;

            public sealed record MemberJoined<TMemberId>(TMemberId MemberId) : DomainEvent;
            """;

        const string module =
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Mediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Package;

            [assembly: Module("Clubs")]

            namespace Clubs;

            public readonly record struct PlayerId(Guid Value);

            internal sealed class Welcome : INotificationHandler<MemberJoined<PlayerId>>
            {
                public ValueTask Handle(MemberJoined<PlayerId> notification, CancellationToken cancellationToken) => default;
            }
            """;

        var result = GeneratorTestHost.Create(module)
            .WithDependencyInjection()
            .WithReferencedAssembly(package, "Package")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "AddNotificationHandler<global::Package.MemberJoined<global::Clubs.PlayerId>, global::Clubs.Welcome>(services);");
    }

    [Fact]
    public void A_handler_the_registration_cannot_name_is_left_out()
    {
        const string source =
            """
            using DDDToolkit.BaseTypes;
            using DDDToolkit.Mediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Shop;

            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            public sealed class Outer
            {
                private sealed class Hidden : INotificationHandler<OrderPlaced>
                {
                    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
                }
            }

            public sealed class Generic<T>
            {
                public sealed class Inside : INotificationHandler<OrderPlaced>
                {
                    public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
                }
            }

            public sealed class Open<TNotification> : INotificationHandler<TNotification>
                where TNotification : INotification
            {
                public ValueTask Handle(TNotification notification, CancellationToken cancellationToken) => default;
            }

            public abstract class Base : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
            }

            file sealed class Local : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
            }
            """;

        var result = GeneratorTestHost.Create(source).WithDependencyInjection().RunCore();

        result.ShouldCompile();
        result.ShouldNotHaveGeneratedFor(Hint);
    }

    [Fact]
    public void A_handler_derived_from_an_abstract_handler_is_registered_for_what_the_base_handles()
    {
        const string source =
            """
            using DDDToolkit.BaseTypes;
            using DDDToolkit.Mediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Shop;

            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            public abstract class Base : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
            }

            public sealed partial class Concrete : Base;

            public sealed partial class Concrete : System.IDisposable
            {
                public void Dispose()
                {
                }
            }
            """;

        var result = GeneratorTestHost.Create(source).WithDependencyInjection().RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "AddNotificationHandler<global::Shop.OrderPlaced, global::Shop.Concrete>(services);");
        result.Source(Hint).Split("AddNotificationHandler<").Length.Should().Be(2, "a partial class declared twice is registered once");
    }

    [Fact]
    public void The_registration_calls_the_one_of_the_modules_project_it_references()
    {
        const string application =
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.BaseTypes;
            using DDDToolkit.Mediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            [assembly: Module("Shop")]

            namespace Shop.Application;

            public sealed record OrderPlaced(string OrderId) : DomainEvent;

            internal sealed class ReserveStock : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
            }
            """;

        const string infrastructure =
            """
            using DDDToolkit.Abstractions.Attributes;
            using DDDToolkit.Mediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Shop.Application;

            [assembly: Module("Shop")]

            namespace Shop.Infrastructure;

            internal sealed class WriteToTheLedger : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken cancellationToken) => default;
            }
            """;

        var result = GeneratorTestHost.Create(infrastructure)
            .WithAssemblyName("Shop.Infrastructure")
            .WithDependencyInjection()
            .WithReferencedProject("Shop.Application", project => project.WithSource(application, "Application.cs"))
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "global::Shop.Application.HandlerExtensions.AddShopHandlers(services);", "one call registers the module");
        result.ShouldContain(Hint, "AddNotificationHandler<global::Shop.Application.OrderPlaced, global::Shop.Infrastructure.WriteToTheLedger>(services);");
        result.ShouldNotContain(Hint, "ReserveStock", "the referenced project registers its own handlers");
    }

    [Fact]
    public void A_project_without_the_core_writes_nothing()
    {
        const string source =
            """
            namespace Shop;

            public sealed class Plain;
            """;

        var result = GeneratorTestHost.Create(source).WithoutTheToolkitAssemblies().RunCore();

        result.ShouldNotHaveGeneratedFor(Hint);
    }
}
