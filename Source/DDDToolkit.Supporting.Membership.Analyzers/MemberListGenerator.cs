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
/// Writes the member list of a resource: for a class declared with the package's member template,
/// <c>[Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]</c>, the property on the resource it names
/// that the resource's own methods change its members through:
/// <code>
/// partial class Document
/// {
///     private MemberList&lt;DocumentShare, DocumentShareId, UserId, NamedRole&gt; Members
///         =&gt; new(_shares, OwnerId, DocumentShareId.CreateSequential, Codes);
/// }
/// </code>
/// Everything in that line is said elsewhere already: the four types by the member class, and the rest by
/// the resource. So an application writes its own methods and guards, and not this.
/// <para>
/// <b>What it is written from.</b> Four things the resource declares, each of which has to be the only one
/// of its kind there, so that nothing is guessed:
/// </para>
/// <list type="bullet">
/// <item>The members: the one get-only partial collection property of the member class that the entity
/// generator keeps in a list, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c> or
/// <c>IEnumerable&lt;T&gt;</c>. The list is the field that generator declares for it.</item>
/// <item>The owner: the one property of the type a member is known by. A resource with members keeps its
/// owner as one, so where there is one such property it is the owner.</item>
/// <item>New rows: the member class's own id is an <c>[EntityId&lt;Guid&gt;]</c>, and a new one is made in time
/// order.</item>
/// <item>The codes: the one static property or field of <c>MembershipCodes</c>, which the rules about the
/// members refuse under.</item>
/// </list>
/// <para>
/// <b>When nothing is written.</b> A resource that declares a member list itself, under whatever name, is left
/// alone and hears nothing: that is the form for every shape this cannot tell, a resource with two properties
/// of the member's id, a row keyed by something else than a <see cref="Guid"/>, codes kept elsewhere. A
/// resource that declares none and cannot be given one is told what stands in the way (DDD00059), since its
/// members could not be changed at all. That includes a member class over a type this generator cannot see,
/// the id of an <c>[AggregateRoot&lt;Guid&gt;]</c> that another generator writes: the application would
/// otherwise hear only that <c>Members</c> does not exist.
/// </para>
/// <para>
/// <b>A member class that names the wrong resource.</b> A resource that is no aggregate root, or one that keeps
/// its members as another member class already, is the member class's mistake, and is said once, on that class
/// (DDD00060). Of several member classes that name one resource, the one the resource keeps a collection of is
/// its member class, and its list is written, so the resource's own methods still compile; the others are what
/// to fix. A project that gets the package's registrations hears it from them already, as DDD00050 or DDD00045,
/// and this generator stands back there.
/// </para>
/// <para>
/// The package's types are named here by their metadata names, as text: this assembly references nothing of
/// the package, and what it writes is compiled in the application, which does.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MemberListGenerator : IIncrementalGenerator
{
    private const string Package = "DDDToolkit.Supporting.Membership";
    private const string MemberTemplate = Package + ".MemberAttribute`4";
    private const string MemberList = Package + ".MemberList`4";
    private const string Codes = Package + ".MembershipCodes";

    /// <summary>What the list is called on the resource.</summary>
    private const string PropertyName = "Members";

    /// <summary>The factory the entity id generator writes for an id over a <see cref="Guid"/>: a new id, in time order.</summary>
    private const string NewId = "CreateSequential";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var lists = context.SyntaxProvider.ForAttributeWithMetadataName(
                MemberTemplate,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => Discover(syntaxContext, cancellationToken))
            .Where(static list => list is not null)
            .Collect()
            .SelectMany(static (all, _) => PerResource(all!));

        // Whether the registrations of the package are closed in this project, and so say what is wrong with a
        // member class themselves: read off the compilation, and a plain boolean, so the output step stays cached.
        var registered = context.CompilationProvider.Select(static (compilation, cancellationToken) => TemplateRegistrations.Registers(compilation, MemberTemplate, cancellationToken));

        context.RegisterSourceOutput(lists.Combine(registered), static (production, pair) => Execute(production, pair.Left, pair.Right));
    }

    // ------------------------------------------------------------------ what to write

    /// <summary>
    /// What one member class asks for, or null when it names nothing a list could be written on: a resource
    /// of another project, or a declaration the compiler or the entity generator reports as it stands.
    /// </summary>
    private static ListOfAResource? Discover(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var compilation = context.SemanticModel.Compilation;
        if (context.TargetSymbol is not INamedTypeSymbol member
            || context.Attributes.Length == 0
            || context.Attributes[0].AttributeClass is not { TypeArguments.Length: 4 } attribute
            || compilation.GetTypeByMetadataName(MemberList) is not { } listType
            || compilation.GetTypeByMetadataName(Codes) is not { } codesType)
        {
            return null;
        }

        // A resource that is no class the compiler reports itself, and so it does a type argument that is no named type.
        if (attribute.TypeArguments.Any(static argument => argument is not INamedTypeSymbol)
            || attribute.TypeArguments[3] is not INamedTypeSymbol { TypeKind: TypeKind.Class } resource)
        {
            return null;
        }

        var ownId = (INamedTypeSymbol)attribute.TypeArguments[0];
        var knownBy = (INamedTypeSymbol)attribute.TypeArguments[1];
        var memberAt = LocationInfo.From(((ClassDeclarationSyntax)context.TargetNode).Identifier);

        // The list is written into the resource's own class, so that class is this project's, and one the
        // entity generator writes a part of: partial, and not generic, which that generator holds it to.
        var declarations = resource.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ClassDeclarationSyntax>()
            .ToList();
        if (declarations.Count == 0
            || declarations.Count != resource.DeclaringSyntaxReferences.Length
            || declarations.Any(static declaration => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return null;
        }

        var type = DefinitionFactory.CreateTypeInfo(resource, declarations[0]);
        if (type.IsGeneric)
        {
            return null;
        }

        var members = resource.GetMembers();
        var declared = members.Any(candidate => IsAListOf(TypeOf(candidate), member, listType));
        var reasons = new List<string>();

        string[] shortArguments = [.. new[] { member }.Concat(attribute.TypeArguments.Take(3).Cast<INamedTypeSymbol>()).Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))];

        // A type of the member class this generator cannot see: one nobody declares, which the compiler reports as
        // well, or one another generator writes, the id of an [AggregateRoot<Guid>] say, which no generator sees of
        // another. The list cannot be closed over it, and the owner cannot be found by it, so that is all there is
        // to say; a resource that writes its list itself has nothing to hear.
        var unseen = attribute.TypeArguments.Take(3).Where(static argument => argument.TypeKind == TypeKind.Error).ToList();
        if (unseen.Count > 0)
        {
            foreach (var argument in unseen)
            {
                reasons.Add("'" + argument.Name + "' is a type it cannot see, one another generator writes or nobody declares: declare it yourself, with [EntityId<T>], where the member class can see it");
            }

            return new ListOfAResource(
                Resource: type,
                MemberName: member.Name,
                MemberAt: memberAt,
                ListType: string.Empty,
                ShortTypeArguments: string.Join(", ", shortArguments),
                Collection: null,
                Field: null,
                Owner: null,
                NewId: string.Empty,
                Codes: null,
                Declared: declared,
                Reasons: reasons.ToEquatableArray());
        }

        // Members are kept with the aggregate they belong to. A resource that is none is the member class's
        // mistake, said on it; nothing about the resource's own members is.
        if (!EntityDeclarations.IsAggregateRoot(resource))
        {
            return new ListOfAResource(
                Resource: type,
                MemberName: member.Name,
                MemberAt: memberAt,
                ListType: string.Empty,
                ShortTypeArguments: string.Join(", ", shortArguments),
                Collection: null,
                Field: null,
                Owner: null,
                NewId: string.Empty,
                Codes: null,
                Declared: declared,
                Reasons: EquatableArray<string>.Empty,
                NotARoot: true);
        }

        string? field = null;
        string? collection = null;
        var collections = members.OfType<IPropertySymbol>()
            .Where(property => !property.IsStatic
                               && !property.IsIndexer
                               && property.IsPartialDefinition
                               && property.PartialImplementationPart is null
                               && property.SetMethod is null
                               && DefinitionFactory.IsBackedByAList(property.Type)
                               && SymbolEqualityComparer.Default.Equals(((INamedTypeSymbol)property.Type).TypeArguments[0], member))
            .ToList();
        if (collections.Count == 1)
        {
            collection = collections[0].Name;
            field = Identifiers.CollectionFieldNameFor(collection);
        }
        else if (collections.Count == 0)
        {
            reasons.Add(
                "it declares no collection of '" + member.Name + "' the toolkit keeps in a list: a get-only partial property of IReadOnlyList<"
                + member.Name + ">, IReadOnlyCollection<" + member.Name + "> or IEnumerable<" + member.Name + ">");
        }
        else
        {
            reasons.Add("it declares more than one collection of '" + member.Name + "' (" + Listed(collections) + "), so which of them holds its members cannot be told");
        }

        var owners = members.OfType<IPropertySymbol>()
            .Where(property => !property.IsStatic
                               && !property.IsIndexer
                               && property.GetMethod is not null
                               && property.ExplicitInterfaceImplementations.IsEmpty
                               && SymbolEqualityComparer.Default.Equals(property.Type, knownBy))
            .ToList();
        if (owners.Count == 0)
        {
            reasons.Add("it declares no property of '" + knownBy.Name + "', which its owner is kept in");
        }
        else if (owners.Count > 1)
        {
            reasons.Add("it declares more than one property of '" + knownBy.Name + "' (" + Listed(owners) + "), so which of them is its owner cannot be told");
        }

        if (!MakesNewIdsInTimeOrder(ownId))
        {
            reasons.Add("'" + ownId.Name + "', the id of a member's row, is not an [EntityId<Guid>], so there is no telling how a new one is made");
        }

        var codes = members
            .Where(candidate => candidate is { IsStatic: true, IsImplicitlyDeclared: false }
                                && candidate is IFieldSymbol or IPropertySymbol { GetMethod: not null }
                                && SymbolEqualityComparer.Default.Equals(TypeOf(candidate), codesType))
            .ToList();
        if (codes.Count == 0)
        {
            reasons.Add("it declares no static property or field of MembershipCodes, the codes the rules about its members refuse under");
        }
        else if (codes.Count > 1)
        {
            reasons.Add("it declares more than one static member of MembershipCodes (" + Listed(codes) + "), so which codes its members refuse under cannot be told");
        }

        if (!declared && resource.GetMembers(PropertyName).Length > 0)
        {
            reasons.Add("it has a member called " + PropertyName + " already, which the list would be called too");
        }

        string[] typeArguments = [.. new[] { member }.Concat(attribute.TypeArguments.Take(3).Cast<INamedTypeSymbol>()).Select(Qualified)];

        return new ListOfAResource(
            Resource: type,
            MemberName: member.Name,
            MemberAt: memberAt,
            ListType: "global::" + Package + ".MemberList<" + string.Join(", ", typeArguments) + ">",
            ShortTypeArguments: string.Join(", ", shortArguments),
            Collection: collection,
            Field: field,
            Owner: owners.Count == 1 ? owners[0].Name : null,
            NewId: Qualified(ownId) + "." + NewId,
            Codes: codes.Count == 1 ? codes[0].Name : null,
            Declared: declared,
            Reasons: reasons.ToEquatableArray());
    }

    /// <summary>
    /// What is written, and what is reported, for each resource. Of several member classes that name one
    /// resource, the one the resource keeps a collection of is its member class: its list is written, and each
    /// of the others is what to fix, said on it. Where the resource keeps a collection of none of them, or of
    /// more than one, no list can be written, since both would be called the same, and that is said once.
    /// </summary>
    private static IEnumerable<ListOfAResource> PerResource(ImmutableArray<ListOfAResource> all)
    {
        foreach (var resource in all.GroupBy(static list => list.Resource.FullyQualifiedName, StringComparer.Ordinal))
        {
            // A member class that names no aggregate root is said on its own, whatever else names the class.
            var lists = new List<ListOfAResource>();
            foreach (var list in resource)
            {
                if (list.NotARoot)
                {
                    yield return list;
                }
                else
                {
                    lists.Add(list);
                }
            }

            if (lists.Count <= 1)
            {
                foreach (var list in lists)
                {
                    yield return list;
                }

                continue;
            }

            var kept = lists.Where(static list => list.Collection is not null).ToList();
            if (kept.Count == 1)
            {
                yield return kept[0];
                foreach (var extra in lists.Where(list => !ReferenceEquals(list, kept[0])))
                {
                    yield return extra with { KeptMember = kept[0].MemberName, KeptIn = kept[0].Collection, Reasons = EquatableArray<string>.Empty };
                }

                continue;
            }

            var named = string.Join(", ", lists.Select(static list => "'" + list.MemberName + "'").OrderBy(static name => name, StringComparer.Ordinal));
            yield return lists[0] with
            {
                Reasons = new[] { "it is the resource of more than one member class (" + named + "), and each list needs a name of its own" }.ToEquatableArray(),
                Several = true,
            };
        }
    }

    /// <summary>The type a member is of: a field's or a property's type, and what a method answers.</summary>
    private static ITypeSymbol? TypeOf(ISymbol member) => member switch
    {
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        IMethodSymbol { MethodKind: MethodKind.Ordinary } method => method.ReturnType,
        _ => null,
    };

    /// <summary>Whether a type is the package's member list over <paramref name="member"/>: what a resource declares when it writes its list itself.</summary>
    private static bool IsAListOf(ITypeSymbol? type, INamedTypeSymbol member, INamedTypeSymbol listType)
        => type is INamedTypeSymbol { TypeArguments.Length: 4 } list
           && SymbolEqualityComparer.Default.Equals(list.OriginalDefinition, listType)
           && SymbolEqualityComparer.Default.Equals(list.TypeArguments[0], member);

    /// <summary>
    /// Whether an id has the factory the list is handed: an <c>[EntityId&lt;Guid&gt;]</c>, for which the entity
    /// id generator writes it. An id of this project does not show the factory yet, since a generator does
    /// not see what another writes, so it is told by its attribute; one of a referenced project shows it.
    /// </summary>
    private static bool MakesNewIdsInTimeOrder(INamedTypeSymbol id)
    {
        if (id.DeclaringSyntaxReferences.IsEmpty)
        {
            return id.GetMembers(NewId).OfType<IMethodSymbol>().Any(static method =>
                method is { IsStatic: true, Parameters.Length: 0, DeclaredAccessibility: Accessibility.Public });
        }

        foreach (var attribute in id.GetAttributes())
        {
            if (attribute.AttributeClass is { TypeArguments.Length: 1 } attributeClass
                && EntityDeclarations.Is(attributeClass, KnownTypes.EntityIdAttribute)
                && attributeClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid")
            {
                return true;
            }
        }

        return false;
    }

    private static string Qualified(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Listed(IEnumerable<ISymbol> members) => string.Join(", ", members.Select(static member => member.Name));

    /// <summary>What one member class asks for on the resource it names.</summary>
    /// <param name="Resource">The resource's class, which the list is written into.</param>
    /// <param name="MemberName">The member class's name.</param>
    /// <param name="ListType">The list's type, closed over the member class and its three types, written out in full.</param>
    /// <param name="ShortTypeArguments">The same four types as an application writes them, for a message.</param>
    /// <param name="Collection">The resource's collection of members, or null when it cannot be told.</param>
    /// <param name="Field">The field the entity generator keeps that collection in, or null.</param>
    /// <param name="Owner">The resource's property that holds its owner, or null when it cannot be told.</param>
    /// <param name="NewId">What makes the id of a new member row.</param>
    /// <param name="Codes">The resource's static member that holds its codes, or null when it cannot be told.</param>
    /// <param name="Declared">Whether the resource declares a member list over this member class itself.</param>
    /// <param name="Reasons">What stands in the way of writing the list, each phrased to follow "the toolkit cannot write one:".</param>
    /// <param name="MemberAt">Where the member class is declared: what DDD00060 is reported on.</param>
    /// <param name="NotARoot">Whether the resource the member class names is no aggregate root.</param>
    /// <param name="KeptMember">
    /// For a member class of a resource that keeps its members as another member class: that class. Null
    /// otherwise.
    /// </param>
    /// <param name="KeptIn">For such a member class, the resource's collection of the other one. Null otherwise.</param>
    /// <param name="Several">Whether the resource is named by several member classes and keeps a collection of none of them, or of more than one.</param>
    private sealed record ListOfAResource(
        TypeDeclarationInfo Resource,
        string MemberName,
        string ListType,
        string ShortTypeArguments,
        string? Collection,
        string? Field,
        string? Owner,
        string NewId,
        string? Codes,
        bool Declared,
        EquatableArray<string> Reasons,
        LocationInfo? MemberAt = null,
        bool NotARoot = false,
        string? KeptMember = null,
        string? KeptIn = null,
        bool Several = false);

    // ------------------------------------------------------------------ writing it

    private static void Execute(SourceProductionContext context, ListOfAResource list, bool registered)
    {
        if (list.NotARoot || list.KeptMember is not null)
        {
            // The member class's own mistake: said once, on it, unless the registrations of this project say it.
            if (!registered)
            {
                DiagnosticInfo.Create(
                        DiagnosticDescriptors.MemberClassOfNoResource,
                        list.MemberAt,
                        list.MemberName,
                        list.Resource.Name,
                        list.KeptMember is { } kept
                            ? "'" + list.Resource.Name + "' keeps its members as '" + kept + "' already, in '" + list.KeptIn
                              + "': a resource has one member class. Name the resource this class is a member of, or remove it"
                            : "'" + list.Resource.Name + "' is not declared an aggregate root: members are kept with the aggregate root they belong to, and their list on it. "
                              + "Name that aggregate root as the resource")
                    .Report(context);
            }

            return;
        }

        if (list.Declared)
        {
            // The application wrote its list itself: its to write, under its own name, and nothing to say about it.
            return;
        }

        if (list.Several && registered)
        {
            // Two classes whose registrations would be called the same: the registrations say so, once.
            return;
        }

        if (list.Reasons.Count > 0 || list.Field is null || list.Owner is null || list.Codes is null)
        {
            DiagnosticInfo.Create(
                    DiagnosticDescriptors.MemberListNotWritten,
                    list.Resource.Location,
                    list.Resource.Name,
                    list.MemberName,
                    string.Join("; ", list.Reasons),
                    list.ShortTypeArguments)
                .Report(context);
            return;
        }

        var writer = new CodeWriter().Header();
        using (writer.TypeScope(list.Resource))
        {
            using (writer.Block("partial class " + Identifier(list.Resource.Name)))
            {
                writer.Line("/// <summary>");
                writer.Line("/// The members of this " + list.Resource.Name + " with the rules about them: what its own methods change its members through.");
                writer.Line("/// Written from what the class declares: its members in <see cref=\"" + list.Collection + "\"/>, its owner in <see cref=\"" + list.Owner
                            + "\"/>, and the codes it refuses under in <see cref=\"" + list.Codes + "\"/>.");
                writer.Line("/// </summary>");
                writer.Line("private " + list.ListType + " " + PropertyName);
                writer.Line("    => new(" + Identifier(list.Field) + ", " + Identifier(list.Owner) + ", " + list.NewId + ", " + Identifier(list.Codes) + ");");
            }
        }

        context.AddSource(list.Resource.HintName("." + PropertyName), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>A name as an identifier: a keyword gets an <c>@</c>.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
