using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// How an entity's <c>IInvariant&lt;T&gt;</c> rules are found, and the three things that can be wrong
/// with one that is in the right place: DDD00025 (about another type), DDD00026 (a code another rule
/// already uses) and DDD00027 (nothing the generator can create).
/// <para>
/// Discovery is deliberately nothing more than <see cref="INamedTypeSymbol.GetTypeMembers"/> on the
/// entity the generator is already holding. That is what nesting buys: no scan over the compilation,
/// no registry to keep in step, and a rule that can read the private state it is about. The one thing
/// nesting cannot do is notice a rule written somewhere else, which is why
/// <see cref="DiagnosticDescriptors.InvariantMustBeNestedInItsSubject"/> is reported by an analyzer
/// instead.
/// </para>
/// <para>
/// Like <see cref="AggregateBoundary"/>, this reads members of types the generator did not start from:
/// a rule normally lives in its own file, as another part of the entity. The same trade applies - the
/// answer can go stale in an IDE session until the entity's file is touched again, and it is recomputed
/// on every real build.
/// </para>
/// </summary>
internal static class Invariants
{
    /// <summary>
    /// The rules <paramref name="entity"/> states, as fully qualified type names for the generated
    /// array, with a diagnostic for every nested rule that cannot be one of them.
    /// </summary>
    /// <param name="entity">The type carrying <c>[Entity&lt;T&gt;]</c> or <c>[AggregateRoot&lt;T&gt;]</c>.</param>
    /// <param name="compilation">Used to resolve constants and to answer what the entity can reach.</param>
    public static EquatableArray<string> Collect(
        INamedTypeSymbol entity,
        Compilation compilation,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        List<string>? rules = null;
        Dictionary<string, string>? codes = null;

        foreach (var nested in entity.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subject = SubjectOf(nested);
            if (subject is null || !CanBeARule(nested))
            {
                continue;
            }

            if (!IsAbout(entity, nested))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvariantIsAboutAnotherType,
                    LocationInfo.From(nested),
                    nested.Name,
                    entity.Name,
                    subject.Name));
                continue;
            }

            if (!CanBeCreatedBy(compilation, nested, entity))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvariantNeedsAParameterlessConstructor,
                    LocationInfo.From(nested),
                    nested.Name,
                    entity.Name));
                continue;
            }

            var code = CodeOf(nested, compilation, cancellationToken);
            if (code is not null)
            {
                codes ??= new Dictionary<string, string>(StringComparer.Ordinal);
                if (codes.TryGetValue(code, out var claimedBy))
                {
                    // Reported, but still emitted: the rule runs and the entity is the poorer for the
                    // shared code, which is a design problem rather than something that cannot compile.
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.InvariantCodeMustBeUnique,
                        LocationInfo.From(nested),
                        nested.Name,
                        code,
                        claimedBy));
                }
                else
                {
                    codes.Add(code, nested.Name);
                }
            }

            (rules ??= []).Add(nested.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return rules is null ? EquatableArray<string>.Empty : rules.ToEquatableArray();
    }

    /// <summary>
    /// The first <c>T</c> of the <c>IInvariant&lt;T&gt;</c> interfaces this type implements, or null when
    /// it implements none. Inherited implementations count: a rule may take its <c>Check</c> from a base
    /// class the author shares between several rules.
    /// </summary>
    public static ITypeSymbol? SubjectOf(INamedTypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (IsTheInvariantInterface(@interface))
            {
                return @interface.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this rule is one <paramref name="entity"/> can be checked with. Usually the type argument
    /// is the entity itself; a rule about a base type or an interface of the entity also applies, because
    /// <c>IInvariant&lt;T&gt;</c> is contravariant in <c>T</c> and the generated array accepts it.
    /// </summary>
    public static bool IsAbout(INamedTypeSymbol entity, INamedTypeSymbol rule)
    {
        foreach (var @interface in rule.AllInterfaces)
        {
            if (IsTheInvariantInterface(@interface) && Accepts(entity, @interface.TypeArguments[0]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a type is the kind of thing a rule can be at all. An abstract type and an open generic one
    /// are skipped without a word: they are the shapes an author writes to share code between rules, not
    /// rules themselves, and reporting them would be an analyzer arguing with a base class.
    /// </summary>
    public static bool CanBeARule(INamedTypeSymbol type)
        => !type.IsAbstract && type.TypeKind != TypeKind.Interface && type.TypeParameters.Length == 0;

    /// <summary>
    /// Whether the generated code, which is written inside <paramref name="entity"/>, can write
    /// <c>new TheRule()</c>. Accessibility is asked of the compiler rather than read off the modifiers:
    /// a rule nested in the entity may be private and still reachable, while a private constructor on it
    /// is not, and only the real rules get that pair right.
    /// </summary>
    public static bool CanBeCreatedBy(Compilation compilation, INamedTypeSymbol rule, INamedTypeSymbol entity)
    {
        foreach (var constructor in rule.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 && compilation.IsSymbolAccessibleWithin(constructor, entity))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The value of the rule's <c>Code</c> property when it is a constant, and null when it is anything
    /// else. Only <c>=&gt; "literal"</c>, <c>{ get; } = "literal"</c> and a reference to a constant can be
    /// read here; a code built at run time is left alone rather than guessed at, because a wrong guess
    /// would report two rules as sharing a code they do not share.
    /// </summary>
    public static string? CodeOf(INamedTypeSymbol rule, Compilation compilation, CancellationToken cancellationToken)
    {
        var property = PropertyNamedCode(rule);
        if (property is null)
        {
            return null;
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax syntax)
            {
                continue;
            }

            var expression = syntax.ExpressionBody?.Expression ?? syntax.Initializer?.Value;
            if (expression is null)
            {
                continue;
            }

            var constant = compilation.GetSemanticModel(syntax.SyntaxTree).GetConstantValue(expression, cancellationToken);
            if (constant is { HasValue: true, Value: string value })
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The <c>Code</c> the rule answers with, wherever in its hierarchy it is declared.</summary>
    private static IPropertySymbol? PropertyNamedCode(INamedTypeSymbol rule)
    {
        for (var type = (INamedTypeSymbol?)rule; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers("Code"))
            {
                if (member is IPropertySymbol { IsStatic: false, Type.SpecialType: SpecialType.System_String } property)
                {
                    return property;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an object of type <paramref name="entity"/> can be handed to a rule about
    /// <paramref name="subject"/>.
    /// </summary>
    private static bool Accepts(INamedTypeSymbol entity, ITypeSymbol subject)
    {
        for (var type = (ITypeSymbol?)entity; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, subject))
            {
                return true;
            }
        }

        foreach (var @interface in entity.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface, subject))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matched by name, like every other type these generators look for, so the analyzer assembly needs
    /// no reference to the assembly that declares it.
    /// </summary>
    private static bool IsTheInvariantInterface(INamedTypeSymbol @interface)
        => @interface is { Name: KnownTypes.InvariantInterfaceName, TypeArguments.Length: 1 }
           && @interface.ContainingNamespace.ToDisplayString() == KnownTypes.InvariantsNamespace;
}
