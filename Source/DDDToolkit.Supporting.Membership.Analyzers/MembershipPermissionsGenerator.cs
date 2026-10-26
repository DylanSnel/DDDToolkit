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

namespace DDDToolkit.Supporting.Membership.Analyzers;

/// <summary>
/// Hands the permissions declared on each resource with members to the rules that are made of them: the constants of
/// a <c>[Permissions]</c> class marked <c>[On&lt;TResource&gt;]</c>, which <c>MembershipRules.For&lt;TResource&gt;</c>
/// reads.
/// <code>
/// [Permissions]
/// public static class BoardPermissions
/// {
///     [On&lt;Board&gt;(EveryMember = true)] public const string View = "boards.view";
/// }
///
/// // Written into the project: declared when the project is first used
/// [ModuleInitializer]
/// internal static void Declare() =&gt; MembershipPermissions.Declare&lt;Board&gt;("BoardPermissions", [new("boards.view", EveryMember: true)], []);
/// </code>
/// <para>
/// <b>What it declares.</b> The permissions on a resource that the project's own classes declare, and, for every
/// resource whose rules the project makes with <c>MembershipRules.For&lt;TResource&gt;</c>, those the projects it
/// references declare. So the rules find every permission they can see, whichever project made them, and a project
/// that only declares permissions hands them over itself. A class declared by two projects is declared once.
/// </para>
/// <para>
/// <b>What it reports.</b> A level on a class that is no resource with members, and a second permission of one
/// resource that every member holds or that locks the members or the owner (DDD00093); and rules made of a resource
/// no permission is declared on (DDD00094). A permission without a level, and a class that is not static, are the
/// core's to report (DDD00092).
/// </para>
/// <para>
/// The package's types are named here by their metadata names, as text: this assembly references nothing of the
/// package, and what it writes is compiled in the application, which does.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MembershipPermissionsGenerator : IIncrementalGenerator
{
    private const string Package = "DDDToolkit.Supporting.Membership";
    private const string PermissionsAttribute = "DDDToolkit.Access.PermissionsAttribute";
    private const string LevelBase = "DDDToolkit.Access.PermissionLevelAttribute";
    private const string OnAttribute = Package + ".Access.OnAttribute`1";
    private const string MemberTemplate = Package + ".MemberAttribute`4";
    private const string Rules = Package + ".Access.MembershipRules";
    private const string Registry = Package + ".Access.MembershipPermissions";
    private const string ResourceInterface = Package + ".IMembershipResource";

    /// <summary>The class written into a project that declares or reads permissions.</summary>
    public const string ClassName = "GeneratedMembershipPermissions";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var own = context.SyntaxProvider.ForAttributeWithMetadataName(
                PermissionsAttribute,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => Read(syntaxContext, cancellationToken))
            .Where(static declaring => declaring is not null)
            .Select(static (declaring, _) => declaring!)
            .Collect();

        // The resources this project names in a member class: a level on another class of this project fits nothing.
        var resources = context.SyntaxProvider.ForAttributeWithMetadataName(
                MemberTemplate,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, _) => syntaxContext.Attributes.Length > 0
                                                        && syntaxContext.Attributes[0].AttributeClass is { TypeArguments.Length: 4 } attribute
                                                        && attribute.TypeArguments[3] is INamedTypeSymbol resource
                    ? Qualified(resource)
                    : null)
            .Where(static resource => resource is not null)
            .Select(static (resource, _) => resource!)
            .Collect();

        var calls = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax invocation && IsForCall(invocation.Expression),
                transform: static (syntaxContext, cancellationToken) => ReadCall(syntaxContext, cancellationToken))
            .Where(static call => call is not null)
            .Select(static (call, _) => call!)
            .Collect();

        var referenced = context.CompilationProvider.Select(static (compilation, cancellationToken) => ReadReferenced(compilation, cancellationToken));

        context.RegisterSourceOutput(
            own.Combine(resources).Combine(calls).Combine(referenced),
            static (production, all) => Execute(production, all.Left.Left.Left, all.Left.Left.Right, all.Left.Right, all.Right));
    }

    // ------------------------------------------------------------------ what is declared

    /// <summary>One permission on a resource, as the attribute says it.</summary>
    private sealed record Declared(string Key, bool Owner, bool EveryMember, bool InRoles, bool LocksMembers, bool LocksOwner, bool FromAbove, string Field, LocationInfo? At);

    /// <summary>What one class declares on one resource.</summary>
    /// <param name="Resource">The resource, written out in full.</param>
    /// <param name="ResourceName">The resource's own name, for a message.</param>
    /// <param name="IsResource">Whether the class is a resource with members, as far as this project can tell; null where it cannot.</param>
    /// <param name="Permissions">The permissions on it, in the order they are declared.</param>
    /// <param name="Elsewhere">The keys the class declares at another level.</param>
    private sealed record OnResource(string Resource, string ResourceName, bool? IsResource, EquatableArray<Declared> Permissions, EquatableArray<string> Elsewhere);

    /// <summary>A <c>[Permissions]</c> class and what it declares on each resource.</summary>
    /// <param name="Name">The class by its full name, as it is declared under.</param>
    /// <param name="Accessible">Whether this project can name every resource of it, as the written declaration does.</param>
    private sealed record DeclaringClass(string Name, EquatableArray<OnResource> Resources, bool Accessible = true);

    /// <summary>A <c>MembershipRules.For&lt;TResource&gt;</c> of this project.</summary>
    /// <param name="Above">Where it says <c>reachFromAbove</c> of a resource that marks no property <c>[At]</c>; null otherwise.</param>
    private sealed record ForCall(string Resource, string ResourceName, LocationInfo? At, LocationInfo? Above = null);

    private static DeclaringClass? Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || context.SemanticModel.Compilation.GetTypeByMetadataName(Registry) is null)
        {
            return null;
        }

        return Of(type, context.SemanticModel.Compilation, own: true, cancellationToken);
    }

    /// <summary>What <paramref name="type"/> declares on each resource, read off its constants' attributes.</summary>
    private static DeclaringClass? Of(INamedTypeSymbol type, Compilation compilation, bool own, CancellationToken cancellationToken)
    {
        var byResource = new Dictionary<string, (INamedTypeSymbol Resource, List<Declared> Permissions)>(StringComparer.Ordinal);
        var levelled = new List<(string Key, HashSet<string> On)>();
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!field.IsConst || field.ConstantValue is not string key || field.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var on = new HashSet<string>(StringComparer.Ordinal);
            var hasLevel = false;
            foreach (var attribute in field.GetAttributes())
            {
                if (attribute.AttributeClass is not { } attributeClass || !DerivesFrom(attributeClass, LevelBase))
                {
                    continue;
                }

                hasLevel = true;
                if (attributeClass.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::" + Package + ".Access.OnAttribute<TResource>"
                    || attributeClass.TypeArguments.Length != 1
                    || attributeClass.TypeArguments[0] is not INamedTypeSymbol resource
                    || resource.TypeKind == TypeKind.Error)
                {
                    continue;
                }

                var qualified = Qualified(resource);
                on.Add(qualified);
                if (!byResource.TryGetValue(qualified, out var entry))
                {
                    byResource[qualified] = entry = (resource, []);
                }

                entry.Permissions.Add(new Declared(
                    key,
                    Named(attribute, "Owner", true),
                    Named(attribute, "EveryMember", false),
                    Named(attribute, "InRoles", true),
                    Named(attribute, "LocksMembers", false),
                    Named(attribute, "LocksOwner", false),
                    Named(attribute, "FromAbove", true),
                    type.Name + "." + field.Name,
                    own && attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } syntax ? LocationInfo.From(syntax) : null));
            }

            if (hasLevel)
            {
                levelled.Add((key, on));
            }
        }

        if (byResource.Count == 0)
        {
            return null;
        }

        var accessible = true;
        var resources = new List<OnResource>();
        foreach (var pair in byResource.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var resource = pair.Value.Resource;
            accessible &= compilation.IsSymbolAccessibleWithin(resource, compilation.Assembly);
            bool? isResource = SymbolEqualityComparer.Default.Equals(resource.ContainingAssembly, compilation.Assembly)
                ? null
                : resource.AllInterfaces.Any(static each => each.ToDisplayString() == ResourceInterface);
            resources.Add(new OnResource(
                pair.Key,
                resource.Name,
                isResource,
                pair.Value.Permissions.ToEquatableArray(),
                levelled.Where(each => !each.On.Contains(pair.Key)).Select(static each => each.Key).Distinct(StringComparer.Ordinal).ToEquatableArray()));
        }

        return new DeclaringClass(type.ToDisplayString(), resources.ToEquatableArray(), accessible);
    }

    private static bool Named(AttributeData attribute, string name, bool otherwise)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return otherwise;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForCall(ExpressionSyntax expression)
        => expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "For", TypeArgumentList.Arguments.Count: 1 } } => true,
            GenericNameSyntax { Identifier.ValueText: "For", TypeArgumentList.Arguments.Count: 1 } => true,
            _ => false,
        };

    private static ForCall? ReadCall(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // In the project that declares the resource, its IMembershipResource part is one a generator writes, which no
        // generator sees: the call binds only as a candidate whose constraint fails, and is the call all the same.
        var bound = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
        var symbol = bound.Symbol ?? (bound is { CandidateReason: CandidateReason.OverloadResolutionFailure, CandidateSymbols.Length: 1 } ? bound.CandidateSymbols[0] : null);
        if (symbol is not IMethodSymbol { Name: "For", TypeArguments.Length: 1 } method
            || method.ContainingType?.ToDisplayString() != Rules
            || method.TypeArguments[0] is not INamedTypeSymbol { TypeKind: not TypeKind.Error } resource)
        {
            return null;
        }

        var name = invocation.Expression is MemberAccessExpressionSyntax access ? (SyntaxNode)access.Name : invocation.Expression;

        // reachFromAbove reads where the resource hangs from the property it marks [At], which it must have.
        LocationInfo? above = null;
        var reach = invocation.ArgumentList.Arguments.FirstOrDefault(static argument => argument.NameColon?.Name.Identifier.ValueText == "reachFromAbove")
                    ?? (invocation.ArgumentList.Arguments.Count > 2 && invocation.ArgumentList.Arguments[2].NameColon is null ? invocation.ArgumentList.Arguments[2] : null);
        if (reach is not null
            && reach.Expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.FalseLiteralExpression or (int)SyntaxKind.NullLiteralExpression }
            && !MarksPlace(resource))
        {
            above = LocationInfo.From(reach);
        }

        return new ForCall(Qualified(resource), resource.Name, LocationInfo.From(name), above);
    }

    /// <summary>Whether <paramref name="resource"/>, or a class it derives from, marks a property <c>[At]</c>.</summary>
    private static bool MarksPlace(INamedTypeSymbol resource)
    {
        for (var current = resource; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.GetMembers().OfType<IPropertySymbol>().Any(static property => property.GetAttributes().Any(static each => each.AttributeClass?.ToDisplayString() == KnownTypes.AtAttribute)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the projects this one references declare, each class read off its metadata: only assemblies compiled
    /// against the package can mark a permission with its level, so the rest are passed over without reading a type.
    /// </summary>
    private static EquatableArray<DeclaringClass> ReadReferenced(Compilation compilation, CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName(Registry) is null || compilation.GetTypeByMetadataName(PermissionsAttribute) is not { } marker)
        {
            return EquatableArray<DeclaringClass>.Empty;
        }

        var found = new List<DeclaringClass>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assembly.Name == Package || !assembly.Modules.Any(static module => module.ReferencedAssemblies.Any(static identity => identity.Name == Package)))
            {
                continue;
            }

            foreach (var type in DefinitionFactory.TypesIn(assembly.GlobalNamespace, cancellationToken))
            {
                if (type.IsStatic
                    && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)
                    && type.GetAttributes().Any(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, marker))
                    && Of(type, compilation, own: false, cancellationToken) is { } declaring)
                {
                    found.Add(declaring);
                }
            }
        }

        return found.OrderBy(static each => each.Name, StringComparer.Ordinal).ToEquatableArray();
    }

    private static string Qualified(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // ------------------------------------------------------------------ what is written

    private static void Execute(
        SourceProductionContext production,
        ImmutableArray<DeclaringClass> own,
        ImmutableArray<string> resources,
        ImmutableArray<ForCall> calls,
        EquatableArray<DeclaringClass> referenced)
    {
        var known = new HashSet<string>(resources, StringComparer.Ordinal);
        var ownClasses = own.OrderBy(static each => each.Name, StringComparer.Ordinal).ToList();

        // A level on a class that is no resource with members, as far as this project can tell.
        foreach (var declaring in ownClasses)
        {
            foreach (var on in declaring.Resources)
            {
                if (on.IsResource == false || (on.IsResource is null && !known.Contains(on.Resource)))
                {
                    foreach (var permission in on.Permissions)
                    {
                        DiagnosticInfo.Create(
                                DiagnosticDescriptors.PermissionLevelMisfits,
                                permission.At,
                                permission.Field,
                                "is declared [On<" + on.ResourceName + ">], and '" + on.ResourceName + "' is no resource with members: no member class names it, [Member<TId, TMemberId, TRoleId, "
                                + on.ResourceName + ">]. Name the aggregate root whose members hold it, or give it another level the [Permissions] class knows")
                            .Report(production);
                    }
                }
            }
        }

        // One permission of a resource at most for each of the settings that name the one key.
        foreach (var resource in ownClasses.SelectMany(static each => each.Resources).GroupBy(static on => on.Resource, StringComparer.Ordinal))
        {
            var permissions = resource.SelectMany(static on => on.Permissions).ToList();
            var name = resource.First().ResourceName;
            Once(production, permissions.Where(static each => each.EveryMember).ToList(), name, "EveryMember", "every member holds");
            Once(production, permissions.Where(static each => each.LocksMembers).ToList(), name, "LocksMembers", "locks the members");
            Once(production, permissions.Where(static each => each.LocksOwner).ToList(), name, "LocksOwner", "locks the owner");
            foreach (var twice in permissions.GroupBy(static each => each.Key, StringComparer.Ordinal).Where(static each => each.Count() > 1))
            {
                var second = twice.Skip(1).First();
                DiagnosticInfo.Create(
                        DiagnosticDescriptors.PermissionLevelMisfits,
                        second.At,
                        second.Field,
                        "declares '" + twice.Key + "' on " + name + ", and so does '" + twice.First().Field + "': a permission is declared once")
                    .Report(production);
            }
        }

        // What each class declares, and, for the rules this project makes, what the projects it references declare.
        var declarations = new List<(DeclaringClass Class, OnResource On)>();
        foreach (var declaring in ownClasses)
        {
            foreach (var on in declaring.Resources)
            {
                declarations.Add((declaring, on));
            }
        }

        var asked = new HashSet<string>(calls.Select(static call => call.Resource), StringComparer.Ordinal);
        foreach (var declaring in referenced)
        {
            if (!declaring.Accessible)
            {
                continue;
            }

            foreach (var on in declaring.Resources)
            {
                if (asked.Contains(on.Resource) && !declarations.Any(each => each.Class.Name == declaring.Name && each.On.Resource == on.Resource))
                {
                    declarations.Add((declaring, on));
                }
            }
        }

        foreach (var call in calls)
        {
            if (call.Above is not null)
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.ReachFromAboveWithoutPlace, call.Above, call.ResourceName).Report(production);
            }

            if (!declarations.Any(each => each.On.Resource == call.Resource))
            {
                DiagnosticInfo.Create(DiagnosticDescriptors.RulesWithoutPermissions, call.At, call.ResourceName).Report(production);
            }
        }

        if (declarations.Count == 0)
        {
            return;
        }

        production.AddSource(ClassName + ".g.cs", SourceText.From(Write(declarations), Encoding.UTF8));
    }

    private static void Once(SourceProductionContext production, List<Declared> marked, string resource, string setting, string what)
    {
        foreach (var second in marked.Skip(1))
        {
            DiagnosticInfo.Create(
                    DiagnosticDescriptors.PermissionLevelMisfits,
                    second.At,
                    second.Field,
                    "is marked " + setting + " on " + resource + ", and so is '" + marked[0].Field + "': one permission " + what + ". Mark the one")
                .Report(production);
        }
    }

    private static string Write(List<(DeclaringClass Class, OnResource On)> declarations)
    {
        const string Permission = "global::" + Package + ".Access.ResourcePermission";
        var writer = new CodeWriter().Header();
        writer.Line("namespace DDDToolkit.Supporting.Membership.Generated;");
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// The permissions declared on resources with members that this project hands to their rules: those its own");
        writer.Line("/// <c>[Permissions]</c> classes declare, and those of the projects it references for the rules it makes with");
        writer.Line("/// <c>MembershipRules.For&lt;TResource&gt;</c>. Declared when the project is first used, before any of its code runs.");
        writer.Line("/// </summary>");
        using (writer.Block("internal static class " + ClassName))
        {
            writer.Line("/// <summary>Declares every permission listed here.</summary>");
            writer.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
            using (writer.Block("internal static void Declare()"))
            {
                foreach (var (declaring, on) in declarations)
                {
                    writer.Line("global::" + Registry + ".Declare<" + on.Resource + ">(");
                    writer.Line("    " + Literal(declaring.Name) + ",");
                    writer.Line("    new " + Permission + "[]");
                    writer.Line("    {");
                    foreach (var permission in on.Permissions)
                    {
                        writer.Line("        new(" + Literal(permission.Key) + ", Owner: " + Bool(permission.Owner) + ", EveryMember: " + Bool(permission.EveryMember)
                                    + ", InRoles: " + Bool(permission.InRoles) + ", LocksMembers: " + Bool(permission.LocksMembers) + ", LocksOwner: " + Bool(permission.LocksOwner)
                                    + ", FromAbove: " + Bool(permission.FromAbove) + "),");
                    }

                    writer.Line("    },");
                    writer.Line("    new string[] { " + string.Join(", ", on.Elsewhere.Select(Literal)) + " });");
                }
            }
        }

        return writer.ToString();
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    private static string Bool(bool value) => value ? "true" : "false";
}
