using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The pipeline behavior the core generator writes for an interface marked <c>[AccessRequests]</c>: only in a
/// project that uses the Mediator library, named after the interface and declared beside it, implemented the way
/// the referenced version of the library declares its behavior, and registered with the set of checks it asks.
/// The library sends a message that is answered with a stream through a pipeline of its own, so the interface
/// gets a second behavior for that one, or a stream query of the module would reach its handler unasked.
/// The last tests compile a module and run what was written, because a behavior that compiles and never asks
/// the checks would pass every test that only reads its text.
/// </summary>
public class AccessBehaviorGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    /// <summary>A module's one line: the interface its requests implement.</summary>
    public const string Billing =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;

        namespace Shop.Billing;

        [AccessRequests]
        public interface IBillingRequest : IRequireAccess;
        """;

    /// <summary>A request of the module, the check that decides its requirement, and code that sends it through the behavior.</summary>
    private const string BillingInUse =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Access;
        using DDDToolkit.Exceptions;
        using Mediator;

        namespace Shop.Billing;

        public sealed record MayClose : AccessRequirement;

        public sealed record CloseInvoice(int Invoice, AccessRequirement Requires) : ICommand, IBillingRequest
        {
            AccessRequirement IRequireAccess.RequiredAccess => Requires;
        }

        public sealed record Ping : ICommand;

        public sealed class BillingCheck(List<string> steps) : IAccessCheck
        {
            public bool Decides(AccessRequirement requirement) => requirement is MayClose;

            public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
            {
                steps.Add("check " + ((CloseInvoice)request).Invoice);
                return ((CloseInvoice)request).Invoice < 0
                    ? throw new RefusalException("billing.not-permitted", RefusalKind.NotPermitted, "Only its owner closes an invoice.")
                    : ValueTask.CompletedTask;
            }
        }

        public static class Probe
        {
            /// <summary>Sends a command through the behavior over the module's check, and says what ran, in order.</summary>
            public static async Task<string> RunThroughAsync(int invoice, AccessRequirement requires)
            {
                var steps = new List<string>();
                var behavior = new BillingAccessBehavior<CloseInvoice, Unit>(new AccessChecks<IBillingRequest>(new IAccessCheck[] { new BillingCheck(steps) }));
                try
                {
                    await behavior.Handle(
                        new CloseInvoice(invoice, requires),
                        (message, cancellationToken) =>
                        {
                            steps.Add("handler " + message.Invoice);
                            return new ValueTask<Unit>(Unit.Value);
                        },
                        CancellationToken.None);
                }
                catch (Exception refused)
                {
                    steps.Add(refused.GetType().Name);
                }

                return string.Join(", ", steps);
            }
        }
        """;

    /// <summary>A query of the module that is answered with a stream, its check, and code that asks for the stream through the behavior.</summary>
    private const string BillingStreamInUse =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Access;
        using DDDToolkit.Exceptions;
        using Mediator;

        namespace Shop.Billing;

        public sealed record MayExport : AccessRequirement;

        public sealed record ExportInvoices(int Year, AccessRequirement Requires) : IStreamQuery<int>, IBillingRequest
        {
            AccessRequirement IRequireAccess.RequiredAccess => Requires;
        }

        public sealed record Tick : IStreamQuery<int>;

        public sealed class ExportCheck(List<string> steps) : IAccessCheck
        {
            public bool Decides(AccessRequirement requirement) => requirement is MayExport;

            public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
            {
                steps.Add("check " + ((ExportInvoices)request).Year);
                return ((ExportInvoices)request).Year < 0
                    ? throw new RefusalException("billing.not-permitted", RefusalKind.NotPermitted, "Only the bookkeeper exports invoices.")
                    : ValueTask.CompletedTask;
            }
        }

        public static class StreamProbe
        {
            /// <summary>Asks for a stream through the behavior over the module's check, reads it to its end, and says what ran, in order.</summary>
            public static async Task<string> RunThroughAsync(int year, AccessRequirement requires)
            {
                var steps = new List<string>();
                var behavior = new BillingAccessStreamBehavior<ExportInvoices, int>(new AccessChecks<IBillingRequest>(new IAccessCheck[] { new ExportCheck(steps) }));
                try
                {
                    var stream = behavior.Handle(
                        new ExportInvoices(year, requires),
                        (message, cancellationToken) =>
                        {
                            steps.Add("handler " + message.Year);
                            return Invoices();
                        },
                        CancellationToken.None);
                    steps.Add("asked");

                    await foreach (var invoice in stream)
                    {
                        steps.Add("invoice " + invoice);
                    }
                }
                catch (Exception refused)
                {
                    steps.Add(refused.GetType().Name);
                }

                return string.Join(", ", steps);
            }

            private static async IAsyncEnumerable<int> Invoices()
            {
                yield return 1;
                await Task.Yield();
                yield return 2;
            }
        }
        """;

    /// <summary>
    /// What the stand-in library declares when it has a pipeline for the messages answered with a stream, as the
    /// Mediator library has: appended to <see cref="LibraryDeclaring"/>.
    /// </summary>
    /// <param name="handle">The members of the stream behavior's interface.</param>
    /// <param name="answers">What the next step answers.</param>
    /// <param name="message">What a stream message is constrained to.</param>
    public static string StreamsDeclaring(
        string handle = "IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        string answers = "IAsyncEnumerable<TResponse>",
        string message = "notnull, IStreamMessage")
        => $$"""

            public interface IStreamMessage;

            public delegate {{answers}} StreamHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken)
                where TMessage : {{message}};

            public interface IStreamPipelineBehavior<TMessage, TResponse>
                where TMessage : {{message}}
            {
                {{handle}}
            }
            """;

    /// <summary>
    /// A library under the Mediator library's names, declared the way the caller says: what another version of the
    /// library looks like to the generator, which reads the behavior's shape and assumes none of it.
    /// </summary>
    /// <param name="handle">The members of the behavior interface.</param>
    /// <param name="next">What the delegate that runs the next step takes.</param>
    /// <param name="answers">What the next step answers.</param>
    /// <param name="message">What a message is constrained to.</param>
    /// <param name="response">The constraint clause on the response, if any.</param>
    public static string LibraryDeclaring(
        string handle = "ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        string next = "TMessage message, CancellationToken cancellationToken",
        string answers = "ValueTask<TResponse>",
        string message = "notnull, IMessage",
        string response = "")
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Mediator;

            public interface IMessage;

            public abstract class Envelope;

            public delegate {{answers}} MessageHandlerDelegate<TMessage, TResponse>({{next}})
                where TMessage : {{message}}
                {{response}};

            public interface IPipelineBehavior<TMessage, TResponse>
                where TMessage : {{message}}
                {{response}}
            {
                {{handle}}
            }
            """;

    private static GeneratorTestHost WithLibrary(string library)
        => GeneratorTestHost.Create(Billing).WithDependencyInjection().WithReferencedAssembly(library, "Mediator.Abstractions");

    // ------------------------------------------------------------------ with and without the library

    [Fact]
    public void A_request_interface_gets_its_behavior_in_a_project_that_uses_the_library()
    {
        var result = GeneratorTestHost.Create(Billing).WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "namespace Shop.Billing;", "beside the interface");
        result.ShouldContain(
            Hint,
            """
            public sealed class BillingAccessBehavior<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
                where TMessage : notnull, global::Shop.Billing.IBillingRequest, global::Mediator.IMessage
            {
            """,
            "for a message of the library's that implements the interface, and for no other");
        result.ShouldContain(Hint, "private readonly global::DDDToolkit.Access.AccessChecks<global::Shop.Billing.IBillingRequest> _checks;");
        result.ShouldContain(
            Hint,
            "public async global::System.Threading.Tasks.ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, global::System.Threading.CancellationToken cancellationToken)");
        result.ShouldContain(
            Hint,
            """
                    await _checks.RequireAsync(message, cancellationToken).ConfigureAwait(false);
                    return await next(message, cancellationToken).ConfigureAwait(false);
            """,
            "the checks, then the next step, and nothing else");
    }

    [Fact]
    public void Nothing_is_generated_in_a_project_that_does_not_use_the_library()
    {
        var result = GeneratorTestHost.Create(Billing).WithDependencyInjection().RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldNotHaveGeneratedFor("IBillingRequest");
        result.GeneratedSources.Should().BeEmpty("the attribute alone changes nothing: the application asks the checks itself");
    }

    [Fact]
    public void Another_library_s_behavior_of_the_same_name_is_not_the_one()
    {
        // MediatR declares IPipelineBehavior<,> too, in its own namespace. The generator looks for the Mediator
        // library's, by its full name.
        var result = GeneratorTestHost.Create(Billing)
            .WithDependencyInjection()
            .WithReferencedAssembly(LibraryDeclaring().Replace("namespace Mediator;", "namespace MediatR;", StringComparison.Ordinal), "MediatR")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.GeneratedSources.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ the name, the place, the visibility

    [Theory]
    [InlineData("IBillingRequest", "BillingAccessBehavior")]
    [InlineData("IBillingRequests", "BillingAccessBehavior")]
    [InlineData("IBilling", "BillingAccessBehavior")]
    [InlineData("BillingRequest", "BillingAccessBehavior")]
    [InlineData("IRequest", "RequestAccessBehavior")]
    [InlineData("Payroll", "PayrollAccessBehavior")]
    public void The_behavior_is_named_after_the_interface(string interfaceName, string behaviorName)
    {
        var result = GeneratorTestHost.Create(Billing.Replace("IBillingRequest", interfaceName, StringComparison.Ordinal)).WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldContain(behaviorName + ".", "public sealed class " + behaviorName + "<TMessage, TResponse>");
        result.ShouldContain(behaviorName + ".", "public static class " + behaviorName + "Registration");
        result.ShouldContain(
            behaviorName + ".",
            "public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add" + behaviorName + "(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
    }

    [Fact]
    public void The_behavior_is_as_visible_as_the_interface()
    {
        var result = GeneratorTestHost.Create(Billing.Replace("public interface", "internal interface", StringComparison.Ordinal)).WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "internal sealed class BillingAccessBehavior<TMessage, TResponse>", "a public class could not be constrained to an internal interface");
        result.ShouldContain(Hint, "internal static class BillingAccessBehaviorRegistration");
        result.ShouldNotContain(Hint, "public sealed class");
        result.ShouldNotContain(Hint, "public static class");
    }

    [Fact]
    public void An_interface_in_the_global_namespace_gets_its_behavior_there()
    {
        var result = GeneratorTestHost.Create(Billing.Replace("namespace Shop.Billing;", string.Empty, StringComparison.Ordinal)).WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldNotContain(Hint, "namespace ");
        result.ShouldContain(Hint, "where TMessage : notnull, global::IBillingRequest, global::Mediator.IMessage");
        result.ShouldContain(Hint, "typeof(global::BillingAccessBehavior<,>)");
    }

    [Fact]
    public void Each_request_interface_of_a_project_gets_a_behavior_of_its_own()
    {
        var result = GeneratorTestHost.Create(Billing)
            .WithSource(
                """
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Access;

                namespace Shop.Shipping;

                [AccessRequests]
                public interface IShippingRequest : IRequireAccess;
                """,
                "Shipping.cs")
            .WithMediator()
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "global::DDDToolkit.Access.AccessChecks<global::Shop.Billing.IBillingRequest>");
        result.ShouldContain("ShippingAccessBehavior.", "global::DDDToolkit.Access.AccessChecks<global::Shop.Shipping.IShippingRequest>");
        result.ShouldNotContain("ShippingAccessBehavior.", "IBillingRequest", "a module's behavior asks the module's own checks");
        result.GeneratedSources.Should().HaveCount(2);
    }

    [Fact]
    public void An_interface_declared_in_parts_gets_one_behavior()
    {
        var result = GeneratorTestHost.Create(Billing.Replace("public interface", "public partial interface", StringComparison.Ordinal))
            .WithSource(
                """
                namespace Shop.Billing;

                public partial interface IBillingRequest;
                """,
                "Part.cs")
            .WithMediator()
            .RunCore();

        result.ShouldCompile();
        result.GeneratedSources.Should().ContainSingle();
    }

    // ------------------------------------------------------------------ the registration

    [Fact]
    public void The_registration_adds_the_behavior_per_scope_and_the_set_of_checks_it_asks()
    {
        var result = GeneratorTestHost.Create(Billing).WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            """
                    global::DDDToolkit.Access.AccessCheckServiceCollectionExtensions.AddAccessChecks<global::Shop.Billing.IBillingRequest>(services);
                    global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(
                        services,
                        global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(global::Mediator.IPipelineBehavior<,>), typeof(global::Shop.Billing.BillingAccessBehavior<,>)));
            """);
    }

    [Fact]
    public void Where_the_service_collection_is_not_seen_only_the_behavior_is_written()
    {
        var result = GeneratorTestHost.Create(Billing).WithMediatorAlone().RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "public sealed class BillingAccessBehavior<TMessage, TResponse>");
        result.ShouldNotContain(Hint, "BillingAccessBehaviorRegistration");
        result.ShouldNotContain(Hint, "services.AddBillingAccessBehavior()", "the behavior does not point at a registration that is not there");
    }

    // ------------------------------------------------------------------ the library's shape is read, not assumed

    [Fact]
    public void The_order_of_Handle_s_parameters_is_the_library_s()
    {
        // An earlier version of the library took the token before the next step.
        var result = WithLibrary(LibraryDeclaring(handle: "ValueTask<TResponse> Handle(TMessage message, CancellationToken cancellationToken, MessageHandlerDelegate<TMessage, TResponse> next);"))
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            "public async global::System.Threading.Tasks.ValueTask<TResponse> Handle(TMessage message, global::System.Threading.CancellationToken cancellationToken, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next)");
        result.ShouldContain(Hint, "return await next(message, cancellationToken).ConfigureAwait(false);", "the next step is called the way its delegate is declared, not the way Handle is");
    }

    [Fact]
    public void The_next_step_is_called_with_its_arguments_in_the_order_its_delegate_declares()
    {
        var result = WithLibrary(LibraryDeclaring(next: "CancellationToken cancellationToken, TMessage message")).RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "return await next(cancellationToken, message).ConfigureAwait(false);");
    }

    [Fact]
    public void The_names_of_Handle_s_parameters_are_the_library_s()
    {
        var result = WithLibrary(LibraryDeclaring(handle: "ValueTask<TResponse> Handle(TMessage @event, MessageHandlerDelegate<TMessage, TResponse> @continue, CancellationToken stop);"))
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "Handle(TMessage @event, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> @continue, global::System.Threading.CancellationToken stop)");
        result.ShouldContain(Hint, "await _checks.RequireAsync(@event, stop).ConfigureAwait(false);");
        result.ShouldContain(Hint, "return await @continue(@event, stop).ConfigureAwait(false);");
    }

    [Fact]
    public void A_library_that_answers_a_task_gets_a_behavior_that_answers_one()
    {
        var result = WithLibrary(LibraryDeclaring(
                handle: "Task<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
                answers: "Task<TResponse>"))
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "public async global::System.Threading.Tasks.Task<TResponse> Handle(");
    }

    [Fact]
    public void What_the_library_asks_of_a_message_and_a_response_the_behavior_asks_too()
    {
        var result = WithLibrary(LibraryDeclaring(message: "Envelope, IMessage, new()", response: "where TResponse : class, IEnumerable<string>")).RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            """
            public sealed class BillingAccessBehavior<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
                where TMessage : global::Mediator.Envelope, global::Shop.Billing.IBillingRequest, global::Mediator.IMessage, new()
                where TResponse : class, global::System.Collections.Generic.IEnumerable<string>
            {
            """,
            "a base class first, then the interfaces, the module's among them, then the constructor");
    }

    [Fact]
    public void A_library_that_asks_nothing_of_a_message_gets_a_behavior_constrained_to_the_interface_alone()
    {
        var result = WithLibrary(
            """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Mediator;

            public delegate ValueTask<TResponse> MessageHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken);

            public interface IPipelineBehavior<TMessage, TResponse>
            {
                ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);
            }
            """).RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "    where TMessage : global::Shop.Billing.IBillingRequest\n{");
    }

    [Fact]
    public void A_message_that_is_a_value_is_not_asked_whether_it_is_null()
    {
        // The compiler does not take "is null" of a type parameter that is a value. A library that sent values
        // would otherwise get a behavior that does not compile, which is what reading the shape is there to avoid.
        var result = WithLibrary(LibraryDeclaring(message: "struct, IMessage")).RunCore();

        result.ShouldCompile();
        result.ShouldContain(Hint, "    where TMessage : struct, global::Shop.Billing.IBillingRequest, global::Mediator.IMessage\n{");
        result.ShouldNotContain(Hint, "if (message is null)");
        result.ShouldContain(Hint, "if (next is null)", "the next step is still held to being there");
    }

    // ------------------------------------------------------------------ messages answered with a stream

    [Fact]
    public void A_request_interface_gets_a_second_behavior_for_the_messages_answered_with_a_stream()
    {
        var result = GeneratorTestHost.Create(Billing).WithMediator().RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.GeneratedSources.Should().ContainSingle("both behaviors of an interface are written in its one file");
        result.ShouldContain(
            Hint,
            """
            public sealed class BillingAccessStreamBehavior<TMessage, TResponse> : global::Mediator.IStreamPipelineBehavior<TMessage, TResponse>
                where TMessage : global::Shop.Billing.IBillingRequest, global::Mediator.IStreamMessage
            {
            """,
            "a stream message passes no IPipelineBehavior, so without this one it would reach its handler unasked");
        result.ShouldContain(
            Hint,
            "public async global::System.Collections.Generic.IAsyncEnumerable<TResponse> Handle(TMessage message, global::Mediator.StreamHandlerDelegate<TMessage, TResponse> next, [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken cancellationToken)");
        result.ShouldContain(
            Hint,
            """
                    await _checks.RequireAsync(message, cancellationToken).ConfigureAwait(false);
                    await foreach (var item in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(next(message, cancellationToken), false))
                    {
                        yield return item;
                    }
            """,
            "the checks, and only then the next step, whose stream is handed on as it comes");
        result.ShouldContain(
            Hint,
            """
                    global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(
                        services,
                        global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(global::Mediator.IStreamPipelineBehavior<,>), typeof(global::Shop.Billing.BillingAccessStreamBehavior<,>)));
            """,
            "the one registration puts it in the pipeline of the streams");
    }

    [Fact]
    public void A_library_without_a_pipeline_for_streams_gets_the_one_behavior()
    {
        var result = WithLibrary(LibraryDeclaring()).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "public sealed class BillingAccessBehavior<TMessage, TResponse>");
        result.ShouldNotContain(Hint, "AccessStreamBehavior");
        result.ShouldNotContain(Hint, "IStreamPipelineBehavior");
    }

    [Fact]
    public void The_stream_behavior_is_written_the_way_the_library_declares_its_own()
    {
        // The order and the names are the library's here too, and the name for one response of the stream is
        // one no parameter has.
        var result = WithLibrary(LibraryDeclaring() + StreamsDeclaring(
                handle: "IAsyncEnumerable<TResponse> Handle(TMessage item, CancellationToken stop, StreamHandlerDelegate<TMessage, TResponse> @continue);",
                message: "class, IStreamMessage"))
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            Hint,
            """
            public sealed class BillingAccessStreamBehavior<TMessage, TResponse> : global::Mediator.IStreamPipelineBehavior<TMessage, TResponse>
                where TMessage : class, global::Shop.Billing.IBillingRequest, global::Mediator.IStreamMessage
            {
            """);
        result.ShouldContain(
            Hint,
            "Handle(TMessage item, [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken stop, global::Mediator.StreamHandlerDelegate<TMessage, TResponse> @continue)");
        result.ShouldContain(Hint, "await _checks.RequireAsync(item, stop).ConfigureAwait(false);");
        result.ShouldContain(Hint, "await foreach (var streamed in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(@continue(item, stop), false))");
        result.ShouldContain(Hint, "yield return streamed;");
    }

    [Fact]
    public void A_stream_pipeline_alone_is_not_the_library()
    {
        // The library is noticed by IPipelineBehavior<,>. Something else that only has a type of the stream
        // behavior's name is not it, and gets nothing written.
        var result = GeneratorTestHost.Create(Billing)
            .WithDependencyInjection()
            .WithReferencedAssembly(
                """
                using System.Collections.Generic;
                using System.Threading;

                namespace Mediator;
                """ + StreamsDeclaring(),
                "Mediator.Abstractions")
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.GeneratedSources.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ what was written, run

    [Fact]
    public async Task The_behavior_asks_the_checks_and_only_then_runs_the_next_step()
    {
        var emitted = GeneratorTestHost.Create(Billing).WithSource(BillingInUse, "InUse.cs").WithMediator().RunCore().Emit();
        var mayClose = emitted.New("Shop.Billing.MayClose");

        (await RunThroughAsync(emitted, 7, mayClose)).Should().Be("check 7, handler 7");
        (await RunThroughAsync(emitted, -1, mayClose)).Should().Be("check -1, RefusalException", "a refused caller never reaches the handler");
        (await RunThroughAsync(emitted, 7, new global::DDDToolkit.Access.AccessRequirement.Open("the price list is public"))).Should().Be("handler 7", "nobody has to decide that nothing is required");
        (await RunThroughAsync(emitted, 7, null)).Should().Be("InvalidOperationException", "a request that declares nothing lets nobody through");

        static async Task<string> RunThroughAsync(EmittedAssembly emitted, int invoice, object? requires)
            => await (Task<string>)emitted.CallStatic("Shop.Billing.Probe", "RunThroughAsync", invoice, requires)!;
    }

    [Fact]
    public void The_behavior_takes_no_message_that_does_not_implement_the_interface()
    {
        var emitted = GeneratorTestHost.Create(Billing).WithSource(BillingInUse, "InUse.cs").WithMediator().RunCore().Emit();
        var behavior = emitted.Type("Shop.Billing.BillingAccessBehavior`2");

        behavior.MakeGenericType(emitted.Type("Shop.Billing.CloseInvoice"), typeof(Unit)).Should().NotBeNull();
        FluentActions.Invoking(() => behavior.MakeGenericType(emitted.Type("Shop.Billing.Ping"), typeof(Unit)))
            .Should().Throw<ArgumentException>("the constraint keeps the behavior out of every other module's pipeline");
    }

    [Fact]
    public async Task The_registered_behavior_is_the_pipeline_of_the_module_s_messages_and_of_no_others()
    {
        var emitted = GeneratorTestHost.Create(Billing).WithSource(BillingInUse, "InUse.cs").WithMediator().RunCore().Emit();
        var services = new ServiceCollection();

        emitted.CallStatic("Shop.Billing.BillingAccessBehaviorRegistration", "AddBillingAccessBehavior", services).Should().BeSameAs(services);
        var registered = services.Count;
        emitted.CallStatic("Shop.Billing.BillingAccessBehaviorRegistration", "AddBillingAccessBehavior", services);

        services.Should().HaveCount(registered, "registered once however often it is called");
        services.Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>)).Should().ContainSingle()
            .Which.Should().Match<ServiceDescriptor>(descriptor =>
                descriptor.Lifetime == ServiceLifetime.Scoped && descriptor.ImplementationType == emitted.Type("Shop.Billing.BillingAccessBehavior`2"));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var closeInvoice = emitted.Type("Shop.Billing.CloseInvoice");

        var pipeline = scope.ServiceProvider.GetServices(typeof(IPipelineBehavior<,>).MakeGenericType(closeInvoice, typeof(Unit))).ToList();
        pipeline.Should().ContainSingle().Which!.GetType().GetGenericTypeDefinition().Should().Be(emitted.Type("Shop.Billing.BillingAccessBehavior`2"));
        scope.ServiceProvider.GetServices(typeof(IPipelineBehavior<,>).MakeGenericType(emitted.Type("Shop.Billing.Ping"), typeof(Unit)))
            .Should().BeEmpty("a message that does not implement the interface is not the behavior's");

        // No check was added: the set is there all the same, lets an open request through and stops every other.
        var checks = scope.ServiceProvider.GetRequiredService(typeof(global::DDDToolkit.Access.AccessChecks<>).MakeGenericType(emitted.Type("Shop.Billing.IBillingRequest")));
        var decides = checks.GetType().GetMethod("Decides")!;
        decides.Invoke(checks, [new global::DDDToolkit.Access.AccessRequirement.Open("the price list is public")]).Should().Be(true);
        decides.Invoke(checks, [emitted.New("Shop.Billing.MayClose")]).Should().Be(false, "a requirement no check decides lets nobody through");
    }

    [Fact]
    public async Task The_stream_behavior_asks_the_checks_before_the_handler_streams_anything()
    {
        var emitted = GeneratorTestHost.Create(Billing).WithSource(BillingStreamInUse, "Streams.cs").WithMediator().RunCore().Emit();
        var mayExport = emitted.New("Shop.Billing.MayExport");

        // "asked" is where the caller holds the stream and has read nothing of it: nothing runs before the first read.
        (await RunThroughAsync(emitted, 2026, mayExport)).Should().Be("asked, check 2026, handler 2026, invoice 1, invoice 2");
        (await RunThroughAsync(emitted, -1, mayExport)).Should().Be("asked, check -1, RefusalException", "a refused caller never reaches the handler, and reads nothing");
        (await RunThroughAsync(emitted, 2026, new global::DDDToolkit.Access.AccessRequirement.Open("the invoices of a demo are public")))
            .Should().Be("asked, handler 2026, invoice 1, invoice 2", "nobody has to decide that nothing is required");
        (await RunThroughAsync(emitted, 2026, null)).Should().Be("asked, InvalidOperationException", "a stream query that declares nothing lets nobody through");

        static async Task<string> RunThroughAsync(EmittedAssembly emitted, int year, object? requires)
            => await (Task<string>)emitted.CallStatic("Shop.Billing.StreamProbe", "RunThroughAsync", year, requires)!;
    }

    [Fact]
    public async Task The_registration_puts_the_stream_behavior_in_the_pipeline_of_the_module_s_stream_messages_and_of_no_others()
    {
        var emitted = GeneratorTestHost.Create(Billing).WithSource(BillingStreamInUse, "Streams.cs").WithMediator().RunCore().Emit();
        var services = new ServiceCollection();

        emitted.CallStatic("Shop.Billing.BillingAccessBehaviorRegistration", "AddBillingAccessBehavior", services);
        var registered = services.Count;
        emitted.CallStatic("Shop.Billing.BillingAccessBehaviorRegistration", "AddBillingAccessBehavior", services);

        services.Should().HaveCount(registered, "registered once however often it is called");
        services.Where(descriptor => descriptor.ServiceType == typeof(IStreamPipelineBehavior<,>)).Should().ContainSingle()
            .Which.Should().Match<ServiceDescriptor>(descriptor =>
                descriptor.Lifetime == ServiceLifetime.Scoped && descriptor.ImplementationType == emitted.Type("Shop.Billing.BillingAccessStreamBehavior`2"));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var exportInvoices = emitted.Type("Shop.Billing.ExportInvoices");

        scope.ServiceProvider.GetServices(typeof(IStreamPipelineBehavior<,>).MakeGenericType(exportInvoices, typeof(int)))
            .Should().ContainSingle().Which!.GetType().GetGenericTypeDefinition().Should().Be(emitted.Type("Shop.Billing.BillingAccessStreamBehavior`2"));
        scope.ServiceProvider.GetServices(typeof(IStreamPipelineBehavior<,>).MakeGenericType(emitted.Type("Shop.Billing.Tick"), typeof(int)))
            .Should().BeEmpty("a stream message that does not implement the interface is not the behavior's");
        FluentActions.Invoking(() => emitted.Type("Shop.Billing.BillingAccessBehavior`2").MakeGenericType(exportInvoices, typeof(int)))
            .Should().Throw<ArgumentException>("a stream message is no message of the other pipeline, which is why it has a behavior of its own");
    }
}
