using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Analyzers.Analyzers;

/// <summary>
/// Reports a message that says what it requires of its caller and reaches its handlers with nothing asking:
/// DDD00058, on a type that implements an interface marked <c>[AccessRequests]</c> and is a notification of
/// the Mediator library.
/// <para>
/// The behavior the generator writes for such an interface is part of the library's pipeline for commands and
/// queries, and of its pipeline for the messages answered with a stream. A notification is published to its
/// handlers through neither. So a notification that implements the interface looks held to what it declares,
/// and is not: every handler of it runs for whoever published it.
/// </para>
/// <para>
/// Silent in a project that does not use the library: it registers nothing where the library's notification
/// type cannot be seen, as the generator writes nothing there.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AccessRequestsAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.NotificationRequiresAccess];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var notification = start.Compilation.GetTypeByMetadataName(KnownTypes.MediatorNotification);
            var marker = start.Compilation.GetTypeByMetadataName(KnownTypes.AccessRequestsAttribute);
            if (notification is null || marker is null)
            {
                return;
            }

            start.RegisterSymbolAction(symbol => Analyze(symbol, notification, marker), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol notification, INamedTypeSymbol marker)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface)
            || Requests(type, marker) is not { } requests
            || !IsA(type, notification))
        {
            return;
        }

        // Said once, where the two are joined: a type that takes both from one type of this project, the class
        // it derives from or an interface it lists, is not reported again. One that takes them from a type of
        // another assembly is: nothing here would say it otherwise.
        bool Joined(INamedTypeSymbol from)
            => IsA(from, notification) && Requests(from, marker) is not null && from.Locations.Any(static location => location.IsInSource);

        if ((type.BaseType is { } inherited && Joined(inherited)) || type.Interfaces.Any(Joined))
        {
            return;
        }

        // Once for the type, however many parts a partial one has: at the part that lists what it implements, where
        // the two are joined, or at its first part when no part of this project lists anything.
        var listing = type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(context.CancellationToken))
            .OfType<BaseTypeDeclarationSyntax>()
            .FirstOrDefault(static declaration => declaration.BaseList is not null);
        if ((listing?.Identifier.GetLocation() ?? type.Locations.FirstOrDefault(static location => location.IsInSource)) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NotificationRequiresAccess, location, type.Name, requests.Name));
        }
    }

    /// <summary>The interface marked <c>[AccessRequests]</c> that <paramref name="type"/> is or implements, or <see langword="null"/>.</summary>
    private static INamedTypeSymbol? Requests(INamedTypeSymbol type, INamedTypeSymbol marker)
        => IsMarked(type, marker) ? type : type.AllInterfaces.FirstOrDefault(implemented => IsMarked(implemented, marker));

    private static bool IsMarked(INamedTypeSymbol type, INamedTypeSymbol marker)
        => type.TypeKind == TypeKind.Interface
           && type.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));

    private static bool IsA(INamedTypeSymbol type, INamedTypeSymbol notification)
        => SymbolEqualityComparer.Default.Equals(type, notification) || type.AllInterfaces.Contains(notification, SymbolEqualityComparer.Default);
}
