namespace DTS.DDD.Analyzers.Tests.Generation;

/// <summary>
/// <c>Add{Module}Handlers()</c>, which the core generator writes for the toolkit's mediator: one
/// <c>AddNotificationHandler&lt;TNotification, THandler&gt;</c> per handler and notification type, and one registration
/// of its kind per request handler, such as <c>AddCommandHandler&lt;TCommand, TResponse, THandler&gt;</c>, closed when
/// the project compiles. A second handler of one request is DDD00110.
/// </summary>
public class HandlersGenerationTests
{
    private const string Hint = "HandlerExtensions";

    private const string Shop =
        """
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.BaseTypes;
        using DTS.DDD.Mediator;
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
        result.ShouldContain(Hint, "namespace DTS.DDD.Sample;", "the assembly's own namespace, so the module calls it without a using");
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
            using DTS.DDD.BaseTypes;

            namespace Package;

            public sealed record MemberJoined<TMemberId>(TMemberId MemberId) : DomainEvent;
            """;

        const string module =
            """
            using DTS.DDD.Abstractions.Attributes;
            using DTS.DDD.Mediator;
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
            using DTS.DDD.BaseTypes;
            using DTS.DDD.Mediator;
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
            using DTS.DDD.BaseTypes;
            using DTS.DDD.Mediator;
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
            using DTS.DDD.Abstractions.Attributes;
            using DTS.DDD.BaseTypes;
            using DTS.DDD.Mediator;
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
            using DTS.DDD.Abstractions.Attributes;
            using DTS.DDD.Mediator;
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

    /// <summary>A module with a handler of each kind of message that is answered once or with a stream.</summary>
    private const string Billing =
        """
        using DTS.DDD.Abstractions.Attributes;
        using DTS.DDD.Mediator;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        [assembly: Module("Billing")]

        namespace Billing;

        public sealed record CloseInvoice(int Invoice) : ICommand;

        public sealed record InvoiceTotal(int Invoice) : IQuery<decimal>;

        public sealed record Ping : IRequest<string>;

        public sealed record InvoiceLines(int Invoice) : IStreamQuery<string>;

        public sealed record Reissue(int Invoice) : IStreamCommand<int>;

        public sealed record Watch : IStreamRequest<int>;

        internal sealed class CloseInvoiceHandler : ICommandHandler<CloseInvoice>
        {
            public ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => Unit.ValueTask;
        }

        internal sealed class InvoiceTotalHandler : IQueryHandler<InvoiceTotal, decimal>
        {
            public ValueTask<decimal> Handle(InvoiceTotal query, CancellationToken cancellationToken) => new(12m);
        }

        internal sealed class PingHandler : IRequestHandler<Ping, string>
        {
            public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken) => new("pong");
        }

        internal sealed class InvoiceLinesHandler : IStreamQueryHandler<InvoiceLines, string>
        {
            public async IAsyncEnumerable<string> Handle(InvoiceLines query, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return "bread";
            }
        }

        internal sealed class ReissueHandler : IStreamCommandHandler<Reissue, int>
        {
            public async IAsyncEnumerable<int> Handle(Reissue command, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return command.Invoice;
            }
        }

        internal sealed class WatchHandler : IStreamRequestHandler<Watch, int>
        {
            public async IAsyncEnumerable<int> Handle(Watch request, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return 1;
            }
        }

        """;

    [Fact]
    public void Each_request_handler_gets_the_registration_of_its_kind_with_its_response()
    {
        var result = GeneratorTestHost.Create(Billing).WithDependencyInjection().RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "AddCommandHandler<global::Billing.CloseInvoice, global::DTS.DDD.Mediator.Unit, global::Billing.CloseInvoiceHandler>(services);", "a command answered with nothing answers Unit");
        result.ShouldContain(Hint, "AddQueryHandler<global::Billing.InvoiceTotal, decimal, global::Billing.InvoiceTotalHandler>(services);");
        result.ShouldContain(Hint, "AddRequestHandler<global::Billing.Ping, string, global::Billing.PingHandler>(services);");
        result.ShouldContain(Hint, "AddStreamQueryHandler<global::Billing.InvoiceLines, string, global::Billing.InvoiceLinesHandler>(services);");
        result.ShouldContain(Hint, "AddStreamCommandHandler<global::Billing.Reissue, int, global::Billing.ReissueHandler>(services);");
        result.ShouldContain(Hint, "AddStreamRequestHandler<global::Billing.Watch, int, global::Billing.WatchHandler>(services);");
        result.ShouldNotContain(Hint, "AddCommandHandler<global::Billing.CloseInvoice, global::Billing.CloseInvoiceHandler>", "the handler interface without a response is registered through the one it derives from");
    }

    [Fact]
    public void A_second_handler_of_one_command_reports_DDD00110_at_the_second_and_the_registration_keeps_the_first()
    {
        var result = GeneratorTestHost.Create(Billing, "Billing.cs")
            .WithSource(
                """
                using DTS.DDD.Mediator;
                using System.Threading;
                using System.Threading.Tasks;

                namespace Billing;

                internal sealed class CloseInvoiceAgain : ICommandHandler<CloseInvoice>
                {
                    public ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => Unit.ValueTask;
                }
                """,
                "Second.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00110");
        result.ShouldHaveDiagnostic("DDD00110", at: "CloseInvoiceAgain").GetMessage().Should()
            .Be("'Billing.CloseInvoiceAgain' is a second handler of 'Billing.CloseInvoice'. 'Billing.CloseInvoiceHandler' handles it already, and a message has one handler.");
        result.ShouldContain(Hint, "AddCommandHandler<global::Billing.CloseInvoice, global::DTS.DDD.Mediator.Unit, global::Billing.CloseInvoiceHandler>(services);");
        result.ShouldNotContain(Hint, "CloseInvoiceAgain", "the registration keeps the first handler, by file and position");
    }

    [Fact]
    public void Two_handlers_of_one_notification_are_no_second_handler()
    {
        var result = GeneratorTestHost.Create(Shop).WithDependencyInjection().RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_partial_handler_declared_in_two_files_is_one_handler()
    {
        var result = GeneratorTestHost.Create(
                """
                using DTS.DDD.Mediator;
                using System.Threading;
                using System.Threading.Tasks;

                namespace Billing;

                public sealed record CloseInvoice(int Invoice) : ICommand;

                internal sealed partial class CloseInvoiceHandler : ICommandHandler<CloseInvoice>
                {
                    public ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => Unit.ValueTask;
                }
                """,
                "Handler.cs")
            .WithSource(
                """
                namespace Billing;

                internal sealed partial class CloseInvoiceHandler : System.IDisposable
                {
                    public void Dispose()
                    {
                    }
                }
                """,
                "Handler.Dispose.cs")
            .WithDependencyInjection()
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "AddCommandHandler<global::Billing.CloseInvoice, global::DTS.DDD.Mediator.Unit, global::Billing.CloseInvoiceHandler>(services);");
    }
}
