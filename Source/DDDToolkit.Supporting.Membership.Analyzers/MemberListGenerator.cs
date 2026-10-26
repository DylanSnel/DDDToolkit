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
/// Writes what makes a class a resource with members: for a class declared with the package's member template,
/// <c>[Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]</c>, the resource's name, its codes and its
/// module's scope, which its rules read through its type, and the member list on it that the resource's own
/// methods change its members through:
/// <code>
/// partial class Document : IMembershipResource
/// {
///     public static MembershipCodes Codes { get; } = MembershipCodes.Under("documents");
///
///     static string IMembershipResource.MembershipName =&gt; "documents";
///     static MembershipCodes IMembershipResource.MembershipCodes =&gt; Codes;
///     static string? IMembershipResource.MembershipScope =&gt; "filing";
///
///     private MemberList&lt;DocumentShare, DocumentShareId, UserId, NamedRole&gt; Members
///         =&gt; new(_shares, OwnerId, Codes);
/// }
/// </code>
/// Everything in it is said elsewhere already: the four types by the member class, and the rest by the resource
/// and its project. So an application writes its own methods and guards, and not this.
/// <para>
/// <b>The name.</b> The plural of the class's name, in lower case with a dash between words: <c>Document</c> is
/// <c>documents</c>, <c>CraneBoard</c> <c>crane-boards</c>. A class that says another with <c>[Members("...")]</c> has
/// that, converted the same way and not made plural; one that cannot be converted is DDD00091, and nothing of the
/// resource is written. The codes are written as <c>Codes</c>, under the name, unless the class declares a static
/// <c>MembershipCodes</c> of its own, which is then the one; the scope is the name of the project's module, in the
/// same form, or none for a project that declares no module.
/// </para>
/// <para>
/// <b>What the list is written from.</b> Three things the resource declares, each of which has to be the only
/// one of its kind there, so that nothing is guessed, and the codes:
/// </para>
/// <list type="bullet">
/// <item>The members: the one get-only partial collection property of the member class that the entity
/// generator keeps in a list, <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c> or
/// <c>IEnumerable&lt;T&gt;</c>. The list is the field that generator declares for it.</item>
/// <item>The owner: the property marked <c>[Owner]</c>, of the type a member is known by.</item>
/// <item>New rows: the member class's own id has a <c>Create()</c>, which the list makes a new row's id with:
/// the generator writes one for an <c>[EntityId&lt;Guid&gt;]</c>, and an id over anything else declares it.</item>
/// <item>The codes: the ones written here, or the one static property or field of <c>MembershipCodes</c> the
/// resource declares itself.</item>
/// </list>
/// <para>
/// <b>When nothing is written.</b> A resource that declares a member list itself, under whatever name, is left
/// alone and hears nothing about it: that is the form for every shape this cannot tell. Its name, codes and scope
/// are written all the same. A resource that declares none and cannot be given one is told what
/// stands in the way (DDD00059), since its
/// members could not be changed at all. That includes a member class over a type this generator cannot see,
/// the id of an <c>[AggregateRoot&lt;Guid&gt;]</c> that another generator writes: the application would
/// otherwise hear only that <c>Members</c> does not exist. An id another package's switch writes into the
/// project, an assembly attribute of that package's marked <c>[TemplateDefaults]</c>, is no type to this generator
/// either, but the switch's plan says it is written and where, so the list is closed over it by its name, in full.
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
    private const string MembersName = Package + ".MembersAttribute";
    private const string Resource = "global::" + Package + ".IMembershipResource";
    private const string CodesType = "global::" + Codes;

    /// <summary>What the codes the generator writes are called on the resource.</summary>
    private const string CodesName = "Codes";

    /// <summary>What the list is called on the resource.</summary>
    private const string PropertyName = "Members";

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

        // The ids another package's switch writes into this project, by name: no type to the compiler a generator
        // sees, so a member class over one is closed over it by the name it was written with.
        var written = context.TemplateDefaultsPlan().Select(static (plan, _) => plan.Ids);

        context.RegisterSourceOutput(
            lists.Combine(registered).Combine(written),
            static (production, all) => Execute(production, all.Left.Left, all.Left.Right, all.Right));
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
        // another. Which of the two it is shows only once the ids a package's switch writes are known, so the class
        // is judged as if each were there, by its name, and what is said of it waits for those (Execute).
        var unseen = attribute.TypeArguments.Take(3).Where(static argument => argument.TypeKind == TypeKind.Error).Select(static argument => argument.Name).ToEquatableArray();

        // Members are kept with the aggregate they belong to. A resource that is none is the member class's
        // mistake, said on it; nothing about the resource's own members is.
        if (!EntityDeclarations.IsAggregateRoot(resource))
        {
            return new ListOfAResource(
                Resource: type,
                MemberName: member.Name,
                MemberAt: memberAt,
                TypeArguments: EquatableArray<string>.Empty,
                ShortTypeArguments: string.Join(", ", shortArguments),
                Collection: null,
                Field: null,
                Owner: null,
                Codes: null,
                Declared: declared,
                Reasons: EquatableArray<string>.Empty,
                Unseen: unseen,
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

        // The owner is what the resource marks [Owner]: a fact of the domain, said on the aggregate, which a
        // request about the owner reads as well. One of the member's id, since the owner is a member.
        var owners = OwnerDeclarations.OwnersOf(resource).Where(static property => !property.IsStatic).ToList();
        if (owners.Count == 0)
        {
            reasons.Add("it marks no property [Owner]: mark the property of '" + knownBy.Name + "' that holds its owner [Owner]");
        }
        else if (owners.Count > 1)
        {
            reasons.Add("it marks more than one property [Owner] (" + Listed(owners) + "), and a resource has one owner");
        }
        else if (!(knownBy.TypeKind == TypeKind.Error
                     ? owners[0].Type is { TypeKind: TypeKind.Error } unbound && unbound.Name == knownBy.Name
                     : SymbolEqualityComparer.Default.Equals(owners[0].Type, knownBy)))
        {
            reasons.Add("its owner, '" + owners[0].Name + "', is a '" + owners[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "', and its members are known by '"
                        + knownBy.Name + "': the owner is a member, so it is known by the same, and never left empty");
            owners.Clear();
        }

        // An id this generator cannot see is either one a switch writes, over a Guid, or what is said of it instead.
        // What it lacks is said as DDD00067 says it: a Create() in its partial declaration, the interface as well for
        // an id written by hand, or the target framework of the project that declares it.
        if (ownId.TypeKind != TypeKind.Error && IdCreation.Lacks(ownId, compilation, cancellationToken))
        {
            var shortfall = IdCreation.ShortfallOf(ownId);
            reasons.Add("'" + ownId.Name + "', the id of a member's row, " + shortfall.Lacks + ", which a new row's id is made with: "
                        + char.ToLowerInvariant(shortfall.Advice[0]) + shortfall.Advice.Substring(1));
        }

        // Codes of its own, when the resource declares them; otherwise they are written, under its name.
        var codes = members
            .Where(candidate => candidate is { IsStatic: true, IsImplicitlyDeclared: false }
                                && candidate is IFieldSymbol or IPropertySymbol { GetMethod: not null }
                                && SymbolEqualityComparer.Default.Equals(TypeOf(candidate), codesType))
            .ToList();
        var resourceReasons = new List<string>();
        if (codes.Count > 1)
        {
            resourceReasons.Add("it declares more than one static member of MembershipCodes (" + Listed(codes) + "), so which codes its members refuse under cannot be told");
        }
        else if (codes.Count == 0 && resource.GetMembers(CodesName).Length > 0)
        {
            resourceReasons.Add("it has a member called " + CodesName + " already, which the codes written for it would be called too: declare its codes as a static MembershipCodes yourself");
        }

        // Its name: what [Members] says, or the plural of its class's name.
        string? name = null;
        DiagnosticInfo? badName = null;
        if (resource.GetAttributes().FirstOrDefault(static each => each.AttributeClass?.ToDisplayString() == MembersName) is { } membersAttribute)
        {
            var said = membersAttribute.ConstructorArguments.Length == 1 ? membersAttribute.ConstructorArguments[0].Value as string : null;
            name = MembershipNames.Converted(said);
            if (name is null)
            {
                badName = DiagnosticInfo.Create(
                    DiagnosticDescriptors.MembershipNameUnreadable,
                    (membersAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } at ? LocationInfo.From(at) : null) ?? type.Location,
                    resource.Name,
                    said ?? "null");
            }
        }
        else
        {
            name = MembershipNames.OfClass(resource.Name);
        }

        if (resource.GetMembers().Any(static each => each.Name.EndsWith("IMembershipResource.MembershipName", StringComparison.Ordinal)))
        {
            // Written by hand already: the resource says its name, codes and scope itself.
            name = null;
        }

        var scope = MembershipNames.Converted(ModuleBoundary.ModuleOf(compilation.Assembly));

        if (!declared && resource.GetMembers(PropertyName).Length > 0)
        {
            reasons.Add("it has a member called " + PropertyName + " already, which the list would be called too");
        }

        return new ListOfAResource(
            Resource: type,
            MemberName: member.Name,
            MemberAt: memberAt,
            TypeArguments: new[] { member }.Concat(attribute.TypeArguments.Take(3).Cast<INamedTypeSymbol>()).Select(Qualified).ToEquatableArray(),
            ShortTypeArguments: string.Join(", ", shortArguments),
            Collection: collection,
            Field: field,
            Owner: owners.Count == 1 ? owners[0].Name : null,
            Codes: codes.Count == 1 ? codes[0].Name : resourceReasons.Count == 0 ? CodesName : null,
            Declared: declared,
            Reasons: reasons.Concat(resourceReasons).ToEquatableArray(),
            Unseen: unseen)
        {
            Name = resourceReasons.Count == 0 ? name : null,
            WritesCodes = codes.Count == 0 && resourceReasons.Count == 0,
            Scope = scope,
            BadName = badName,
        };
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

    private static string Qualified(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Listed(IEnumerable<ISymbol> members) => string.Join(", ", members.Select(static member => member.Name));

    /// <summary>What one member class asks for on the resource it names.</summary>
    /// <param name="Resource">The resource's class, which the list is written into.</param>
    /// <param name="MemberName">The member class's name.</param>
    /// <param name="TypeArguments">The four types the list is closed over, the member class and its three, written out in full; one this generator cannot see as it was written.</param>
    /// <param name="ShortTypeArguments">The same four types as an application writes them, for a message.</param>
    /// <param name="Collection">The resource's collection of members, or null when it cannot be told.</param>
    /// <param name="Field">The field the entity generator keeps that collection in, or null.</param>
    /// <param name="Owner">The resource's property that holds its owner, or null when it cannot be told.</param>
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
    /// <param name="Unseen">
    /// The names of the member class's types this generator cannot see: one an application's switch writes, which
    /// is known by its name once the switch's ids are, or one nobody declares or another generator writes.
    /// </param>
    private sealed record ListOfAResource(
        TypeDeclarationInfo Resource,
        string MemberName,
        EquatableArray<string> TypeArguments,
        string ShortTypeArguments,
        string? Collection,
        string? Field,
        string? Owner,
        string? Codes,
        bool Declared,
        EquatableArray<string> Reasons,
        LocationInfo? MemberAt = null,
        bool NotARoot = false,
        string? KeptMember = null,
        string? KeptIn = null,
        bool Several = false,
        EquatableArray<string> Unseen = default)
    {
        /// <summary>The resource's name, as its codes are written, or null when it is not written here.</summary>
        public string? Name { get; init; }

        /// <summary>Whether the codes are written, as <see cref="CodesName"/>, rather than declared by the resource.</summary>
        public bool WritesCodes { get; init; }

        /// <summary>The scope of the project's module's system work, or null for a project that declares no module.</summary>
        public string? Scope { get; init; }

        /// <summary>DDD00091 for a name <c>[Members]</c> says that cannot be converted, or null.</summary>
        public DiagnosticInfo? BadName { get; init; }
    }

    // ------------------------------------------------------------------ writing it

    /// <summary>Writes the list, or says why it cannot be written.</summary>
    /// <param name="context">Where the list and the diagnostics go.</param>
    /// <param name="list">What the member class asks for.</param>
    /// <param name="registered">Whether the registrations of this project say what is wrong with a member class themselves.</param>
    /// <param name="written">The ids a package's switch writes into this project, which this generator knows by their names alone.</param>
    private static void Execute(SourceProductionContext context, ListOfAResource list, bool registered, EquatableArray<EntityIdDefinition> written)
    {
        // A type of the member class this generator cannot see and no switch writes: one nobody declares, which the
        // compiler reports as well, or one another generator writes, the id of an [AggregateRoot<Guid>] say. The list
        // cannot be closed over it, and the owner cannot be found by it, so that is all there is to say; a resource
        // that writes its list itself has nothing to hear.
        var unseen = list.Unseen.Where(name => !written.Any(id => id.Type.Name == name)).ToList();
        if (unseen.Count > 0)
        {
            if (!list.Declared)
            {
                DiagnosticInfo.Create(
                        DiagnosticDescriptors.MemberListNotWritten,
                        list.Resource.Location,
                        list.Resource.Name,
                        list.MemberName,
                        string.Join("; ", unseen.Select(static name => "'" + name + "' is a type it cannot see, one another generator writes or nobody declares: declare it yourself, with [EntityId<T>], where the member class can see it")),
                        list.ShortTypeArguments)
                    .Report(context);
            }

            return;
        }

        // An id a switch writes is named in full, so the list compiles wherever the resource is.
        string InFull(string text)
            => list.Unseen.Contains(text) && written.FirstOrDefault(id => id.Type.Name == text) is { } id ? id.Type.FullyQualifiedName : text;

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

        list.BadName?.Report(context);

        // What the rules read through the resource's type, written whatever becomes of the list: its name, its codes
        // and its module's scope.
        var writesResource = list.Name is not null && list.Codes is not null && !(list.Several && registered);
        var writesList = !list.Declared && !(list.Several && registered);
        if (writesList && (list.Reasons.Count > 0 || list.Field is null || list.Owner is null || list.Codes is null))
        {
            DiagnosticInfo.Create(
                    DiagnosticDescriptors.MemberListNotWritten,
                    list.Resource.Location,
                    list.Resource.Name,
                    list.MemberName,
                    string.Join("; ", list.Reasons),
                    list.ShortTypeArguments)
                .Report(context);
            writesList = false;
        }

        // Two files, so the member list is in one of its own exactly when it is written.
        if (writesResource)
        {
            var resource = new CodeWriter().Header();
            using (resource.TypeScope(list.Resource))
            {
                using (resource.Block("partial class " + Identifier(list.Resource.Name) + " : " + Resource))
                {
                    WriteResource(resource, list);
                }
            }

            context.AddSource(list.Resource.HintName(".Membership"), SourceText.From(resource.ToString(), Encoding.UTF8));
        }

        if (!writesList)
        {
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
                writer.Line("private global::" + Package + ".MemberList<" + string.Join(", ", list.TypeArguments.Select(InFull)) + "> " + PropertyName);
                writer.Line("    => new(" + Identifier(list.Field!) + ", " + Identifier(list.Owner!) + ", " + Identifier(list.Codes!) + ");");
            }
        }

        context.AddSource(list.Resource.HintName("." + PropertyName), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// The resource's part of <see cref="Resource"/>: its codes, where they are written, and the three members its
    /// rules read through its type, explicitly, so they add nothing to what the class shows.
    /// </summary>
    private static void WriteResource(CodeWriter writer, ListOfAResource list)
    {
        var name = Literal(list.Name!);
        if (list.WritesCodes)
        {
            writer.Line("/// <summary>");
            writer.Line("/// The codes " + list.Resource.Name + " refuses under, the rules about its members' own names under its name: <c>" + list.Name
                        + ".already-member</c> and the rest.");
            writer.Line("/// </summary>");
            writer.Line("public static " + CodesType + " " + CodesName + " { get; } = " + CodesType + ".Under(" + name + ");");
            writer.Line();
        }

        writer.Line("/// <summary>The name of " + list.Resource.Name + " as its codes and its functions in the database are named.</summary>");
        writer.Line("static string " + Resource + ".MembershipName => " + name + ";");
        writer.Line();
        writer.Line("/// <summary>The codes " + list.Resource.Name + " refuses under.</summary>");
        writer.Line("static " + CodesType + " " + Resource + ".MembershipCodes => " + Identifier(list.Codes!) + ";");
        writer.Line();
        writer.Line("/// <summary>The scope of its module's system work" + (list.Scope is null ? ": none, since the project declares no module." : ".") + "</summary>");
        writer.Line("static string? " + Resource + ".MembershipScope => " + (list.Scope is null ? "null" : Literal(list.Scope)) + ";");
        writer.Line();
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    /// <summary>A name as an identifier: a keyword gets an <c>@</c>.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
