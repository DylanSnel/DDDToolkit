using DDDToolkit.Analyzers.Tests.Generation;

namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// DDD00056 and DDD00057: an interface marked <c>[AccessRequests]</c> that no behavior can be written for, and
/// a Mediator library whose behavior the generator does not know. Each stops the behavior of the interface it is
/// reported on, without a crash, and leaves the others alone: a behavior that was written wrong, or one that
/// never ran the checks, would be worse than none. And DDD00058: a notification that implements such an
/// interface, which no behavior ever sees.
/// </summary>
public class AccessRequestsDiagnosticTests
{
    private const string Usings =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;

        namespace Shop.Billing;


        """;

    private static GeneratorRunOutcome Run(string declarations) => GeneratorTestHost.Create(Usings + declarations).WithMediator().RunCore();

    // ------------------------------------------------------------------ DDD00056

    [Fact]
    public void An_interface_that_does_not_derive_from_IRequireAccess_reports_DDD00056_with_what_to_write()
    {
        var result = Run(
            """
            [AccessRequests]
            public interface IBillingRequest;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00056");
        result.ShouldHaveDiagnostic("DDD00056", at: "IBillingRequest").GetMessage()
            .Should().Contain("does not derive from IRequireAccess").And.Contain("interface IBillingRequest : IRequireAccess");
        result.ShouldNotHaveGeneratedFor("IBillingRequest");
        result.ShouldNotCrash();
    }

    [Fact]
    public void An_interface_that_derives_from_it_through_another_is_fine()
    {
        var result = Run(
            """
            public interface IChecked : IRequireAccess;

            [AccessRequests]
            public interface IBillingRequest : IChecked;
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldHaveGenerated("BillingAccessBehavior.");
    }

    [Fact]
    public void An_interface_with_type_parameters_reports_DDD00056()
    {
        var result = Run(
            """
            [AccessRequests]
            public interface IBillingRequest<TAnswer> : IRequireAccess;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00056");
        result.ShouldHaveDiagnostic("DDD00056", at: "IBillingRequest").GetMessage().Should().Contain("type parameters");
        result.ShouldNotHaveGeneratedFor("IBillingRequest");
    }

    [Fact]
    public void An_interface_nested_in_a_class_reports_DDD00056()
    {
        var result = Run(
            """
            public static class Billing
            {
                [AccessRequests]
                public interface IBillingRequest : IRequireAccess;
            }
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00056");
        result.ShouldHaveDiagnostic("DDD00056", at: "IBillingRequest").GetMessage().Should().Contain("nested in 'Billing'").And.Contain("in a namespace");
        result.ShouldNotHaveGeneratedFor("IBillingRequest");
    }

    [Fact]
    public void A_file_local_interface_reports_DDD00056()
    {
        var result = Run(
            """
            [AccessRequests]
            file interface IBillingRequest : IRequireAccess;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00056");
        result.ShouldHaveDiagnostic("DDD00056", at: "IBillingRequest").GetMessage().Should().Contain("file-local");
        result.ShouldNotHaveGeneratedFor("IBillingRequest");
    }

    [Fact]
    public void Two_interfaces_that_give_one_behavior_name_report_DDD00056_each_naming_the_other()
    {
        var result = Run(
            """
            [AccessRequests]
            public interface IBillingRequest : IRequireAccess;

            [AccessRequests]
            public interface IBilling : IRequireAccess;

            [AccessRequests]
            public interface IShippingRequest : IRequireAccess;
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00056", "DDD00056");
        result.ShouldHaveDiagnostic("DDD00056", at: "IBillingRequest").GetMessage().Should().Contain("'BillingAccessBehavior'").And.Contain("'IBilling'");
        result.ShouldHaveDiagnostic("DDD00056", at: "IBilling").GetMessage().Should().Contain("'BillingAccessBehavior'").And.Contain("'IBillingRequest'");
        result.ShouldNotHaveGeneratedFor("BillingAccessBehavior");
        result.ShouldHaveGenerated("ShippingAccessBehavior.");
    }

    [Fact]
    public void The_same_name_in_two_namespaces_is_two_behaviors()
    {
        var result = GeneratorTestHost.Create(AccessBehaviorGenerationTests.Billing)
            .WithSource(AccessBehaviorGenerationTests.Billing.Replace("Shop.Billing", "Shop.Payroll", StringComparison.Ordinal), "Payroll.cs")
            .WithMediator()
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.GeneratedSources.Select(source => source.HintName).Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }

    [Fact]
    public void A_misshapen_interface_is_reported_where_no_library_is_referenced_too()
    {
        // The attribute says the interface is a module's request interface. One that is not shaped as such is a
        // mistake whether or not a behavior would be written, and adding the library later brings no surprise.
        var result = GeneratorTestHost.Create(Usings +
            """
            [AccessRequests]
            public interface IBillingRequest;
            """).RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00056");
    }

    // ------------------------------------------------------------------ DDD00057

    [Theory]
    [InlineData(
        "ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken); void Reset();",
        "more to implement than the one method Handle")]
    [InlineData(
        "ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next);",
        "does not take the message, a cancellation token and the delegate")]
    [InlineData(
        "ValueTask<TResponse> Handle(TMessage message, TMessage again, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        "does not take the message, a cancellation token and the delegate")]
    [InlineData(
        "ValueTask<TResponse> Handle(in TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        "does not take the message, a cancellation token and the delegate")]
    [InlineData(
        "ValueTask<TResponse> Run(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        "more to implement than the one method Handle")]
    [InlineData(
        "ValueTask<TResponse> Handle<TOther>(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        "more to implement than the one method Handle")]
    public void A_behavior_declared_otherwise_than_the_generator_knows_it_reports_DDD00057(string handle, string says)
    {
        var result = GeneratorTestHost.Create(AccessBehaviorGenerationTests.Billing)
            .WithDependencyInjection()
            .WithReferencedAssembly(AccessBehaviorGenerationTests.LibraryDeclaring(handle: handle), "Mediator.Abstractions")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00057");
        result.ShouldHaveDiagnostic("DDD00057", at: "IBillingRequest").GetMessage()
            .Should().Contain("declares IPipelineBehavior<,> otherwise than the generator knows it").And.Contain(says)
            .And.Contain("AccessChecks<IBillingRequest>.RequireAsync(message, cancellationToken)", "it says what to write by hand")
            .And.Contain("its Handle is async", "the check a request passed is kept with the flow of the method that awaited it, for the handler");
        result.GeneratedSources.Should().BeEmpty("nothing is written rather than guessed");
        result.ShouldNotCrash();
    }

    [Theory]
    [InlineData("TMessage message", "ValueTask<TResponse>")]
    [InlineData("TMessage message, CancellationToken cancellationToken, int attempt", "ValueTask<TResponse>")]
    [InlineData("TMessage message, CancellationToken cancellationToken", "Task<TResponse>")]
    public void A_next_step_declared_otherwise_reports_DDD00057(string next, string answers)
    {
        var result = GeneratorTestHost.Create(AccessBehaviorGenerationTests.Billing)
            .WithDependencyInjection()
            .WithReferencedAssembly(AccessBehaviorGenerationTests.LibraryDeclaring(next: next, answers: answers), "Mediator.Abstractions")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00057");
        result.ShouldHaveDiagnostic("DDD00057", at: "IBillingRequest").GetMessage()
            .Should().Contain("a next step that does not take the message and a cancellation token and answer what Handle answers");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact]
    public void A_behavior_that_answers_no_task_of_the_response_reports_DDD00057()
    {
        var result = GeneratorTestHost.Create(AccessBehaviorGenerationTests.Billing)
            .WithDependencyInjection()
            .WithReferencedAssembly(
                AccessBehaviorGenerationTests.LibraryDeclaring(
                    handle: "IAsyncEnumerable<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
                    answers: "IAsyncEnumerable<TResponse>"),
                "Mediator.Abstractions")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00057");
        result.ShouldHaveDiagnostic("DDD00057", at: "IBillingRequest").GetMessage().Should().Contain("does not answer a ValueTask or a Task of the response");
        result.GeneratedSources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(
        "ValueTask<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        "ValueTask<TResponse>",
        "does not answer an IAsyncEnumerable of the response")]
    [InlineData(
        "ValueTask<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken);",
        "IAsyncEnumerable<TResponse>",
        "a next step that does not take the message and a cancellation token and answer what Handle answers")]
    [InlineData(
        "IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next);",
        "IAsyncEnumerable<TResponse>",
        "does not take the message, a cancellation token and the delegate")]
    [InlineData(
        "IAsyncEnumerable<TResponse> Handle(TMessage message, StreamHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken); void Reset();",
        "IAsyncEnumerable<TResponse>",
        "more to implement than the one method Handle")]
    public void A_stream_behavior_declared_otherwise_reports_DDD00057_and_stops_both_behaviors(string handle, string answers, string says)
    {
        // The behavior for ordinary messages could be written here. It is not: half of a module's messages held
        // and the other half not would look, from the registration, like all of them were.
        var result = GeneratorTestHost.Create(AccessBehaviorGenerationTests.Billing)
            .WithDependencyInjection()
            .WithReferencedAssembly(AccessBehaviorGenerationTests.LibraryDeclaring() + AccessBehaviorGenerationTests.StreamsDeclaring(handle: handle, answers: answers), "Mediator.Abstractions")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00057");
        result.ShouldHaveDiagnostic("DDD00057", at: "IBillingRequest").GetMessage()
            .Should().Contain("declares IStreamPipelineBehavior<,> otherwise than the generator knows it").And.Contain(says);
        result.GeneratedSources.Should().BeEmpty("nothing is written rather than guessed");
        result.ShouldNotCrash();
    }

    [Fact]
    public void An_unknown_library_is_reported_for_each_interface_and_a_misshapen_interface_for_its_own_mistake()
    {
        var result = GeneratorTestHost.Create(Usings +
                """
                [AccessRequests]
                public interface IBillingRequest : IRequireAccess;

                [AccessRequests]
                public interface IShippingRequest : IRequireAccess;

                [AccessRequests]
                public interface IPayrollRequest;
                """)
            .WithDependencyInjection()
            .WithReferencedAssembly(AccessBehaviorGenerationTests.LibraryDeclaring(handle: "void Handle();"), "Mediator.Abstractions")
            .RunCore();

        result.ShouldHaveExactlyDiagnostics("DDD00056", "DDD00057", "DDD00057");
        result.ShouldHaveDiagnostic("DDD00057", at: "IBillingRequest");
        result.ShouldHaveDiagnostic("DDD00057", at: "IShippingRequest");
        result.ShouldHaveDiagnostic("DDD00056", at: "IPayrollRequest");
        result.GeneratedSources.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ DDD00058

    private const string BillingModule =
        """
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using Mediator;

        namespace Shop.Billing;

        public sealed record MayRead : AccessRequirement;

        [AccessRequests]
        public interface IBillingRequest : IRequireAccess;


        """;

    private static GeneratorRunOutcome RunWithAnalyzers(string declarations)
        => GeneratorTestHost.Create(BillingModule + declarations).WithMediator().WithAnalyzers(GeneratorTestHost.CoreAnalyzers()).RunCore();

    [Fact]
    public void A_notification_that_implements_the_interface_is_reported()
    {
        var result = RunWithAnalyzers(
            """
            public sealed record InvoiceClosed(int Invoice) : INotification, IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
            }

            public sealed record CloseInvoice(int Invoice) : ICommand, IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
            }
            """);

        // The behavior is written for the interface, and holds the command. The notification is published
        // through no pipeline: what it declares, nothing asks, and that is said on the notification.
        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00058");
        result.ShouldHaveDiagnostic("DDD00058", at: "InvoiceClosed").GetMessage().Should()
            .StartWith("'InvoiceClosed' implements 'IBillingRequest', which is marked [AccessRequests], and is a notification of the Mediator library.")
            .And.Contain("published to its handlers through no pipeline")
            .And.Contain("AccessChecks<IBillingRequest>.RequireAsync");
        result.ShouldHaveGenerated("BillingAccessBehavior.");
        result.ShouldNotCrash();
    }

    [Fact]
    public void A_notification_is_reported_where_it_and_the_interface_are_first_joined_and_not_on_what_derives_from_that()
    {
        var result = RunWithAnalyzers(
            """
            // Through an interface of the module's own that is both: said there, and not on the class that
            // implements it, nor on the one that derives from that class.
            public interface IBillingEvent : INotification, IBillingRequest;

            public abstract record BillingEvent : IBillingEvent
            {
                AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
            }

            public sealed record InvoiceSent(int Invoice) : BillingEvent;

            // Joined on a class: said there, and not on what derives from it.
            public abstract record ReceiptEvent : INotification, IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
            }

            public sealed record ReceiptPrinted(int Invoice) : ReceiptEvent;

            // A struct that is both is a notification as well.
            public readonly record struct InvoiceCounted(int Count) : INotification, IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
            }
            """);

        result.ShouldHaveExactlyDiagnostics("DDD00058", "DDD00058", "DDD00058");
        result.ShouldHaveDiagnostic("DDD00058", at: "IBillingEvent");
        result.ShouldHaveDiagnostic("DDD00058", at: "ReceiptEvent");
        result.ShouldHaveDiagnostic("DDD00058", at: "InvoiceCounted");
    }

    [Fact]
    public void A_partial_notification_is_reported_once_at_the_part_that_joins_the_two()
    {
        // Two parts in two files, the second first: one mistake, said once, where the notification takes the interface.
        var result = GeneratorTestHost.Create(
                """
                namespace Shop.Billing;

                public sealed partial record InvoiceVoided
                {
                    public string Reason => "voided";
                }
                """,
                "InvoiceVoided.Reason.cs")
            .WithSource(
                BillingModule
                + """
                  public sealed partial record InvoiceVoided(int Invoice) : INotification, IBillingRequest
                  {
                      AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
                  }
                  """,
                "InvoiceVoided.cs")
            .WithMediator()
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00058");
        result.ShouldHaveDiagnostic("DDD00058", at: "InvoiceVoided").Location.SourceTree!.FilePath.Should().EndWith("InvoiceVoided.cs");
    }

    [Fact]
    public void A_notification_that_declares_nothing_and_a_request_that_is_no_notification_are_left_alone()
    {
        var result = RunWithAnalyzers(
            """
            public sealed record InvoiceClosed(int Invoice) : INotification;

            public sealed record CloseInvoice(int Invoice) : ICommand, IBillingRequest
            {
                AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
            }

            // Declares what it requires without the module's interface: no behavior is written for it either way.
            public sealed record InvoiceVoided(int Invoice) : INotification, IRequireAccess
            {
                public AccessRequirement RequiredAccess => new MayRead();
            }
            """);

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }

    [Fact]
    public void Without_the_library_nothing_is_a_notification_and_nothing_is_reported()
    {
        var result = GeneratorTestHost.Create(Usings +
                """
                public interface INotification;

                [AccessRequests]
                public interface IBillingRequest : IRequireAccess;

                public sealed record MayRead : AccessRequirement;

                public sealed record InvoiceClosed(int Invoice) : INotification, IBillingRequest
                {
                    AccessRequirement IRequireAccess.RequiredAccess => new MayRead();
                }
                """)
            .WithAnalyzers(GeneratorTestHost.CoreAnalyzers())
            .RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
    }
}
