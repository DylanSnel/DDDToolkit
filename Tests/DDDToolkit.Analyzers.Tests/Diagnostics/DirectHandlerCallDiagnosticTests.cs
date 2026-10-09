using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00061: a request that declares what it requires of its caller, handed to its handler directly. The access
/// behavior asks the module's checks in the pipeline a request passes when it is sent; a handler called past it runs
/// with nothing having asked, and nothing at run time notices. So the build says it at the call, through a handler
/// class or the interface it was injected as, and leaves alone what is no call (constructing or injecting a handler),
/// what is no handler (a pipeline behavior), what is the library's own dispatch, and a test project.
/// </summary>
public class DirectHandlerCallDiagnosticTests
{
    /// <summary>A module of the shop: its request interface, a command, a query, a stream query, and their handlers.</summary>
    private const string Billing =
        """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using Mediator;

        namespace Shop.Billing;

        public sealed record MayClose : AccessRequirement;

        [AccessRequests]
        public interface IBillingRequest : IRequireAccess;

        public sealed record CloseInvoice(int Invoice) : ICommand, IBillingRequest
        {
            AccessRequirement IRequireAccess.RequiredAccess => new MayClose();
        }

        public sealed record InvoiceTotal(int Invoice) : IQuery<decimal>, IBillingRequest
        {
            AccessRequirement IRequireAccess.RequiredAccess => new MayClose();
        }

        public sealed record InvoiceLines(int Invoice) : IStreamQuery<string>, IBillingRequest
        {
            AccessRequirement IRequireAccess.RequiredAccess => new MayClose();
        }

        public sealed record Ping : ICommand;

        public class CloseInvoiceHandler : ICommandHandler<CloseInvoice>
        {
            public virtual ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => new(Unit.Value);
        }

        public sealed class InvoiceTotalHandler : IQueryHandler<InvoiceTotal, decimal>
        {
            public ValueTask<decimal> Handle(InvoiceTotal query, CancellationToken cancellationToken) => new(12m);
        }

        public sealed class InvoiceLinesHandler : IStreamQueryHandler<InvoiceLines, string>
        {
            public async IAsyncEnumerable<string> Handle(InvoiceLines query, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return "bread";
            }
        }

        public sealed class PingHandler : ICommandHandler<Ping>
        {
            public ValueTask<Unit> Handle(Ping command, CancellationToken cancellationToken) => new(Unit.Value);
        }


        """;

    private static GeneratorRunOutcome Run(string code, Func<GeneratorTestHost, GeneratorTestHost>? configure = null)
    {
        var host = GeneratorTestHost.Create(Billing + code).WithMediator().WithAnalyzers(GeneratorTestHost.CoreAnalyzers());
        return (configure?.Invoke(host) ?? host).RunCore();
    }

    [Fact]
    public void A_command_handed_to_its_handler_class_is_reported_at_Handle_naming_the_request_and_its_interface()
    {
        var result = Run(
            """
            public sealed class Closing(CloseInvoiceHandler handler)
            {
                public async Task CloseAsync(int invoice, CancellationToken cancellationToken)
                    => await handler.Handle(new CloseInvoice(invoice), cancellationToken);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061");
        var reported = result.ShouldHaveDiagnostic("DDD00061", at: "Handle");
        reported.Severity.Should().Be(DiagnosticSeverity.Warning);
        reported.GetMessage().Should()
            .StartWith("'CloseInvoice' is handed to its handler directly, past the pipeline, so nothing asks what it requires")
            .And.Contain("it implements 'IBillingRequest', which is marked [AccessRequests]")
            .And.Contain("Send it with ISender instead");
        reported.Properties["SendWith"].Should().Be("Send");
        result.ShouldNotCrash();
    }

    [Fact]
    public void A_call_through_the_handler_interface_it_was_injected_as_is_reported_as_well()
    {
        var result = Run(
            """
            public sealed class Closing(ICommandHandler<CloseInvoice> handler, IQueryHandler<InvoiceTotal, decimal> totals)
            {
                public async Task<decimal> CloseAsync(int invoice, CancellationToken cancellationToken)
                {
                    await handler.Handle(new CloseInvoice(invoice), cancellationToken);
                    return await totals.Handle(new InvoiceTotal(invoice), cancellationToken);
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061", "DDD00061");
        result.ReportedDiagnostics.Select(diagnostic => diagnostic.GetMessage()).Should().Contain(message => message.StartsWith("'InvoiceTotal' is handed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stream_query_handed_to_its_handler_is_reported_and_is_sent_with_CreateStream()
    {
        var result = Run(
            """
            public sealed class Export(InvoiceLinesHandler handler)
            {
                public IAsyncEnumerable<string> Lines(int invoice, CancellationToken cancellationToken) => handler.Handle(new InvoiceLines(invoice), cancellationToken);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061");
        result.ShouldHaveDiagnostic("DDD00061", at: "Handle").Properties["SendWith"].Should().Be("CreateStream");
    }

    [Fact]
    public void Handle_made_into_a_delegate_is_reported_and_so_is_a_call_on_a_handler_that_may_be_null()
    {
        var result = Run(
            """
            public sealed class Later(CloseInvoiceHandler handler, CloseInvoiceHandler? maybe)
            {
                public System.Func<CloseInvoice, CancellationToken, ValueTask<Unit>> Closing => handler.Handle;

                public async Task CloseAsync(CancellationToken cancellationToken)
                {
                    var closing = maybe?.Handle(new CloseInvoice(7), cancellationToken);
                    if (closing is { } running)
                    {
                        await running;
                    }
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061", "DDD00061");
    }

    [Fact]
    public void A_handler_of_a_type_parameter_constrained_to_the_interface_is_reported()
    {
        var result = Run(
            """
            public sealed class Relay<TRequest>(ICommandHandler<TRequest> handler)
                where TRequest : ICommand, IBillingRequest
            {
                public ValueTask<Unit> RelayAsync(TRequest request, CancellationToken cancellationToken) => handler.Handle(request, cancellationToken);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061");
        result.ShouldHaveDiagnostic("DDD00061", at: "Handle").GetMessage().Should().StartWith("'TRequest' is handed").And.Contain("'IBillingRequest'");
    }

    [Fact]
    public void Sending_constructing_injecting_and_a_request_of_no_module_are_left_alone()
    {
        var result = Run(
            """
            public sealed class Closing(ISender sender, CloseInvoiceHandler injected, PingHandler pings)
            {
                public CloseInvoiceHandler Made { get; } = new();

                public CloseInvoiceHandler Injected => injected;

                public async Task CloseAsync(int invoice, CancellationToken cancellationToken)
                {
                    await sender.Send(new CloseInvoice(invoice), cancellationToken);
                    await foreach (var line in sender.CreateStream(new InvoiceLines(invoice), cancellationToken))
                    {
                    }

                    await pings.Handle(new Ping(), cancellationToken);   // a request of no module: nothing to ask
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_behavior_is_no_handler_and_neither_is_another_method_called_Handle()
    {
        var result = Run(
            """
            public sealed class Probe
            {
                public ValueTask<Unit> Handle(CloseInvoice command) => new(Unit.Value);

                public static async Task RunAsync(BillingAccessBehavior<CloseInvoice, Unit> behavior, CancellationToken cancellationToken)
                {
                    await behavior.Handle(new CloseInvoice(7), (message, token) => new ValueTask<Unit>(Unit.Value), cancellationToken);
                    await new Probe().Handle(new CloseInvoice(7));
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_handler_that_overrides_Handle_hands_on_the_request_it_was_sent_to_base_Handle()
    {
        var result = Run(
            """
            public sealed class LoggedCloseInvoiceHandler : CloseInvoiceHandler
            {
                public override async ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken)
                    => await base.Handle(command, cancellationToken);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_handler_derived_from_another_that_overrides_Handle_is_reported_where_it_is_called()
    {
        // The derived class implements the interface through its base's Handle, which its own overrides.
        var result = Run(
            """
            public sealed class LoggedCloseInvoiceHandler : CloseInvoiceHandler
            {
                public override async ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken)
                    => await base.Handle(command, cancellationToken);
            }

            public sealed class Closing(LoggedCloseInvoiceHandler handler)
            {
                public async Task CloseAsync(CancellationToken cancellationToken) => await handler.Handle(new CloseInvoice(7), cancellationToken);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061");
        result.ShouldHaveDiagnostic("DDD00061", at: "Handle").GetMessage().Should().StartWith("'CloseInvoice' is handed");
    }

    [Fact]
    public void A_handler_that_wraps_another_and_hands_it_the_message_it_was_given_is_left_alone()
    {
        // A decorator: the library dispatches the message to it after the pipeline, as to any handler, and sending
        // it again would come back round to the decorator. A message of its own making is another matter.
        var result = Run(
            """
            public sealed class LoggingCloseInvoice(ICommandHandler<CloseInvoice> inner) : ICommandHandler<CloseInvoice>
            {
                public ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => inner.Handle(command, cancellationToken);
            }

            public sealed class ReopeningCloseInvoice(ICommandHandler<CloseInvoice> inner) : ICommandHandler<CloseInvoice>
            {
                public ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => inner.Handle(command with { Invoice = 8 }, cancellationToken);
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00061");
        var reported = result.ReportedDiagnostics.Should().ContainSingle().Subject;
        reported.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).Lines[reported.Location.GetLineSpan().StartLinePosition.Line].ToString()
            .Should().Contain("with { Invoice = 8 }", "the decorator that hands on the message it was given is not the one reported");
    }

    [Theory]
    [InlineData("IsTestProject", "true")]
    [InlineData("IsTestProject", "True")]
    [InlineData("IsTestingPlatformApplication", "true")]
    public void A_test_project_calls_a_handler_on_purpose_and_hears_nothing(string property, string value)
    {
        var result = Run(
            """
            public sealed class CloseInvoiceHandlerTests
            {
                public async Task Closes_an_invoice() => await new CloseInvoiceHandler().Handle(new CloseInvoice(7), CancellationToken.None);
            }
            """,
            host => host.WithBuildProperty(property, value));

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void A_project_that_says_it_is_no_test_project_is_reported()
    {
        var result = Run(
            """
            public sealed class Closing
            {
                public async Task CloseAsync() => await new CloseInvoiceHandler().Handle(new CloseInvoice(7), CancellationToken.None);
            }
            """,
            host => host.WithBuildProperty("IsTestProject", "false").WithBuildProperty("IsTestingPlatformApplication", ""));

        result.ShouldHaveExactlyDiagnostics("DDD00061");
    }

    [Fact]
    public void A_call_that_is_meant_is_suppressed_where_it_is_made()
    {
        var result = Run(
            """
            public sealed class Replay
            {
                public async Task ReplayAsync(CloseInvoice checkedAlready)
                {
            #pragma warning disable DDD00061 // replayed from the outbox, after the access behavior let it through once
                    await new CloseInvoiceHandler().Handle(checkedAlready, CancellationToken.None);
            #pragma warning restore DDD00061
                }
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void Without_the_library_nothing_is_reported()
    {
        var result = GeneratorTestHost.Create(
                """
                using System.Threading;
                using System.Threading.Tasks;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Access;

                namespace Shop.Billing;

                public sealed record MayClose : AccessRequirement;

                [AccessRequests]
                public interface IBillingRequest : IRequireAccess;

                public sealed record CloseInvoice(int Invoice) : IBillingRequest
                {
                    AccessRequirement IRequireAccess.RequiredAccess => new MayClose();
                }

                public interface ICommandHandler<TCommand>
                {
                    Task Handle(TCommand command, CancellationToken cancellationToken);
                }

                public sealed class Closing(ICommandHandler<CloseInvoice> handler)
                {
                    public Task CloseAsync() => handler.Handle(new CloseInvoice(7), CancellationToken.None);
                }
                """)
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }
}
