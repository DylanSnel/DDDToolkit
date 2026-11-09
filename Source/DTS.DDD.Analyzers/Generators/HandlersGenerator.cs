using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DTS.DDD.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DTS.DDD.Analyzers;

/// <summary>
/// Writes a project's handler registration for the toolkit's mediator: one <c>Add{Module}Handlers()</c> with a call
/// for each handler and each message type it handles. A notification handler gets
/// <c>AddNotificationHandler&lt;TNotification, THandler&gt;()</c>, and a handler of a request, a command or a query,
/// or of one of their stream kinds, gets the registration of its kind, such as
/// <c>AddCommandHandler&lt;TCommand, TResponse, THandler&gt;()</c>.
/// <para>
/// The generics are closed here, when the project compiles, so the mediator calls each handler without reflection.
/// A closed generic notification of a package, such as <c>SeatAdded&lt;TenantId, SeatId&gt;</c>, is closed by the
/// handler that names it, so it is registered like a notification of the project's own.
/// </para>
/// <para>
/// A request, a command or a query has one handler. A second handler of the same message in this project is
/// DDD00110, reported at the second handler by file and position, and the registration keeps the first.
/// </para>
/// <para>
/// A handler may be internal, because the registration is written into its own project. A handler the registration
/// cannot name is left out: a private or protected nested class, a class nested in a generic type, a file-local
/// class, and an open generic class. The method calls the registrations of the module's other projects that this
/// project references, so one call registers the whole module. A project without handlers of its own gets no method.
/// </para>
/// <para>
/// The class is written into the assembly's own namespace, as <c>{Module}EventNames</c> is, so the module's code calls
/// the method without a using. Code outside the module imports the namespace of the module's outermost project with
/// handlers. Importing the namespaces of two of the module's projects with handlers makes the call ambiguous.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HandlersGenerator : IIncrementalGenerator
{
    /// <summary>The generated class, in the assembly's namespace.</summary>
    internal const string RegistrationClass = "HandlerExtensions";

    /// <summary>What the generated method's name ends in.</summary>
    internal const string RegistrationSuffix = "Handlers";

    private const string MediatorNamespace = "DTS.DDD.Mediator";
    private const string RegistrationsMetadataName = MediatorNamespace + ".MediatorServiceCollectionExtensions";
    private const string NotificationHandlerName = "INotificationHandler";
    private const string NotificationRegistration = "AddNotificationHandler";
    private const string ServiceCollection = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";

    /// <summary>Each handler interface of a message answered once or with a stream, with the registration it gets.</summary>
    private static readonly Dictionary<string, (string Registration, bool Streamed)> RequestHandlers = new(StringComparer.Ordinal)
    {
        ["IRequestHandler"] = ("AddRequestHandler", false),
        ["ICommandHandler"] = ("AddCommandHandler", false),
        ["IQueryHandler"] = ("AddQueryHandler", false),
        ["IStreamRequestHandler"] = ("AddStreamRequestHandler", true),
        ["IStreamCommandHandler"] = ("AddStreamCommandHandler", true),
        ["IStreamQueryHandler"] = ("AddStreamQueryHandler", true),
    };

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The registration calls the core's registrations, so a project without the core writes nothing.
        var enabled = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(RegistrationsMetadataName) is not null);

        var handled = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                transform: static (syntaxContext, cancellationToken) => Describe(syntaxContext, cancellationToken))
            .Where(static pairs => pairs.Count > 0)
            .Collect();

        var registration = handled
            .Combine(enabled)
            .Combine(context.RegistrationName())
            .Combine(context.AssemblyName())
            .Combine(context.RegistrationsOfTheSameModule(RegistrationClass, RegistrationSuffix));

        context.RegisterSourceOutput(registration, static (production, data) =>
        {
            var ((((found, isEnabled), moduleName), assemblyName), sameModule) = data;

            if (!isEnabled)
            {
                return;
            }

            // A partial class is found once per declaration with a base list, so the pairs are made distinct here.
            var pairs = found
                .SelectMany(static declaration => declaration)
                .Distinct()
                .ToList();

            var kept = KeepOneHandlerPerRequest(pairs, production);
            if (kept.Count == 0)
            {
                return;
            }

            production.AddSource(
                "HandlerExtensions.g.cs",
                SourceText.From(Write(kept, moduleName, assemblyName, sameModule), Encoding.UTF8));
        });
    }

    /// <summary>
    /// Every notification pair, and for each request, command or query the first of its handlers by file and
    /// position. Each later handler of the same message is reported as DDD00110. The result is ordered by handler
    /// and message, which is the order the registration lists them in.
    /// </summary>
    private static List<Handled> KeepOneHandlerPerRequest(List<Handled> pairs, SourceProductionContext production)
    {
        var kept = pairs.Where(static pair => pair.Response is null).ToList();

        foreach (var sameMessage in pairs.Where(static pair => pair.Response is not null).GroupBy(static pair => (pair.Message, pair.Streamed)))
        {
            var byPlace = sameMessage
                .GroupBy(static pair => pair.Handler)
                .Select(static handler => handler.First())
                .OrderBy(static pair => pair.Location?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Location?.TextSpan.Start ?? 0)
                .ThenBy(static pair => pair.Handler, StringComparer.Ordinal)
                .ToList();

            var first = byPlace[0];
            kept.AddRange(sameMessage.Where(pair => pair.Handler == first.Handler));

            foreach (var second in byPlace.Skip(1))
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.SecondRequestHandler, second.Location, second.HandlerName, second.MessageName, first.HandlerName)
                    .Report(production);
            }
        }

        return kept
            .Distinct()
            .OrderBy(static pair => pair.Handler, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Message, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Registration, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The message types the declared class handles, or none when the registration cannot name it.</summary>
    private static EquatableArray<Handled> Describe(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        if (syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node, cancellationToken) is not INamedTypeSymbol type
            || type.TypeKind != TypeKind.Class
            || type.IsAbstract
            || type.IsStatic
            || !CanBeNamed(type))
        {
            return EquatableArray<Handled>.Empty;
        }

        var handler = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var handlerName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        // The symbol's first location, which every declaration of a partial class shares, so the pairs of one handler
        // stay equal across its declarations.
        var location = LocationInfo.From(type);
        var pairs = new List<Handled>();

        foreach (var implemented in type.AllInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (implemented.ContainingNamespace?.ToDisplayString() != MediatorNamespace
                || implemented.TypeArguments.Any(ContainsTypeParameter))
            {
                continue;
            }

            if (implemented is { Name: NotificationHandlerName, Arity: 1 })
            {
                pairs.Add(new Handled(
                    handler,
                    handlerName,
                    NotificationRegistration,
                    implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    Response: null,
                    Streamed: false,
                    location));
            }
            else if (implemented.Arity == 2 && RequestHandlers.TryGetValue(implemented.Name, out var kind))
            {
                pairs.Add(new Handled(
                    handler,
                    handlerName,
                    kind.Registration,
                    implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    implemented.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    kind.Streamed,
                    location));
            }
        }

        return pairs.Count == 0 ? EquatableArray<Handled>.Empty : new EquatableArray<Handled>(pairs.ToArray());
    }

    /// <summary>
    /// Whether a top-level class of the same project can name <paramref name="type"/>: it is no open generic, and
    /// neither it nor a type it is nested in is generic, file-local, private or protected.
    /// </summary>
    private static bool CanBeNamed(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType || current.IsFileLocal)
            {
                return false;
            }

            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
        _ => false,
    };

    private static string Write(IReadOnlyList<Handled> pairs, string moduleName, string? assemblyName, EquatableArray<string> sameModule)
    {
        var writer = new CodeWriter().Header();

        writer.Line("namespace " + Identifiers.NamespaceFrom(assemblyName) + ";");
        writer.Line();
        writer.Line("/// <summary>Registers the handlers of this assembly with the toolkit's mediator.</summary>");
        using (writer.Block("public static class HandlerExtensions"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// Registers every handler of this assembly: each handler, scoped, for each message type it handles. The");
            writer.Line("/// toolkit's <c>IPublisher</c> then hands each published notification to its handlers, and its <c>ISender</c>");
            writer.Line("/// hands each request, command and query to its one handler.");
            if (sameModule.Count > 0)
            {
                writer.Line("/// It also calls the registrations of the module's other assemblies this one references, so one call covers the module.");
            }

            writer.Line("/// </summary>");
            using (writer.Block("public static " + ServiceCollection + " Add" + moduleName + RegistrationSuffix + "(this " + ServiceCollection + " services)"))
            {
                foreach (var call in sameModule)
                {
                    writer.Line(call + "(services);");
                }

                foreach (var pair in pairs)
                {
                    var types = pair.Response is null
                        ? pair.Message + ", " + pair.Handler
                        : pair.Message + ", " + pair.Response + ", " + pair.Handler;
                    writer.Line("global::" + RegistrationsMetadataName + "." + pair.Registration + "<" + types + ">(services);");
                }

                writer.Line("return services;");
            }
        }

        return writer.ToString();
    }

    /// <summary>One handler of one message type, with the registration it gets.</summary>
    /// <param name="Handler">The handler, fully qualified.</param>
    /// <param name="HandlerName">The handler as a diagnostic names it.</param>
    /// <param name="Registration">The registration method, such as <c>AddCommandHandler</c>.</param>
    /// <param name="Message">The message, fully qualified.</param>
    /// <param name="MessageName">The message as a diagnostic names it.</param>
    /// <param name="Response">The response, fully qualified, or null for a notification.</param>
    /// <param name="Streamed">Whether the handler answers with a stream.</param>
    /// <param name="Location">Where the handler is declared.</param>
    private sealed record Handled(
        string Handler,
        string HandlerName,
        string Registration,
        string Message,
        string MessageName,
        string? Response,
        bool Streamed,
        LocationInfo? Location);
}
