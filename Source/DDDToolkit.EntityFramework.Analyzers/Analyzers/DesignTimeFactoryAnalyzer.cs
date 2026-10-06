using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DDDToolkit.EntityFramework.Analyzers;

/// <summary>
/// Reports a design-time factory whose context does not keep its migration history where the running application
/// does: DDD00074, at <c>CreateDbContext</c> of an <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c> whose class calls
/// none of <c>UseDDDToolkitDesignTime</c>, <c>UseDDDToolkit</c>, <c>UseDDDToolkitCore</c> and
/// <c>MigrationsHistoryTable</c>.
/// <para>
/// <c>UseDDDToolkit</c> keeps the history in the context's default schema. <c>dotnet ef</c> and the Supabase export
/// make the context through the factory, which has no services to hand it, and says the same with
/// <c>UseDDDToolkitDesignTime()</c>. A factory without it records every migration in the provider's default schema,
/// and nothing at run time says so until the application starts against a database whose history it cannot find: the
/// first migration is applied again and fails on a table that is there. So the build says it, where the factory is.
/// </para>
/// <para>
/// What it reads: the code of the factory's class, <c>CreateDbContext</c> and whatever of the class it calls or reads,
/// methods, properties and fields, lambdas and local functions among them. Options made by a helper of another class
/// are not looked into, as the analyzer sees no more than one class at a time: the factory adds the call after the
/// helper, which adds nothing options have already. Silent in a project that does not reference
/// <c>DDDToolkit.EntityFramework</c>, whose contexts the toolkit does not wire, and for an abstract factory, which
/// <c>dotnet ef</c> cannot make.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DesignTimeFactoryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The class of <c>UseDDDToolkit</c>, <c>UseDDDToolkitCore</c> and <c>UseDDDToolkitDesignTime</c>.</summary>
    private const string ToolkitWiring = "DDDToolkit.EntityFramework.DependencyInjection";

    /// <summary>What every relational provider's options derive from, which declares <c>MigrationsHistoryTable</c>.</summary>
    private const string RelationalOptionsBuilder = "Microsoft.EntityFrameworkCore.Infrastructure.RelationalDbContextOptionsBuilder`2";

    private static readonly ImmutableHashSet<string> ToolkitCalls = ImmutableHashSet.Create("UseDDDToolkitDesignTime", "UseDDDToolkit", "UseDDDToolkitCore");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.DesignTimeFactoryWithoutTheToolkit];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var factory = start.Compilation.GetTypeByMetadataName(KnownTypes.DesignTimeDbContextFactory);
            var wiring = start.Compilation.GetTypeByMetadataName(ToolkitWiring);
            if (factory is null || wiring is null)
            {
                return;
            }

            var relational = start.Compilation.GetTypeByMetadataName(RelationalOptionsBuilder);
            start.RegisterSymbolStartAction(symbolStart => StartClass(symbolStart, factory, wiring, relational), SymbolKind.NamedType);
        });
    }

    /// <summary>
    /// For a class that implements the factory's <c>CreateDbContext</c> itself: notes, per member of the class, whether
    /// its code places the history and which members of the class it uses, and at the end of the class reports each
    /// <c>CreateDbContext</c> from which no member that places it can be reached.
    /// </summary>
    private static void StartClass(SymbolStartAnalysisContext context, INamedTypeSymbol factory, INamedTypeSymbol wiring, INamedTypeSymbol? relational)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } type)
        {
            return;
        }

        var creates = new List<(IMethodSymbol Method, ITypeSymbol Context)>();
        foreach (var implemented in type.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, factory) || implemented.TypeArguments.Length != 1)
            {
                continue;
            }

            foreach (var member in implemented.GetMembers("CreateDbContext"))
            {
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation
                    && SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
                {
                    creates.Add((implementation, implemented.TypeArguments[0]));
                }
            }
        }

        if (creates.Count == 0)
        {
            return;
        }

        var placing = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
        var uses = new ConcurrentDictionary<ISymbol, ConcurrentBag<ISymbol>>(SymbolEqualityComparer.Default);

        context.RegisterOperationBlockStartAction(block =>
        {
            var owner = block.OwningSymbol;
            block.RegisterOperationAction(
                operation =>
                {
                    switch (operation.Operation)
                    {
                        case IInvocationOperation invocation when Places(invocation.TargetMethod, wiring, relational):
                            placing[owner] = true;
                            break;
                        case IInvocationOperation invocation:
                            Use(owner, invocation.TargetMethod);
                            break;
                        case IPropertyReferenceOperation property:
                            Use(owner, property.Property);
                            break;
                        case IFieldReferenceOperation field:
                            Use(owner, field.Field);
                            break;
                        case IMethodReferenceOperation method:
                            Use(owner, method.Method);
                            break;
                    }
                },
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.MethodReference);
        });

        context.RegisterSymbolEndAction(end =>
        {
            foreach (var (create, made) in creates)
            {
                if (!Reaches(create, placing, uses) && create.Locations.FirstOrDefault(location => location.IsInSource) is { } location)
                {
                    end.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.DesignTimeFactoryWithoutTheToolkit, location, type.Name, made.Name));
                }
            }
        });

        // A member of the class itself, as code that runs when CreateDbContext does may reach it: a property by its
        // getter or its initializer, so both are noted under the property.
        void Use(ISymbol owner, ISymbol used)
        {
            var member = used.OriginalDefinition;
            if (!SymbolEqualityComparer.Default.Equals(member.ContainingType, type))
            {
                return;
            }

            uses.GetOrAdd(owner, static _ => []).Add(member);
            if (member is IPropertySymbol { GetMethod: { } getter })
            {
                uses.GetOrAdd(member, static _ => []).Add(getter.OriginalDefinition);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="method"/> says where the history is: one of the toolkit's calls, or
    /// <c>MigrationsHistoryTable</c> of a relational provider's options.
    /// </summary>
    private static bool Places(IMethodSymbol method, INamedTypeSymbol wiring, INamedTypeSymbol? relational)
    {
        var called = method.ReducedFrom ?? method;
        if (ToolkitCalls.Contains(called.Name) && SymbolEqualityComparer.Default.Equals(called.ContainingType, wiring))
        {
            return true;
        }

        if (called.Name != "MigrationsHistoryTable" || relational is null)
        {
            return false;
        }

        for (var type = called.ContainingType; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, relational))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a member whose code places the history can be reached from <paramref name="create"/> through the class's own members.</summary>
    private static bool Reaches(IMethodSymbol create, ConcurrentDictionary<ISymbol, bool> placing, ConcurrentDictionary<ISymbol, ConcurrentBag<ISymbol>> uses)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var next = new Stack<ISymbol>();
        next.Push(create.OriginalDefinition);

        while (next.Count > 0)
        {
            var member = next.Pop();
            if (!seen.Add(member))
            {
                continue;
            }

            if (placing.ContainsKey(member))
            {
                return true;
            }

            if (uses.TryGetValue(member, out var used))
            {
                foreach (var each in used)
                {
                    next.Push(each);
                }
            }
        }

        return false;
    }
}
