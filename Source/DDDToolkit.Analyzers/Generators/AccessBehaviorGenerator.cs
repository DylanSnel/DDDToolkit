using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes the pipeline behavior that holds a module's commands and queries to the access they declare, for
/// every interface marked <c>[AccessRequests]</c>, in a project that uses the Mediator library. A module writes
/// <code>
/// [AccessRequests]
/// public interface IBillingRequest : IRequireAccess;
/// </code>
/// and gets, beside the interface,
/// <code>
/// public sealed class BillingAccessBehavior&lt;TMessage, TResponse&gt; : IPipelineBehavior&lt;TMessage, TResponse&gt;
///     where TMessage : IBillingRequest, IMessage
/// {
///     public async ValueTask&lt;TResponse&gt; Handle(TMessage message, MessageHandlerDelegate&lt;TMessage, TResponse&gt; next, CancellationToken cancellationToken)
///     {
///         await _checks.RequireAsync(message, cancellationToken);
///         return await next(message, cancellationToken);
///     }
/// }
/// </code>
/// over <c>AccessChecks&lt;IBillingRequest&gt;</c>, and <c>services.AddBillingAccessBehavior()</c>, which adds it
/// to the pipeline and registers the set of checks it asks.
/// <para>
/// The library sends a message that is answered with a stream through a pipeline of its own,
/// <c>IStreamPipelineBehavior&lt;TMessage, TResponse&gt;</c>, which the behavior above is never part of. So
/// where the library has that pipeline a second class is written in the same file,
/// <c>BillingAccessStreamBehavior&lt;TMessage, TResponse&gt;</c>, which asks the same checks before the
/// handler streams anything, and the one registration adds both. Otherwise a stream query that implements
/// the interface would reach its handler with nothing having asked what it requires.
/// </para>
/// <para>
/// The library is noticed by its type, <c>Mediator.IPipelineBehavior&lt;TMessage, TResponse&gt;</c>, as
/// System.Text.Json and HotChocolate are: the toolkit references none of them, and a project that cannot see
/// the type gets nothing written. How a behavior is implemented is read from that type, not assumed: the
/// constraints on a message, the order of <c>Handle</c>'s parameters, which differs between versions of the
/// library, and what it answers. A shape the generator does not know, of either pipeline, is DDD00057, and
/// nothing is written.
/// </para>
/// <para>
/// The behavior is named after the interface, <c>IBillingRequest</c> giving <c>BillingAccessBehavior</c>, is
/// declared in the interface's namespace and is as visible as the interface. The registration is written
/// only where the project can see <c>IServiceCollection</c>, which the toolkit's own package brings.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AccessBehaviorGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaces = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownTypes.AccessRequestsAttribute,
            predicate: static (node, _) => node is InterfaceDeclarationSyntax,
            transform: static (syntaxContext, cancellationToken) => AccessRequestsInterface.Create(syntaxContext, cancellationToken));

        // Collected, because two interfaces of one namespace may give their behaviors one name, and only all of
        // them together show it.
        context.RegisterSourceOutput(interfaces.Collect(), static (productionContext, all) => Execute(productionContext, all));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<AccessRequestsInterface> all)
    {
        // An interface declared in parts is found once per part that carries the attribute.
        var interfaces = all
            .GroupBy(static each => each.FullyQualifiedName, StringComparer.Ordinal)
            .Select(static parts => parts.First())
            .OrderBy(static each => each.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        foreach (var requests in interfaces)
        {
            if (requests.Problem is { } problem)
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.AccessRequestsShape, requests.Location, requests.Name, problem).Report(context);
                continue;
            }

            var sameName = interfaces.FirstOrDefault(other =>
                !ReferenceEquals(other, requests)
                && other.Problem is null
                && string.Equals(other.Namespace, requests.Namespace, StringComparison.Ordinal)
                && string.Equals(other.BehaviorName, requests.BehaviorName, StringComparison.Ordinal));
            if (sameName is not null)
            {
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.AccessRequestsShape,
                    requests.Location,
                    requests.Name,
                    "gives its behavior the name '" + requests.BehaviorName + "', which '" + sameName.Name + "' in the same namespace gives as well; rename one of the two").Report(context);
                continue;
            }

            // Either pipeline in a shape the generator does not know stops both behaviors: half of a module's
            // messages held and the other half not would look like all of them were.
            if (requests.PipelineProblem is { } unknown)
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.PipelineBehaviorShapeUnknown, requests.Location, requests.Name, unknown, "IPipelineBehavior<,>").Report(context);
            }

            if (requests.StreamPipelineProblem is { } unknownStream)
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.PipelineBehaviorShapeUnknown, requests.Location, requests.Name, unknownStream, "IStreamPipelineBehavior<,>").Report(context);
            }

            if (requests.PipelineProblem is not null || requests.StreamPipelineProblem is not null)
            {
                continue;
            }

            // A project that does not use the library: the interface says which checks a request is held to,
            // and the application asks them itself.
            if (requests.Pipeline is not { } pipeline)
            {
                continue;
            }

            context.AddSource(
                TypeDeclarationInfo.HintNameFor(requests.BehaviorName, requests.FullyQualifiedName, string.Empty),
                SourceText.From(Emit(requests, pipeline, requests.StreamPipeline), Encoding.UTF8));
        }
    }

    private static string Emit(AccessRequestsInterface requests, PipelineShape pipeline, PipelineShape? streams)
    {
        var behavior = requests.BehaviorName;
        var writer = new CodeWriter().Header();
        if (requests.Namespace.Length > 0)
        {
            writer.Line("namespace " + requests.Namespace + ";");
            writer.Line();
        }

        EmitBehavior(writer, requests, pipeline, behavior);
        if (streams is not null)
        {
            writer.Line();
            EmitBehavior(writer, requests, streams, requests.StreamBehaviorName);
        }

        if (requests.CanRegister)
        {
            const string Services = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
            var documented = Documented(behavior, pipeline);

            writer.Line();
            writer.Line("/// <summary>Adds <see cref=\"" + documented + "\"/> to an application.</summary>");
            using (writer.Block(requests.Accessibility + " static class " + behavior + "Registration"))
            {
                writer.Line("/// <summary>");
                writer.Line("/// Puts <see cref=\"" + documented + "\"/> in the pipeline of the messages that implement");
                writer.Line("/// <see cref=\"" + requests.FullyQualifiedName + "\"/>, and registers the set of access checks it asks, both per scope. The");
                writer.Line("/// checks themselves are added with <c>AddAccessCheck</c>, before this call or after it. Behaviors run in the order");
                writer.Line("/// they were registered. Registered once however often it is called.");
                writer.Line("/// </summary>");
                if (streams is not null)
                {
                    writer.Line("/// <remarks>");
                    writer.Line("/// A message that is answered with a stream passes a pipeline of its own, so");
                    writer.Line("/// <see cref=\"" + Documented(requests.StreamBehaviorName, streams) + "\"/> is put in that one, over the same checks.");
                    writer.Line("/// </remarks>");
                }

                writer.Line("/// <param name=\"services\">The application's services.</param>");
                using (writer.Block("public static " + Services + " Add" + behavior + "(this " + Services + " services)"))
                {
                    using (writer.Block("if (services is null)"))
                    {
                        writer.Line("throw new global::System.ArgumentNullException(nameof(services));");
                    }

                    writer.Line();
                    writer.Line("global::" + KnownTypes.AccessCheckRegistration + ".AddAccessChecks<" + requests.FullyQualifiedName + ">(services);");
                    EmitRegistration(writer, requests, pipeline, behavior);
                    if (streams is not null)
                    {
                        EmitRegistration(writer, requests, streams, requests.StreamBehaviorName);
                    }

                    writer.Line();
                    writer.Line("return services;");
                }
            }
        }

        return writer.ToString();
    }

    /// <summary>One behavior: the class that implements <paramref name="pipeline"/> over the checks of the interface.</summary>
    private static void EmitBehavior(CodeWriter writer, AccessRequestsInterface requests, PipelineShape pipeline, string behavior)
    {
        var typeParameters = "<" + pipeline.MessageParameter + ", " + pipeline.ResponseParameter + ">";
        var checks = KnownTypes.AccessChecksUsage + "<" + requests.FullyQualifiedName + ">";

        writer.Line("/// <summary>");
        if (pipeline.Streams)
        {
            writer.Line("/// Holds every message answered with a stream that implements <see cref=\"" + requests.FullyQualifiedName + "\"/> to what it");
            writer.Line("/// declared it requires of its caller, before its handler streams anything: it asks the access checks registered for the");
            writer.Line("/// interface, and runs the next step only when they let the caller through. A message that does not implement the");
            writer.Line("/// interface never passes it.");
        }
        else
        {
            writer.Line("/// Holds every message that implements <see cref=\"" + requests.FullyQualifiedName + "\"/> to what it declared it requires of its caller,");
            writer.Line("/// before its handler runs: it asks the access checks registered for the interface, and runs the next step only");
            writer.Line("/// when they let the caller through. A message that does not implement the interface never passes it.");
        }

        writer.Line("/// </summary>");
        writer.Line("/// <remarks>");
        writer.Line("/// Written by DDDToolkit for the interface marked [AccessRequests]." + (requests.CanRegister
            ? " Add it to the pipeline with <c>services.Add" + requests.BehaviorName + "()</c>."
            : string.Empty));
        writer.Line("/// It keeps nothing itself, so the one instance a scope has serves the messages of that scope that run side by side.");
        writer.Line("/// </remarks>");
        writer.Line(requests.Accessibility + " sealed class " + behavior + typeParameters + " : " + pipeline.Interface + typeParameters);

        // The last constraint clause opens the class: the writer puts the brace under whatever it is handed.
        var onTheMessage = "    where " + pipeline.MessageParameter + " : " + MessageConstraints(requests, pipeline);
        var onTheResponse = pipeline.ResponseConstraints.Length > 0 ? "    where " + pipeline.ResponseParameter + " : " + pipeline.ResponseConstraints : null;
        if (onTheResponse is not null)
        {
            writer.Line(onTheMessage);
        }

        using (writer.Block(onTheResponse ?? onTheMessage))
        {
            writer.Line("private readonly " + checks + " _checks;");
            writer.Line();
            writer.Line("/// <summary>A behavior over the access checks of the interface, which the container makes once per scope.</summary>");
            writer.Line("/// <param name=\"checks\">The checks the messages are held to.</param>");
            using (writer.Block("public " + behavior + "(" + checks + " checks)"))
            {
                writer.Line("_checks = checks ?? throw new global::System.ArgumentNullException(nameof(checks));");
            }

            writer.Line();
            writer.Line("/// <inheritdoc />");
            using (writer.Block("public async " + pipeline.ReturnType + " Handle(" + string.Join(", ", pipeline.Parameters) + ")"))
            {
                // A message that is a value is never null, and the compiler does not take the question.
                foreach (var required in pipeline.MessageIsAValue ? new[] { pipeline.Next } : new[] { pipeline.Message, pipeline.Next })
                {
                    using (writer.Block("if (" + required + " is null)"))
                    {
                        writer.Line("throw new global::System.ArgumentNullException(nameof(" + required + "));");
                    }

                    writer.Line();
                }

                var next = pipeline.Next + "(" + (pipeline.NextTakesTheMessageFirst
                    ? pipeline.Message + ", " + pipeline.CancellationToken
                    : pipeline.CancellationToken + ", " + pipeline.Message) + ")";

                writer.Line("await _checks.RequireAsync(" + pipeline.Message + ", " + pipeline.CancellationToken + ").ConfigureAwait(false);");
                if (pipeline.Streams)
                {
                    // Nothing of the stream is asked for until the checks let the caller through: the next step
                    // is not even called before. ConfigureAwait is an extension here, named in full because the
                    // file has no using.
                    using (writer.Block("await foreach (var " + pipeline.StreamItem + " in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(" + next + ", false))"))
                    {
                        writer.Line("yield return " + pipeline.StreamItem + ";");
                    }
                }
                else
                {
                    writer.Line("return await " + next + ".ConfigureAwait(false);");
                }
            }
        }
    }

    /// <summary>The line that puts one behavior in its pipeline, once.</summary>
    private static void EmitRegistration(CodeWriter writer, AccessRequestsInterface requests, PipelineShape pipeline, string behavior)
    {
        var qualified = "global::" + (requests.Namespace.Length > 0 ? requests.Namespace + "." : string.Empty) + behavior;

        writer.Line("global::" + KnownTypes.ServiceCollectionDescriptorExtensions + ".TryAddEnumerable(");
        writer.Line("    services,");
        writer.Line("    global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(" + pipeline.Interface + "<,>), typeof(" + qualified + "<,>)));");
    }

    /// <summary>A behavior as a documentation comment names it.</summary>
    private static string Documented(string behavior, PipelineShape pipeline)
        => behavior + "{" + pipeline.MessageParameter + ", " + pipeline.ResponseParameter + "}";

    /// <summary>
    /// What the behavior asks of a message: what the library asks, and the module's interface. In the order a
    /// constraint clause takes them: the kind of type, a base class, the interfaces, a constructor.
    /// </summary>
    private static string MessageConstraints(AccessRequestsInterface requests, PipelineShape pipeline)
    {
        var constraints = new List<string>();
        if (pipeline.MessageKind.Length > 0)
        {
            constraints.Add(pipeline.MessageKind);
        }

        constraints.AddRange(pipeline.MessageBaseTypes);
        constraints.Add(requests.FullyQualifiedName);
        constraints.AddRange(pipeline.MessageInterfaces);
        if (pipeline.MessageHasConstructor)
        {
            constraints.Add("new()");
        }

        return string.Join(", ", constraints);
    }
}

/// <summary>An interface marked <c>[AccessRequests]</c>, with what the project it is declared in lets be written for it.</summary>
/// <param name="Name">The interface's name, as its author wrote it.</param>
/// <param name="Namespace">Its namespace, empty for the global one.</param>
/// <param name="FullyQualifiedName">Its name from <c>global::</c> on.</param>
/// <param name="Accessibility">How visible it is, which is how visible what is written for it is.</param>
/// <param name="BehaviorName">The name of its behavior.</param>
/// <param name="StreamBehaviorName">The name of its behavior for the messages that are answered with a stream.</param>
/// <param name="Problem">Why no behavior can be written for it (DDD00056), or <see langword="null"/>.</param>
/// <param name="Pipeline">How the library's behavior is implemented, or <see langword="null"/> when the project does not use the library.</param>
/// <param name="PipelineProblem">How the library's behavior differs from what the generator knows (DDD00057), or <see langword="null"/>.</param>
/// <param name="StreamPipeline">How the library's behavior for streams is implemented, or <see langword="null"/> when the library has none.</param>
/// <param name="StreamPipelineProblem">How the library's behavior for streams differs from what the generator knows (DDD00057), or <see langword="null"/>.</param>
/// <param name="CanRegister">Whether the project can see what the registration is written with.</param>
/// <param name="Location">Where the interface is named.</param>
internal sealed record AccessRequestsInterface(
    string Name,
    string Namespace,
    string FullyQualifiedName,
    string Accessibility,
    string BehaviorName,
    string StreamBehaviorName,
    string? Problem,
    PipelineShape? Pipeline,
    string? PipelineProblem,
    PipelineShape? StreamPipeline,
    string? StreamPipelineProblem,
    bool CanRegister,
    LocationInfo? Location)
{
    public static AccessRequestsInterface Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (InterfaceDeclarationSyntax)context.TargetNode;
        var compilation = context.SemanticModel.Compilation;

        cancellationToken.ThrowIfCancellationRequested();
        var (pipeline, pipelineProblem) = PipelineShape.Read(compilation, KnownTypes.MediatorPipelineBehavior, streams: false);

        // The pipeline of the messages answered with a stream belongs to the same library: without the first
        // the project does not use the library, whatever else is called the same.
        var (streamPipeline, streamPipelineProblem) = pipeline is null && pipelineProblem is null
            ? (null, null)
            : PipelineShape.Read(compilation, KnownTypes.MediatorStreamPipelineBehavior, streams: true);
        var namedAfter = NamedAfter(symbol.Name);

        return new AccessRequestsInterface(
            Name: symbol.Name,
            Namespace: symbol.ContainingNamespace is { IsGlobalNamespace: false } declared ? declared.ToDisplayString() : string.Empty,
            FullyQualifiedName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Accessibility: symbol.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public ? "public" : "internal",
            BehaviorName: namedAfter + "AccessBehavior",
            StreamBehaviorName: namedAfter + "AccessStreamBehavior",
            Problem: ProblemWith(symbol, compilation),
            Pipeline: pipeline,
            PipelineProblem: pipelineProblem,
            StreamPipeline: streamPipeline,
            StreamPipelineProblem: streamPipelineProblem,
            CanRegister: compilation.GetTypeByMetadataName(KnownTypes.ServiceCollection) is not null
                && compilation.GetTypeByMetadataName(KnownTypes.ServiceCollectionDescriptorExtensions) is not null
                && compilation.GetTypeByMetadataName(KnownTypes.AccessCheckRegistration) is { } registration
                && registration.GetMembers("AddAccessChecks").Length > 0,
            Location: LocationInfo.From(syntax.Identifier));
    }

    /// <summary>
    /// What the behaviors are named after: the interface's name without the <c>I</c> an interface starts with
    /// and without the <c>Request</c> or <c>Requests</c> a request interface ends in, so <c>IBillingRequest</c>
    /// gives <c>BillingAccessBehavior</c> and <c>BillingAccessStreamBehavior</c>. An interface named for
    /// nothing else, <c>IRequest</c>, keeps that word. The two endings differ in their last word but one, so
    /// two interfaces give each other's names only where they are named after the same.
    /// </summary>
    private static string NamedAfter(string name)
    {
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        foreach (var ending in new[] { "Requests", "Request" })
        {
            if (name.Length > ending.Length && name.EndsWith(ending, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - ending.Length);
                break;
            }
        }

        return name;
    }

    /// <summary>Why no behavior can be written for the interface as it is declared, or <see langword="null"/>.</summary>
    private static string? ProblemWith(INamedTypeSymbol symbol, Compilation compilation)
    {
        if (symbol.ContainingType is { } outer)
        {
            return "is nested in '" + outer.Name + "'; declare it in a namespace, where its behavior is written beside it";
        }

        if (symbol.IsFileLocal)
        {
            return "is file-local; its behavior is written in a file of its own and has to name it";
        }

        if (symbol.TypeParameters.Length > 0)
        {
            return "has type parameters; its behavior and the module's set of checks are closed over the interface, so declare it without";
        }

        var requireAccess = compilation.GetTypeByMetadataName(KnownTypes.RequireAccessInterface);
        if (requireAccess is null || !symbol.AllInterfaces.Contains(requireAccess, SymbolEqualityComparer.Default))
        {
            return "does not derive from IRequireAccess, which is where a request's requirement is read from; declare it as 'interface " + symbol.Name + " : IRequireAccess'";
        }

        return null;
    }
}

/// <summary>
/// How one of the Mediator library's behaviors is implemented, read from the version the project references:
/// <c>IPipelineBehavior&lt;TMessage, TResponse&gt;</c>, or <c>IStreamPipelineBehavior&lt;TMessage, TResponse&gt;</c>
/// for the messages that are answered with a stream.
/// </summary>
/// <param name="Interface">The interface's name from <c>global::</c> on, without its type parameters.</param>
/// <param name="MessageParameter">What the library calls the message's type.</param>
/// <param name="ResponseParameter">What the library calls the response's type.</param>
/// <param name="MessageKind">The kind of type a message is to be (<c>notnull</c>, <c>class</c>, <c>struct</c>), or empty.</param>
/// <param name="MessageBaseTypes">The class a message derives from, where the library asks for one.</param>
/// <param name="MessageInterfaces">The interfaces a message implements: the library's message type.</param>
/// <param name="MessageIsAValue">Whether a message is constrained to a value type, which is never null.</param>
/// <param name="MessageHasConstructor">Whether a message is to have a public parameterless constructor.</param>
/// <param name="ResponseConstraints">What the library asks of a response, as the entries of a constraint clause; empty for nothing.</param>
/// <param name="ReturnType">What <c>Handle</c> answers.</param>
/// <param name="Parameters">The parameters of <c>Handle</c>, each with its type, in the library's order.</param>
/// <param name="Message">The name of the message parameter.</param>
/// <param name="Next">The name of the parameter that runs the next step.</param>
/// <param name="CancellationToken">The name of the cancellation token parameter.</param>
/// <param name="NextTakesTheMessageFirst">Whether the next step takes the message before the token.</param>
/// <param name="Streams">Whether <c>Handle</c> answers a stream of responses, and not a task of one.</param>
/// <param name="StreamItem">A name for one response of the stream that no parameter of <c>Handle</c> has.</param>
internal sealed record PipelineShape(
    string Interface,
    string MessageParameter,
    string ResponseParameter,
    string MessageKind,
    EquatableArray<string> MessageBaseTypes,
    EquatableArray<string> MessageInterfaces,
    bool MessageIsAValue,
    bool MessageHasConstructor,
    string ResponseConstraints,
    string ReturnType,
    EquatableArray<string> Parameters,
    string Message,
    string Next,
    string CancellationToken,
    bool NextTakesTheMessageFirst,
    bool Streams,
    string StreamItem)
{
    private static readonly SymbolDisplayFormat WithNullability =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// The shape of the library's behavior named <paramref name="metadataName"/> in
    /// <paramref name="compilation"/>: none and no problem when the project cannot see it, and a problem when
    /// the library declares the interface otherwise than the generator knows it.
    /// </summary>
    /// <param name="compilation">The project.</param>
    /// <param name="metadataName">The behavior's interface, by its metadata name.</param>
    /// <param name="streams">
    /// Whether the behavior is the one for messages answered with a stream, whose <c>Handle</c> answers an
    /// <c>IAsyncEnumerable</c> of the response.
    /// </param>
    public static (PipelineShape? Shape, string? Problem) Read(Compilation compilation, string metadataName, bool streams)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is not { } behavior)
        {
            return (null, null);
        }

        if (behavior.TypeKind != TypeKind.Interface || behavior.TypeParameters.Length != 2)
        {
            return (null, "not as an interface over a message and a response");
        }

        var message = behavior.TypeParameters[0];
        var response = behavior.TypeParameters[1];

        // Everything a class that implements the interface has to write: the interface's own, and what it derives from.
        var toImplement = behavior.AllInterfaces.Concat(new[] { behavior })
            .SelectMany(static each => each.GetMembers())
            .Where(static member => member.IsAbstract)
            .ToList();
        if (toImplement.Count != 1
            || toImplement[0] is not IMethodSymbol { Name: "Handle", MethodKind: MethodKind.Ordinary, IsGenericMethod: false, IsStatic: false } handle)
        {
            return (null, "with more to implement than the one method Handle");
        }

        var token = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        var parameters = handle.Parameters;
        var messageAt = IndexOfOnly(parameters, parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, message));
        var tokenAt = IndexOfOnly(parameters, parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, token));
        var nextAt = IndexOfOnly(parameters, static parameter => parameter.Type.TypeKind == TypeKind.Delegate);
        if (parameters.Length != 3 || messageAt < 0 || tokenAt < 0 || nextAt < 0 || parameters.Any(static parameter => parameter.RefKind != RefKind.None))
        {
            return (null, "with a Handle that does not take the message, a cancellation token and the delegate that runs the next step, each once");
        }

        var next = ((INamedTypeSymbol)parameters[nextAt].Type).DelegateInvokeMethod;
        var messageOfNextAt = next is null ? -1 : IndexOfOnly(next.Parameters, parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, message));
        if (next is null
            || next.Parameters.Length != 2
            || next.Parameters.Any(static parameter => parameter.RefKind != RefKind.None)
            || messageOfNextAt < 0
            || IndexOfOnly(next.Parameters, parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, token)) < 0
            || !SymbolEqualityComparer.Default.Equals(next.ReturnType, handle.ReturnType))
        {
            return (null, "with a next step that does not take the message and a cancellation token and answer what Handle answers");
        }

        // What the generated Handle awaits and answers: a task of the response, of either kind; or, for a
        // stream, what an async iterator answers.
        var known = streams
            ? new[] { "System.Collections.Generic.IAsyncEnumerable`1" }
            : new[] { "System.Threading.Tasks.ValueTask`1", "System.Threading.Tasks.Task`1" };
        if (handle.ReturnType is not INamedTypeSymbol { TypeArguments.Length: 1 } answered
            || !SymbolEqualityComparer.Default.Equals(answered.TypeArguments[0], response)
            || !known.Any(name => SymbolEqualityComparer.Default.Equals(answered.OriginalDefinition, compilation.GetTypeByMetadataName(name))))
        {
            return (null, streams
                ? "with a Handle that does not answer an IAsyncEnumerable of the response"
                : "with a Handle that does not answer a ValueTask or a Task of the response");
        }

        // An async iterator's token is marked as the one an enumeration is cancelled with, where the project
        // can see the attribute: it comes with IAsyncEnumerable itself.
        var tokenOfTheStream = streams && compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.EnumeratorCancellationAttribute") is not null
            ? "[global::System.Runtime.CompilerServices.EnumeratorCancellation] "
            : string.Empty;
        var taken = parameters.Select(static parameter => parameter.Name).ToList();

        var constraints = message.ConstraintTypes;
        return (
            new PipelineShape(
                Interface: behavior.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None)),
                MessageParameter: message.Name,
                ResponseParameter: response.Name,
                MessageKind: KindOf(message),
                MessageBaseTypes: constraints.Where(static type => type.TypeKind != TypeKind.Interface && type.TypeKind != TypeKind.TypeParameter)
                    .Select(static type => type.ToDisplayString(WithNullability)).ToEquatableArray(),
                MessageInterfaces: constraints.Where(static type => type.TypeKind == TypeKind.Interface || type.TypeKind == TypeKind.TypeParameter)
                    .Select(static type => type.ToDisplayString(WithNullability)).ToEquatableArray(),
                MessageIsAValue: message.HasValueTypeConstraint || message.HasUnmanagedTypeConstraint,
                MessageHasConstructor: message.HasConstructorConstraint && !message.HasValueTypeConstraint,
                ResponseConstraints: ConstraintsOf(response),
                ReturnType: handle.ReturnType.ToDisplayString(WithNullability),
                Parameters: parameters.Select((parameter, index) =>
                    (index == tokenAt ? tokenOfTheStream : string.Empty) + parameter.Type.ToDisplayString(WithNullability) + " " + Identifier(parameter.Name)).ToEquatableArray(),
                Message: Identifier(parameters[messageAt].Name),
                Next: Identifier(parameters[nextAt].Name),
                CancellationToken: Identifier(parameters[tokenAt].Name),
                NextTakesTheMessageFirst: messageOfNextAt == 0,
                Streams: streams,
                StreamItem: new[] { "item", "streamed", "streamedItem", "oneOfTheStream" }.First(name => !taken.Contains(name))),
            null);
    }

    /// <summary>The position of the one parameter that <paramref name="matches"/>, or -1 when there is none or more than one.</summary>
    private static int IndexOfOnly(ImmutableArray<IParameterSymbol> parameters, Func<IParameterSymbol, bool> matches)
    {
        var found = -1;
        for (var index = 0; index < parameters.Length; index++)
        {
            if (!matches(parameters[index]))
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = index;
        }

        return found;
    }

    /// <summary>The kind of type a type parameter is constrained to, as a constraint clause writes it first; empty for none.</summary>
    private static string KindOf(ITypeParameterSymbol parameter)
        => parameter.HasUnmanagedTypeConstraint ? "unmanaged"
            : parameter.HasValueTypeConstraint ? "struct"
            : parameter.HasReferenceTypeConstraint ? (parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class")
            : parameter.HasNotNullConstraint ? "notnull"
            : string.Empty;

    /// <summary>Everything a type parameter is constrained to, as the entries of a constraint clause; empty for nothing.</summary>
    private static string ConstraintsOf(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();
        if (KindOf(parameter) is { Length: > 0 } kind)
        {
            constraints.Add(kind);
        }

        constraints.AddRange(parameter.ConstraintTypes.Select(static type => type.ToDisplayString(WithNullability)));
        if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint)
        {
            constraints.Add("new()");
        }

        return string.Join(", ", constraints);
    }

    /// <summary>A parameter's name as code writes it: with an <c>@</c> when it is a keyword.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
