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
    private const string RoleTemplate = Package + ".CustomRoleAttribute`2";
    private const string ManagedResource = "global::" + Package + ".IMembershipResource";
    private const string RoleHooks = "global::" + Package + ".ICustomRoleHooks";
    private const string Managers = "global::" + Package + ".Managers";
    private const string Access = "global::" + Package + ".Access";
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
            .Select(static (all, _) => PerResource(all!).ToEquatableArray());

        // The role classes, each of the resource it names: the hooks written onto each, and a manager beside the
        // member manager of a resource whose list is written here.
        var roles = context.SyntaxProvider.ForAttributeWithMetadataName(
                RoleTemplate,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => DiscoverRole(syntaxContext, cancellationToken))
            .Where(static role => role is not null)
            .Collect()
            .Select(static (all, _) => all.Select(static role => role!).ToEquatableArray());

        // Whether the registrations of the package are closed in this project, and so say what is wrong with a
        // member class themselves: read off the compilation, and a plain boolean, so the output step stays cached.
        var registered = context.CompilationProvider.Select(static (compilation, cancellationToken) => TemplateRegistrations.Registers(compilation, MemberTemplate, cancellationToken));

        // The ids another package's switch writes into this project, by name: no type to the compiler a generator
        // sees, so a member class over one is closed over it by the name it was written with.
        var written = context.TemplateDefaultsPlan().Select(static (plan, _) => plan.Ids);

        // The types of the managers' names the project declares itself: such a type keeps the manager out.
        var taken = lists.Combine(roles).Combine(context.CompilationProvider)
            .Select(static (all, cancellationToken) => TakenNames(all.Left.Left, all.Left.Right, all.Right, cancellationToken));

        context.RegisterSourceOutput(
            lists.Combine(roles).Combine(registered).Combine(written).Combine(taken),
            static (production, all) => ExecuteAll(production, all.Left.Left.Left.Left, all.Left.Left.Left.Right, all.Left.Left.Right, all.Left.Right, all.Right));
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

        // What the member manager is closed over beside the list's types, and what it sets when it names an owner.
        var resourceId = DefinitionFactory.IdOf(resource, cancellationToken, out var idText, out _, out _) == DefinitionFactory.DeclaredIdKind.Found ? idText : null;
        var ownerSettable = owners.Count == 1 && owners[0].SetMethod is { IsInitOnly: false };

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
            ResourceId = resourceId,
            OwnerSettable = ownerSettable,
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

        /// <summary>The resource's id, in full, or null when it cannot be told: what the member manager is closed over.</summary>
        public string? ResourceId { get; init; }

        /// <summary>Whether the property marked <c>[Owner]</c> can be set in the class: what naming an owner does.</summary>
        public bool OwnerSettable { get; init; }
    }

    /// <summary>
    /// What one role class asks for: hooks on the class, and a manager where the resource it names gets a member manager
    /// here. Null for a class the entity generator reports as it stands: one that is not partial, or is generic.
    /// </summary>
    private static RoleClass? DiscoverRole(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol role
            || context.Attributes.Length == 0
            || context.Attributes[0].AttributeClass is not { TypeArguments.Length: 2 } attribute
            || attribute.TypeArguments[0] is not INamedTypeSymbol id
            || attribute.TypeArguments[1] is not INamedTypeSymbol { TypeKind: TypeKind.Class } resource)
        {
            return null;
        }

        var declarations = role.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ClassDeclarationSyntax>()
            .ToList();
        if (declarations.Count == 0
            || declarations.Count != role.DeclaringSyntaxReferences.Length
            || declarations.Any(static declaration => !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return null;
        }

        var type = DefinitionFactory.CreateTypeInfo(role, declarations[0]);
        if (type.IsGeneric)
        {
            return null;
        }

        return new RoleClass(
            type,
            id.TypeKind == TypeKind.Error ? id.Name : Qualified(id),
            Qualified(resource),
            LocationInfo.From(((ClassDeclarationSyntax)context.TargetNode).Identifier));
    }

    /// <summary>
    /// Each manager's name the project declares a type of itself, top-level: in the global namespace, where the project
    /// wrote its own, or in a namespace, where the manager would hide it.
    /// </summary>
    private static EquatableArray<TakenName> TakenNames(
        EquatableArray<ListOfAResource> lists,
        EquatableArray<RoleClass> roles,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var names = lists.Select(static list => list.MemberName + ManagerSuffix)
            .Concat(roles.Select(static role => role.Role.Name + ManagerSuffix))
            .Distinct(StringComparer.Ordinal);

        var taken = new List<TakenName>();
        foreach (var name in names)
        {
            if (!compilation.Assembly.TypeNames.Contains(name))
            {
                continue;
            }

            foreach (var symbol in compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellationToken).OfType<INamedTypeSymbol>())
            {
                if (symbol.ContainingType is null)
                {
                    taken.Add(new TakenName(
                        name,
                        symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
                        LocationInfo.From(symbol)));
                }
            }
        }

        return taken.ToEquatableArray();
    }

    /// <summary>What the managers' names are, after the member class or the role class.</summary>
    private const string ManagerSuffix = "Manager";

    /// <summary>Writes the lists, then the member managers, the role hooks and the role managers.</summary>
    private static void ExecuteAll(
        SourceProductionContext context,
        EquatableArray<ListOfAResource> lists,
        EquatableArray<RoleClass> roles,
        bool registered,
        EquatableArray<EntityIdDefinition> written,
        EquatableArray<TakenName> taken)
    {
        // Two member classes, or two role classes, of one name would give two managers of one name.
        var twice = lists.Where(static list => !list.NotARoot && list.KeptMember is null).Select(static list => list.MemberName + ManagerSuffix)
            .Concat(roles.Select(static role => role.Role.Name + ManagerSuffix))
            .GroupBy(static name => name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToImmutableHashSet(StringComparer.Ordinal);

        var managed = new Dictionary<string, (ListOfAResource List, string[] Types)>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            if (Execute(context, list, registered, written) is not { } types)
            {
                continue;
            }

            if (!list.OwnerSettable)
            {
                Report(
                    context,
                    list.MemberName + ManagerSuffix,
                    list.Resource.Name,
                    "its owner, '" + list.Owner + "', has no setter in the class, and naming another owner sets it: give it one, { get; private set; }",
                    list.MemberAt);
                continue;
            }

            // The way to the members, and the hooks, whatever becomes of the manager's own class.
            managed[list.Resource.FullyQualifiedName] = (list, types);
        }

        // The role managers: for a role class whose resource gets a member manager here.
        var roleManagers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            WriteRoleHooks(context, role);

            var manager = role.Role.Name + ManagerSuffix;
            if (!managed.ContainsKey(role.Resource))
            {
                if (!lists.Any(list => list.Resource.FullyQualifiedName == role.Resource))
                {
                    Report(context, manager, role.Role.Name, "its resource, '" + LastNameOf(role.Resource) + "', is declared in another project, whose registration cannot name it", role.At);
                }

                continue;
            }

            if (Blocked(context, manager, role.Role.Name, role.At, twice, taken))
            {
                continue;
            }

            WriteRoleManager(context, role, manager);
            if (!roleManagers.TryGetValue(role.Resource, out var listed))
            {
                roleManagers[role.Resource] = listed = [];
            }

            listed.Add(manager);
        }

        foreach (var (list, types) in managed.Values)
        {
            var manager = list.MemberName + ManagerSuffix;
            var writesManager = false;
            if (list.ResourceId is null)
            {
                Report(context, manager, list.Resource.Name, "its id cannot be told: declare it with [AggregateRoot<TId>] over an id of your own", list.MemberAt);
            }
            else
            {
                writesManager = !Blocked(context, manager, list.Resource.Name, list.MemberAt, twice, taken);
            }

            List<string> managers = writesManager ? [manager] : [];
            if (roleManagers.TryGetValue(list.Resource.FullyQualifiedName, out var ofRoles))
            {
                managers.AddRange(ofRoles);
            }

            WriteManagedResource(context, list, types, managers);
            if (writesManager)
            {
                WriteMemberManager(context, list, types, manager);
            }
        }
    }

    /// <summary>
    /// Whether a manager's name is not to be written: a type of the name in the global namespace, which the project wrote
    /// itself and hears nothing about; one in a namespace, said on that type; or two classes that would both give it.
    /// </summary>
    private static bool Blocked(SourceProductionContext context, string manager, string forClass, LocationInfo? at, ImmutableHashSet<string> twice, EquatableArray<TakenName> taken)
    {
        var mine = taken.Where(each => each.Name == manager).ToList();
        if (mine.Count > 0)
        {
            foreach (var each in mine.Where(static each => each.Namespace is not null))
            {
                Report(
                    context,
                    manager,
                    forClass,
                    "the project declares a type of that name in '" + each.Namespace + "', which the manager in the global namespace would hide wherever that namespace is imported: "
                    + "rename that type",
                    each.At);
            }

            return true;
        }

        if (twice.Contains(manager))
        {
            Report(context, manager, forClass, "two classes of the project would each give a manager of that name: rename one of them", at);
            return true;
        }

        return false;
    }

    private static void Report(SourceProductionContext context, string manager, string forClass, string reason, LocationInfo? at)
        => DiagnosticInfo.Create(DiagnosticDescriptors.ManagerNotWritten, at, manager, forClass, reason).Report(context);

    /// <summary>
    /// The member manager's way to the resource's members, explicitly, and the hooks the resource writes its guard and
    /// its events in: each change calls the guard, then the list, then the hook that says what happened.
    /// </summary>
    private static void WriteManagedResource(SourceProductionContext context, ListOfAResource list, string[] types, List<string> managers)
    {
        var member = types[0];
        var memberId = types[2];
        var roleId = types[3];
        var face = ManagedResource + "<" + member + ", " + memberId + ", " + roleId + ">";
        var memberRole = "global::" + Package + ".MemberRole<" + memberId + ", " + roleId + ">";
        var ownerNamed = "global::" + Package + ".OwnerNamed<" + memberId + ", " + roleId + ">";
        var period = "global::" + Package + ".MemberPeriod";
        var now = "global::System.DateTimeOffset";
        var field = Identifier(list.Field!);
        var owner = Identifier(list.Owner!);

        var writer = new CodeWriter().Header();
        using (writer.TypeScope(list.Resource))
        {
            using (writer.Block("partial class " + Identifier(list.Resource.Name) + " : " + face))
            {
                writer.Line("/// <summary>Runs in front of every change the member manager makes to the members: a guard of the resource's, such as a closed resource changing no more.</summary>");
                writer.Line("partial void OnChangingMembers();");
                writer.Line();
                writer.Line("/// <summary>A member was added.</summary>");
                writer.Line("partial void OnMemberAdded(" + member + " member);");
                writer.Line();
                writer.Line("/// <summary>A member was taken off, with every role it held.</summary>");
                writer.Line("partial void OnMemberRemoved(" + memberId + " member);");
                writer.Line();
                writer.Line("/// <summary>A member was given a role.</summary>");
                writer.Line("partial void OnRoleGiven(" + memberId + " member, " + memberRole + " role);");
                writer.Line();
                writer.Line("/// <summary>A role was taken from a member.</summary>");
                writer.Line("partial void OnRoleTaken(" + memberId + " member, " + memberRole + " role);");
                writer.Line();
                writer.Line("/// <summary>Another owner was named: everything that changed with it, in the order its events are raised.</summary>");
                writer.Line("partial void OnOwnerNamed(" + ownerNamed + " named);");
                writer.Line();

                writer.Line("static global::System.Collections.Generic.IReadOnlyList<global::System.Type> " + face + ".MembershipManagers { get; } =");
                writer.Line("    [" + string.Join(", ", managers.Select(static manager => "typeof(global::" + manager + ")")) + "];");
                writer.Line();
                writer.Line(memberId + " " + face + ".Owner => " + owner + ";");
                writer.Line();
                writer.Line("global::System.Collections.Generic.IReadOnlyList<" + member + "> " + face + ".Members => " + field + ";");
                writer.Line();

                using (writer.Block(member + " " + face + ".AddMember(" + memberId + " member, " + roleId + "? role, " + period + " period, " + now + " now, " + memberId + "? by)"))
                {
                    writer.Line("OnChangingMembers();");
                    using (writer.Block("if (role is { } given)"))
                    {
                        writer.Line("var added = " + PropertyName + ".Add(member, given, period, now, by);");
                        writer.Line("OnMemberAdded(added);");
                        writer.Line("OnRoleGiven(member, added.Roles[added.Roles.Count - 1]);");
                        writer.Line("return added;");
                    }

                    writer.Line();
                    writer.Line("var plain = " + PropertyName + ".Add(member, period, now, by);");
                    writer.Line("OnMemberAdded(plain);");
                    writer.Line("return plain;");
                }

                writer.Line();
                using (writer.Block(memberRole + " " + face + ".GiveRole(" + memberId + " member, " + roleId + " role, " + period + " period, " + now + " now, " + memberId + "? by)"))
                {
                    writer.Line("OnChangingMembers();");
                    writer.Line("var given = " + PropertyName + ".GiveRole(member, role, period, now, by);");
                    writer.Line("OnRoleGiven(member, given);");
                    writer.Line("return given;");
                }

                writer.Line();
                using (writer.Block(memberRole + " " + face + ".TakeRole(" + memberId + " member, " + roleId + " role)"))
                {
                    writer.Line("OnChangingMembers();");
                    writer.Line("var taken = " + PropertyName + ".TakeRole(member, role);");
                    writer.Line("OnRoleTaken(member, taken);");
                    writer.Line("return taken;");
                }

                writer.Line();
                using (writer.Block(member + " " + face + ".RemoveMember(" + memberId + " member)"))
                {
                    writer.Line("OnChangingMembers();");
                    writer.Line("var removed = " + PropertyName + ".Remove(member);");
                    writer.Line("OnMemberRemoved(member);");
                    writer.Line("return removed;");
                }

                writer.Line();
                using (writer.Block(ownerNamed + " " + face + ".NameOwner(" + memberId + " member, global::" + Package + ".FormerOwner<" + roleId + "> formerOwner, " + now + " now)"))
                {
                    writer.Line("OnChangingMembers();");
                    writer.Line("var named = " + PropertyName + ".NameOwner(member, formerOwner, now);");
                    writer.Line(owner + " = named.Owner;");
                    writer.Line("OnOwnerNamed(named);");
                    writer.Line("return named;");
                }
            }
        }

        context.AddSource(list.Resource.HintName(".MemberManagement"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>The member manager, in the global namespace, closed over the resource, its id and the member class's types.</summary>
    private static void WriteMemberManager(SourceProductionContext context, ListOfAResource list, string[] types, string manager)
    {
        var resource = list.Resource.FullyQualifiedName;
        var resourceId = list.ResourceId!;
        var member = types[0];
        var ownId = types[1];
        var memberId = types[2];
        var roleId = types[3];

        var writer = new CodeWriter().Header();
        writer.Line("/// <summary>");
        writer.Line("/// The members of <see cref=\"" + Cref(resource) + "\"/>: adding them, giving and taking their roles, removing them, leaving, naming");
        writer.Line("/// another owner, opening a new one, and listing who is on one. Written for <see cref=\"" + Cref(member) + "\"/>, and registered");
        writer.Line("/// per scope by the resource's registration.");
        writer.Line("/// </summary>");
        using (writer.Block("public sealed class " + manager + " : " + Managers + ".MemberManager<" + resource + ", " + resourceId + ", " + member + ", " + ownId + ", " + memberId + ", " + roleId + ">"))
        {
            writer.Line("/// <summary>The member manager of " + list.Resource.Name + ". The container makes it.</summary>");
            writer.Line("public " + manager + "(");
            writer.Line("    " + Access + ".MembershipAccessManager<" + resource + "> access,");
            writer.Line("    " + Managers + ".IMemberStore<" + resource + ", " + resourceId + ", " + memberId + "> store,");
            writer.Line("    global::DDDToolkit.Abstractions.Access.ICurrentUser user,");
            writer.Line("    global::System.TimeProvider clock,");
            writer.Line("    " + Access + ".IMemberRoles<" + resourceId + ", " + roleId + ">? roles = null,");
            writer.Line("    " + Managers + ".IMemberDirectory<" + resourceId + ", " + memberId + ">? directory = null,");
            writer.Line("    " + Managers + ".IMemberRolePolicy<" + resourceId + ", " + roleId + ">? policy = null,");
            writer.Line("    " + Managers + ".IOwnPlaceWork<" + resourceId + ", " + memberId + ">? ownPlace = null)");
            writer.Line("    : base(access, store, user, clock, roles, directory, policy, ownPlace)");
            writer.Line("{");
            writer.Line("}");
        }

        context.AddSource(manager + ".g.cs", SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>The hooks of a role class: partial methods the class raises its events in, and the way the package's role calls them.</summary>
    private static void WriteRoleHooks(SourceProductionContext context, RoleClass role)
    {
        var set = "global::" + Package + ".RolePermissionsSet";
        var writer = new CodeWriter().Header();
        using (writer.TypeScope(role.Role))
        {
            using (writer.Block("partial class " + Identifier(role.Role.Name) + " : " + RoleHooks))
            {
                writer.Line("/// <summary>The role was renamed, or described again.</summary>");
                writer.Line("partial void OnRenamed();");
                writer.Line();
                writer.Line("/// <summary>The role's permissions changed: the permissions that came in and the permissions that went out.</summary>");
                writer.Line("partial void OnPermissionsSet(" + set + " set);");
                writer.Line();
                writer.Line("/// <summary>The role was archived.</summary>");
                writer.Line("partial void OnArchived();");
                writer.Line();
                writer.Line("void " + RoleHooks + ".Renamed() => OnRenamed();");
                writer.Line();
                writer.Line("void " + RoleHooks + ".PermissionsSet(" + set + " set) => OnPermissionsSet(set);");
                writer.Line();
                writer.Line("void " + RoleHooks + ".Archived() => OnArchived();");
            }
        }

        context.AddSource(role.Role.HintName(".RoleHooks"), SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>The role manager, in the global namespace, closed over the role class, its id and the resource.</summary>
    private static void WriteRoleManager(SourceProductionContext context, RoleClass role, string manager)
    {
        var type = role.Role.FullyQualifiedName;
        var writer = new CodeWriter().Header();
        writer.Line("/// <summary>");
        writer.Line("/// The roles a customer makes for <see cref=\"" + Cref(role.Resource) + "\"/>, of <see cref=\"" + Cref(type) + "\"/>: making them, the starter roles among");
        writer.Line("/// them, renaming them, setting their permissions and archiving them, and reading them. Registered per scope by the");
        writer.Line("/// resource's registration.");
        writer.Line("/// </summary>");
        using (writer.Block("public sealed class " + manager + " : " + Managers + ".CustomRoleManager<" + type + ", " + role.Id + ", " + role.Resource + ">"))
        {
            writer.Line("/// <summary>The role manager of " + role.Role.Name + ". The container makes it.</summary>");
            writer.Line("public " + manager + "(");
            writer.Line("    " + Managers + ".ICustomRoleStore<" + type + ", " + role.Id + "> store,");
            writer.Line("    " + Access + ".MembershipAccessManager<" + role.Resource + "> access,");
            writer.Line("    global::DDDToolkit.Abstractions.Access.ICurrentUser user,");
            writer.Line("    " + Managers + ".ICustomRoleAccess<" + role.Resource + ">? roleAccess = null)");
            writer.Line("    : base(store, access, user, roleAccess)");
            writer.Line("{");
            writer.Line("}");
        }

        context.AddSource(manager + ".g.cs", SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    /// <summary>A type in full as a <c>cref</c> takes it: without <c>global::</c>.</summary>
    private static string Cref(string qualified) => qualified.StartsWith("global::", StringComparison.Ordinal) ? qualified.Substring("global::".Length) : qualified;

    /// <summary>A type's own name, from its fully qualified one.</summary>
    private static string LastNameOf(string qualified)
    {
        var dot = qualified.LastIndexOfAny(['.', ':']);
        return dot < 0 ? qualified : qualified.Substring(dot + 1);
    }

    /// <summary>A role class, as the managers are written from it.</summary>
    /// <param name="Role">The role class.</param>
    /// <param name="Id">The role class's id, in full; the name alone for one this generator cannot see.</param>
    /// <param name="Resource">The resource the roles are of, in full.</param>
    /// <param name="At">Where the role class is declared.</param>
    private sealed record RoleClass(TypeDeclarationInfo Role, string Id, string Resource, LocationInfo? At);

    /// <summary>A top-level type the project declares of a manager's name.</summary>
    /// <param name="Name">The name.</param>
    /// <param name="Namespace">Its namespace, or null for the global one.</param>
    /// <param name="At">Where it is declared.</param>
    private sealed record TakenName(string Name, string? Namespace, LocationInfo? At);

    // ------------------------------------------------------------------ writing it

    /// <summary>Writes the list, or says why it cannot be written.</summary>
    /// <param name="context">Where the list and the diagnostics go.</param>
    /// <param name="list">What the member class asks for.</param>
    /// <param name="registered">Whether the registrations of this project say what is wrong with a member class themselves.</param>
    /// <param name="written">The ids a package's switch writes into this project, which this generator knows by their names alone.</param>
    private static string[]? Execute(SourceProductionContext context, ListOfAResource list, bool registered, EquatableArray<EntityIdDefinition> written)
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

            return null;
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

            return null;
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
            return null;
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
        return [.. list.TypeArguments.Select(InFull)];
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
