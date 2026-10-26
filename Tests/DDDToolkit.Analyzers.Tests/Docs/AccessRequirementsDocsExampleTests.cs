using System.Reflection;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Analyzers.Tests.Docs;

/// <summary>
/// The examples of docs/access-requirements.md, read from the page and compiled as they stand: a module's request
/// interface, three requests, a requirement and a check of the module's own, its registration, a handler that acts
/// on its request, and a dispatcher that asks the checks. And run, so what the page says they do is what they do:
/// a caller that holds the key passes and its handler closes the invoice its request names, one that does not is
/// refused with the module's code, a request anyone may send passes with nothing asked, and one that requires a
/// signed-in user passes for one and refuses everyone else, with no check the module added.
/// </summary>
public class AccessRequirementsDocsExampleTests
{
    private const string Page = "access-requirements.md";

    private static readonly string[] ExampleHeadings = ["What a request declares", "What answers it", "Asking the checks without Mediator"];

    /// <summary>What the page leaves out: the usings, and a namespace of the application's own.</summary>
    private const string Header =
        """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Access;
        using DDDToolkit.Exceptions;
        using Microsoft.Extensions.DependencyInjection;

        namespace Billing;

        """;

    /// <summary>What a test adds to run the example: an answer to whether the caller holds a key, and a store that remembers.</summary>
    private const string TestOnly =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Billing;

        public sealed class TheAnswer(bool holds) : IInvoiceAccess
        {
            public ValueTask<bool> HoldsKeyAsync(string key, InvoiceId invoice, CancellationToken cancellationToken) => ValueTask.FromResult(holds);
        }

        public sealed class RememberingStore : IInvoiceStore
        {
            public List<InvoiceId> Closed { get; } = [];

            public Task CloseAsync(InvoiceId invoice, CancellationToken cancellationToken)
            {
                Closed.Add(invoice);
                return Task.CompletedTask;
            }
        }
        """;

    private static GeneratorRunOutcome Run()
    {
        var examples = DocsExamples.Of(Page, ExampleHeadings);
        var host = GeneratorTestHost.Create(TestOnly, "TestOnly.cs");
        for (var index = 0; index < examples.Count; index++)
        {
            host = host.WithSource(Header + examples[index], "Example" + index + ".cs");
        }

        return host.WithAssemblyName("Billing").WithDependencyInjection().RunCore();
    }

    [Fact]
    public void The_examples_compile_as_the_page_writes_them_and_nothing_is_reported()
    {
        var result = Run();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
    }

    [Fact]
    public void A_caller_that_holds_the_key_passes_and_the_handler_closes_the_invoice_its_request_names()
    {
        var emitted = Run().Emit();
        using var provider = Services(emitted, holds: true);
        using var scope = provider.CreateScope();

        var invoice = emitted.CallStatic("Billing.InvoiceId", "CreateUnique")!;
        var command = emitted.New("Billing.CloseInvoice", invoice);
        var checks = scope.ServiceProvider.GetRequiredService(typeof(AccessChecks<>).MakeGenericType(emitted.Type("Billing.IBillingRequest")));

        Await(checks.GetType().GetMethod("RequireAsync")!.Invoke(checks, [command, CancellationToken.None])!);
        var handler = ActivatorUtilities.CreateInstance(scope.ServiceProvider, emitted.Type("Billing.CloseInvoiceHandler"));
        Await(emitted.Call(handler, "HandleAsync", command, CancellationToken.None)!);

        var store = scope.ServiceProvider.GetRequiredService(emitted.Type("Billing.IInvoiceStore"));
        ((System.Collections.IEnumerable)emitted.Property(store, "Closed")!).Cast<object>().Should().Equal(invoice);

        // The request is in hand for what runs after its checks, and the page's check kept nothing with it.
        (RequestInHand.Current?.Request).Should().BeSameAs(command);
    }

    [Fact]
    public void A_caller_that_does_not_hold_the_key_is_refused_with_the_modules_code_and_a_request_anyone_may_send_passes()
    {
        var emitted = Run().Emit();
        using var provider = Services(emitted, holds: false);
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService(typeof(AccessChecks<>).MakeGenericType(emitted.Type("Billing.IBillingRequest")));
        var require = checks.GetType().GetMethod("RequireAsync")!;

        var command = emitted.New("Billing.CloseInvoice", emitted.CallStatic("Billing.InvoiceId", "CreateUnique"));
        FluentActions.Invoking(() => Await(require.Invoke(checks, [command, CancellationToken.None])!))
            .Should().Throw<RefusalException>().Which.Code.Should().Be("billing.not-permitted");

        Await(require.Invoke(checks, [emitted.New("Billing.ListPlans"), CancellationToken.None])!);
    }

    [Fact]
    public void A_request_that_requires_a_signed_in_user_passes_for_one_and_refuses_anyone_else()
    {
        var emitted = Run().Emit();
        using var provider = Services(emitted, holds: false);
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService(typeof(AccessChecks<>).MakeGenericType(emitted.Type("Billing.IBillingRequest")));
        var require = checks.GetType().GetMethod("RequireAsync")!;
        var mine = emitted.New("Billing.MyInvoices");

        // The module added a check of its own and none of the core's: who is calling is the core's in every module.
        using (Callers.Begin(Caller.User(Guid.NewGuid())))
        {
            Await(require.Invoke(checks, [mine, CancellationToken.None])!);
        }

        using (Callers.Begin(Caller.Anonymous))
        {
            FluentActions.Invoking(() => Await(require.Invoke(checks, [mine, CancellationToken.None])!))
                .Should().Throw<RefusalException>().Which.Code.Should().Be("access.not-signed-in");
        }
    }

    /// <summary>The module's services, as the page registers them, with the two answers a test supplies.</summary>
    private static ServiceProvider Services(EmittedAssembly emitted, bool holds)
    {
        var services = new ServiceCollection();
        emitted.CallStatic("Billing.BillingModule", "AddBilling", services);
        services.AddSingleton(emitted.Type("Billing.IInvoiceAccess"), emitted.New("Billing.TheAnswer", holds));
        services.AddScoped(emitted.Type("Billing.IInvoiceStore"), emitted.Type("Billing.RememberingStore"));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Waits for a <see cref="Task"/> or a <see cref="ValueTask"/> a reflected call answered, and throws what it threw.</summary>
    private static void Await(object pending)
    {
        switch (pending)
        {
            case Task task:
                task.GetAwaiter().GetResult();
                break;
            case ValueTask valueTask:
                valueTask.GetAwaiter().GetResult();
                break;
            default:
                throw new InvalidOperationException("Not a task: " + pending.GetType().Name);
        }
    }
}
