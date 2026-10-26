using System.Collections.Immutable;
using DDDToolkit.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers.Tests.CodeFixes;

/// <summary>
/// The fix for DDD00061: a request handed to its handler directly is sent instead, through a sender the code already
/// reaches, so it passes the pipeline and the access behavior in it. Each test runs the real generators and analyzers
/// for the diagnostic, applies the fix through a workspace the way an IDE does, and runs them again: a fix is right
/// when the warning is gone and the code still compiles. Where the code reaches no sender, no fix is offered.
/// </summary>
public class SendThroughSenderCodeFixTests
{
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

        public sealed record InvoiceLines(int Invoice) : IStreamQuery<string>, IBillingRequest
        {
            AccessRequirement IRequireAccess.RequiredAccess => new MayClose();
        }

        public sealed class CloseInvoiceHandler : ICommandHandler<CloseInvoice>
        {
            public ValueTask<Unit> Handle(CloseInvoice command, CancellationToken cancellationToken) => new(Unit.Value);
        }

        public sealed class InvoiceLinesHandler : IStreamQueryHandler<InvoiceLines, string>
        {
            public async IAsyncEnumerable<string> Handle(InvoiceLines query, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return "bread";
            }
        }


        """;

    [Fact]
    public async Task A_command_is_sent_with_the_sender_the_class_was_given()
    {
        var fixedSource = await Fix(
            """
            public sealed class Closing(ISender sender, CloseInvoiceHandler handler)
            {
                public async Task CloseAsync(int invoice, CancellationToken cancellationToken)
                    => await handler.Handle(new CloseInvoice(invoice), cancellationToken);
            }
            """);

        fixedSource.Should().Contain("=> await sender.Send(new CloseInvoice(invoice), cancellationToken);");
    }

    [Fact]
    public async Task A_stream_query_is_sent_with_CreateStream()
    {
        var fixedSource = await Fix(
            """
            public sealed class Export(InvoiceLinesHandler handler, ISender sender)
            {
                public IAsyncEnumerable<string> Lines(int invoice, CancellationToken cancellationToken) => handler.Handle(new InvoiceLines(invoice), cancellationToken);
            }
            """);

        fixedSource.Should().Contain("=> sender.CreateStream(new InvoiceLines(invoice), cancellationToken);");
    }

    [Fact]
    public async Task Named_arguments_lose_their_names_since_the_sender_names_its_parameters_otherwise()
    {
        var fixedSource = await Fix(
            """
            public sealed class Closing(ICommandHandler<CloseInvoice> handler, ISender sender)
            {
                public ValueTask<Unit> CloseAsync(CancellationToken cancellationToken)
                    => handler.Handle(command: new CloseInvoice(7), cancellationToken: cancellationToken);
            }
            """);

        fixedSource.Should().Contain("=> sender.Send(new CloseInvoice(7), cancellationToken);");
    }

    [Fact]
    public async Task Named_arguments_written_out_of_order_are_put_in_the_order_of_the_parameters()
    {
        var fixedSource = await Fix(
            """
            public sealed class Closing(ICommandHandler<CloseInvoice> handler, ISender sender)
            {
                public ValueTask<Unit> CloseAsync(CancellationToken cancellationToken)
                    => handler.Handle(cancellationToken: cancellationToken, command: new CloseInvoice(7));
            }
            """);

        fixedSource.Should().Contain("=> sender.Send(new CloseInvoice(7), cancellationToken);");
    }

    [Fact]
    public async Task A_static_local_function_is_fixed_with_a_sender_of_its_own_and_not_one_of_the_method_around_it()
    {
        // By name the outer parameter would come first; a static local function cannot use it.
        var fixedSource = await Fix(
            """
            public static class Closing
            {
                public static Task CloseAsync(ISender outer, CloseInvoiceHandler handler, CancellationToken cancellationToken)
                {
                    return Close(handler, outer, cancellationToken);

                    static async Task Close(CloseInvoiceHandler inner, ISender own, CancellationToken token)
                        => await inner.Handle(new CloseInvoice(7), token);
                }
            }
            """);

        fixedSource.Should().Contain("=> await own.Send(new CloseInvoice(7), token);");
    }

    [Fact]
    public async Task No_fix_from_static_code_with_only_a_sender_it_cannot_use()
    {
        // A static member reaches no parameter of the primary constructor; a static lambda none of the method around it.
        var fromAStaticMember = await Actions(
            """
            public sealed class Closing(ISender sender)
            {
                public ISender Sender => sender;

                public static async Task CloseAsync(CloseInvoiceHandler handler, CancellationToken cancellationToken)
                    => await handler.Handle(new CloseInvoice(7), cancellationToken);
            }
            """);

        var fromAStaticLambda = await Actions(
            """
            public static class Closing
            {
                public static System.Func<CloseInvoiceHandler, Task> Later(ISender sender)
                {
                    _ = sender;
                    return static async handler => await handler.Handle(new CloseInvoice(7), CancellationToken.None);
                }
            }
            """);

        fromAStaticMember.Should().BeEmpty();
        fromAStaticLambda.Should().BeEmpty();
    }

    [Fact]
    public async Task A_local_is_preferred_to_a_field_and_a_mediator_is_a_sender_too()
    {
        var fixedSource = await Fix(
            """
            public sealed class Closing(CloseInvoiceHandler handler, IMediator fromTheContainer)
            {
                private readonly IMediator _mediator = fromTheContainer;

                public async Task CloseAsync(CancellationToken cancellationToken)
                {
                    IMediator nearest = _mediator;
                    await handler.Handle(new CloseInvoice(7), cancellationToken);
                }
            }
            """);

        fixedSource.Should().Contain("await nearest.Send(new CloseInvoice(7), cancellationToken);");
    }

    [Fact]
    public async Task No_fix_where_the_code_reaches_no_sender()
    {
        var actions = await Actions(
            """
            public sealed class Closing(CloseInvoiceHandler handler)
            {
                private readonly ISender? _sender = null;

                public static async Task CloseAsync(CloseInvoiceHandler handler, CancellationToken cancellationToken)
                {
                    await handler.Handle(new CloseInvoice(7), cancellationToken);   // a static member reaches no field
                    ISender later = null!;                                          // and a local declared after the call is not there yet
                    _ = later;
                }

                public ISender? Sender => _sender;

                public CloseInvoiceHandler Handler => handler;
            }
            """);

        actions.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ harness

    /// <summary>Applies the one fix offered for the one diagnostic and returns the fixed text, checked by a second run.</summary>
    private static async Task<string> Fix(string code)
    {
        var (document, diagnostics) = await Open(code);
        var actions = await ActionsFor(document, diagnostics.Single());
        var chosen = actions.Should().ContainSingle().Subject;
        chosen.EquivalenceKey.Should().Be("DDDToolkit.SendThroughSender");

        var operations = await chosen.GetOperationsAsync(CancellationToken.None);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var fixedText = (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString().Replace("\r\n", "\n");

        var rerun = GeneratorTestHost.Create(fixedText).WithMediator().WithAnalyzers(GeneratorTestHost.CoreAnalyzers()).RunCore();
        rerun.ShouldNotHaveDiagnostic("DDD00061");
        rerun.ShouldCompile();

        return fixedText;
    }

    private static async Task<List<CodeAction>> Actions(string code)
    {
        var (document, diagnostics) = await Open(code);
        return await ActionsFor(document, diagnostics.Single());
    }

    private static async Task<List<CodeAction>> ActionsFor(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new SendThroughSenderCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    /// <summary>
    /// Runs the generators and analyzers and opens the source as a workspace document, with DDD00061 anchored in that
    /// document and its properties kept, since the fix reads there whether a stream is sent.
    /// </summary>
    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> Open(string code)
    {
        var text = (Billing + code).Replace("\r\n", "\n");
        var host = GeneratorTestHost.Create(text).WithMediator().WithAnalyzers(GeneratorTestHost.CoreAnalyzers());
        var outcome = host.RunCore();
        outcome.ShouldCompile();

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            GeneratorTestHost.DefaultAssemblyName,
            GeneratorTestHost.DefaultAssemblyName,
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: host.CreateParseOptions(),
            metadataReferences: host.References));
        var document = workspace.AddDocument(project.Id, "Source.cs", SourceText.From(text));
        var tree = (await document.GetSyntaxTreeAsync())!;

        var diagnostics = outcome.AnalyzerDiagnostics
            .Where(diagnostic => diagnostic.Id == "DDD00061")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => Diagnostic.Create(diagnostic.Descriptor, Location.Create(tree, diagnostic.Location.SourceSpan), diagnostic.Properties))
            .ToImmutableArray();

        diagnostics.Should().NotBeEmpty("the source should report DDD00061");
        return (document, diagnostics);
    }
}
