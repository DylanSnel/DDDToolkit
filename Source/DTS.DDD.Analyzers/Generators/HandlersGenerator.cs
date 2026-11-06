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
/// Writes a project's handler registration for the toolkit's mediator: one <c>Add{Module}Handlers()</c> with a
/// call to <c>AddNotificationHandler&lt;TNotification, THandler&gt;()</c> for each handler and each notification type
/// it implements <c>INotificationHandler&lt;TNotification&gt;</c> for.
/// <para>
/// The generics are closed here, when the project compiles, so the mediator calls each handler without reflection.
/// A closed generic notification of a package, such as <c>SeatAdded&lt;TenantId, SeatId&gt;</c>, is closed by the
/// handler that names it, so it is registered like a notification of the project's own.
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
    private const string ServiceCollection = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The registration calls the core's AddNotificationHandler, so a project without the core writes nothing.
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
                .OrderBy(static pair => pair.Handler, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Notification, StringComparer.Ordinal)
                .ToList();

            if (pairs.Count == 0)
            {
                return;
            }

            production.AddSource(
                "HandlerExtensions.g.cs",
                SourceText.From(Write(pairs, moduleName, assemblyName, sameModule), Encoding.UTF8));
        });
    }

    /// <summary>The notification types the declared class handles, or none when the registration cannot name it.</summary>
    private static EquatableArray<HandledNotification> Describe(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        if (syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node, cancellationToken) is not INamedTypeSymbol type
            || type.TypeKind != TypeKind.Class
            || type.IsAbstract
            || type.IsStatic
            || !CanBeNamed(type))
        {
            return EquatableArray<HandledNotification>.Empty;
        }

        var handler = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var pairs = new List<HandledNotification>();

        foreach (var implemented in type.AllInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsNotificationHandler(implemented)
                && implemented.TypeArguments[0] is var notification
                && !ContainsTypeParameter(notification))
            {
                pairs.Add(new HandledNotification(handler, notification.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        return pairs.Count == 0 ? EquatableArray<HandledNotification>.Empty : new EquatableArray<HandledNotification>(pairs.ToArray());
    }

    private static bool IsNotificationHandler(INamedTypeSymbol type)
        => type is { Name: NotificationHandlerName, Arity: 1 }
            && type.ContainingNamespace?.ToDisplayString() == MediatorNamespace;

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

    private static string Write(IReadOnlyList<HandledNotification> pairs, string moduleName, string? assemblyName, EquatableArray<string> sameModule)
    {
        var writer = new CodeWriter().Header();

        writer.Line("namespace " + Identifiers.NamespaceFrom(assemblyName) + ";");
        writer.Line();
        writer.Line("/// <summary>Registers the notification handlers of this assembly with the toolkit's mediator.</summary>");
        using (writer.Block("public static class HandlerExtensions"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// Registers every notification handler of this assembly: each handler, scoped, for each notification type it");
            writer.Line("/// handles. The toolkit's <c>IPublisher</c> then hands each published notification to its handlers.");
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
                    writer.Line("global::" + RegistrationsMetadataName + ".AddNotificationHandler<" + pair.Notification + ", " + pair.Handler + ">(services);");
                }

                writer.Line("return services;");
            }
        }

        return writer.ToString();
    }

    /// <summary>One handler of one notification type, both fully qualified.</summary>
    private sealed record HandledNotification(string Handler, string Notification);
}
