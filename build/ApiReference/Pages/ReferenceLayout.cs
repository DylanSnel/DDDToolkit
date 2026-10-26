using DDDToolkit.Build.ApiReference.Loading;
using DDDToolkit.Build.ApiReference.Model;
using DDDToolkit.Build.ApiReference.Writing;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Build.ApiReference.Pages;

/// <summary>A page as it will be written: its groups, their types and their members, each heading with its anchor.</summary>
internal sealed record PageLayout(PagePlan Plan, IReadOnlyList<GroupLayout> Groups);

/// <summary>A group of a page.</summary>
internal sealed record GroupLayout(string Heading, string Anchor, string? Intro, IReadOnlyList<TypeLayout> Types);

/// <summary>
/// A type's section. <paramref name="Template"/> is the attribute that declares an application's class from it, when the
/// type is one of the templates.
/// </summary>
internal sealed record TypeLayout(INamedTypeSymbol Type, string Heading, string Anchor, INamedTypeSymbol? Template, IReadOnlyList<MemberLayout> Members);

/// <summary>A member; <paramref name="Anchor"/> is <see langword="null"/> for one the type's table holds whole.</summary>
internal sealed record MemberLayout(ISymbol Member, string Heading, string? Anchor);

/// <summary>
/// Puts every documented type on its page and in its group, orders them, and gives every heading the anchor the site will
/// give it, so links can be written before the pages are.
/// </summary>
internal sealed class ReferenceLayout
{
    private readonly ApiSurface _surface;
    private readonly Documentation _documentation;

    private ReferenceLayout(ApiSurface surface, Documentation documentation)
    {
        _surface = surface;
        _documentation = documentation;
    }

    /// <summary>The pages, in sidebar order.</summary>
    public IReadOnlyList<PageLayout> Pages { get; private set; } = [];

    /// <summary>Types no claim of the plan takes, which went to an "Other" group.</summary>
    public List<INamedTypeSymbol> Unclaimed { get; } = [];

    /// <summary>Where each documented type and member is: the page's file and the heading's anchor.</summary>
    public Dictionary<ISymbol, (string File, string Anchor)> Locations { get; } = new(SymbolEqualityComparer.Default);

    public static ReferenceLayout Build(PackageCompilation packages, ApiSurface surface, Documentation documentation)
    {
        var layout = new ReferenceLayout(surface, documentation);
        layout.Lay(packages);
        return layout;
    }

    /// <summary>
    /// Whether a member's whole specification fits its row of the type's table, so it needs no section of its own: a field
    /// or a property, whose declaration the row shows, with a summary of one paragraph and no other tag.
    /// </summary>
    public bool FitsInTable(ISymbol member)
        => member is IFieldSymbol or IPropertySymbol { IsIndexer: false }
           && (_documentation.Of(member) is not { } docs
               || (docs.Elements().All(element => element.Name.LocalName == "summary") && docs.Elements("summary").All(DocMarkdown.IsInline)));

    private void Lay(PackageCompilation packages)
    {
        var templates = TemplatesOf(packages.Packages);

        // Every visible type, with its assembly and folder.
        var types = new List<(INamedTypeSymbol Type, string Assembly, string Folder)>();
        foreach (var assembly in packages.Packages)
        {
            var folders = SourceFolders.Read(packages.PathOf(assembly), assembly.Name);
            types.AddRange(_surface.TypesOf(assembly).Select(type => (type, assembly.Name, FolderOf(type, folders) ?? "")));
        }

        // Claims by type name first, then by the longest folder.
        var placed = new Dictionary<INamedTypeSymbol, (int Page, int Group, int Claim)>(SymbolEqualityComparer.Default);
        foreach (var (type, assembly, folder) in types)
        {
            (int Page, int Group, int Claim, int Strength)? best = null;
            for (var page = 0; page < ReferencePlan.Pages.Count; page++)
            {
                var groups = ReferencePlan.Pages[page].Groups;
                for (var group = 0; group < groups.Count; group++)
                {
                    for (var index = 0; index < groups[group].Claims.Count; index++)
                    {
                        var claim = groups[group].Claims[index];
                        if (claim.Assembly != assembly)
                        {
                            continue;
                        }

                        var strength = claim switch
                        {
                            { Type: { } name } when name == type.Name => int.MaxValue,
                            { Folder: { } claimed } when claimed.Length == 0 || folder == claimed || folder.StartsWith(claimed + "/", StringComparison.Ordinal) => claimed.Length,
                            _ => -1,
                        };
                        if (strength >= 0 && (best is null || strength > best.Value.Strength))
                        {
                            best = (page, group, index, strength);
                        }
                    }
                }
            }

            if (best is { } found)
            {
                placed[type] = (found.Page, found.Group, found.Claim);
            }
            else
            {
                Unclaimed.Add(type);
            }
        }

        // A type no claim took goes to the last page of its assembly, so it is still documented and still found.
        var lastPageOf = ReferencePlan.Assemblies.ToDictionary(
            assembly => assembly,
            assembly => ReferencePlan.Pages.ToList().FindLastIndex(page => page.Groups.Any(group => group.Claims.Any(claim => claim.Assembly == assembly))));

        var pages = new List<PageLayout>();
        for (var pageIndex = 0; pageIndex < ReferencePlan.Pages.Count; pageIndex++)
        {
            var plan = ReferencePlan.Pages[pageIndex];

            // Every heading of the page goes through one slugger, in the order the page will have them, the title first.
            var slugger = new Slugger();
            slugger.Slug(plan.FullTitle);

            var groupPlans = plan.Groups.Select((group, groupIndex) => (group.Heading, group.Intro, Lead: (group.Lead ?? []).ToList(), Types: types
                    .Where(entry => placed.TryGetValue(entry.Type, out var at) && at.Page == pageIndex && at.Group == groupIndex)
                    .ToList()))
                .ToList();

            var others = types.Where(entry => Unclaimed.Contains(entry.Type, SymbolEqualityComparer.Default) && lastPageOf[entry.Assembly] == pageIndex).ToList();
            if (others.Count > 0)
            {
                groupPlans.Add(("Other", null, new List<string>(), others));
            }

            var groups = new List<GroupLayout>();
            foreach (var (heading, intro, lead, entries) in groupPlans)
            {
                if (entries.Count == 0)
                {
                    continue;
                }

                var groupAnchor = slugger.Slug(heading);
                var ordered = entries
                    .OrderBy(entry => lead.IndexOf(entry.Type.Name) is var rank and >= 0 ? rank : lead.Count)
                    .ThenBy(entry => placed.TryGetValue(entry.Type, out var at) ? at.Claim : 0)
                    .ThenBy(entry => FolderRank(entry.Folder, placed.TryGetValue(entry.Type, out var at) ? plan.Groups[at.Group].Claims[at.Claim].Folder : null))
                    .ThenBy(entry => entry.Folder, StringComparer.Ordinal)
                    .ThenBy(entry => KindRank(entry.Type, templates))
                    .ThenBy(entry => Signatures.Plain(entry.Type), StringComparer.Ordinal)
                    .ToList();

                var typeLayouts = new List<TypeLayout>();
                foreach (var (type, _, _) in ordered)
                {
                    var template = templates.GetValueOrDefault(type);
                    var name = Signatures.Name(type);
                    var typeHeading = template is null ? name : $"{ApplicationName(template)} ({name})";
                    var anchor = slugger.Slug(typeHeading);
                    Locations[type] = (plan.File, anchor);

                    var members = new List<MemberLayout>();
                    foreach (var member in OrderMembers(type))
                    {
                        var memberHeading = Signatures.ShortName(member);
                        var memberAnchor = FitsInTable(member) ? null : slugger.Slug(memberHeading);
                        Locations[member] = (plan.File, memberAnchor ?? anchor);
                        members.Add(new MemberLayout(member, memberHeading, memberAnchor));
                    }

                    typeLayouts.Add(new TypeLayout(type, typeHeading, anchor, template, members));
                }

                groups.Add(new GroupLayout(heading, groupAnchor, intro, typeLayouts));
            }

            pages.Add(new PageLayout(plan, groups));
        }

        Pages = pages;
    }

    /// <summary>The name the application's class goes by: the attribute's, without <c>Aggregate</c> and <c>Attribute</c>.</summary>
    public static string ApplicationName(INamedTypeSymbol templateAttribute)
    {
        var name = templateAttribute.Name;
        foreach (var suffix in new[] { "Attribute", "Aggregate" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
            }
        }

        return name;
    }

    /// <summary>
    /// Each template with the attribute that declares an application's class from it: the attribute's
    /// <c>[AggregateRootTemplate(typeof(...))]</c> or <c>[EntityTemplate(typeof(...))]</c> names it.
    /// </summary>
    private static Dictionary<INamedTypeSymbol, INamedTypeSymbol> TemplatesOf(IEnumerable<IAssemblySymbol> assemblies)
    {
        var templates = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var assembly in assemblies)
        {
            foreach (var type in AllTypes(assembly.GlobalNamespace))
            {
                foreach (var attribute in type.GetAttributes())
                {
                    if (attribute.AttributeClass?.Name is "AggregateRootTemplateAttribute" or "EntityTemplateAttribute"
                        && attribute.ConstructorArguments is [{ Value: INamedTypeSymbol template }, ..])
                    {
                        templates[template.OriginalDefinition] = type;
                    }
                }
            }
        }

        return templates;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol nested)
            {
                if (nested is INamedTypeSymbol type)
                {
                    yield return type;
                }

                foreach (var inner in AllTypes(nested))
                {
                    yield return inner;
                }
            }
        }
    }

    private IEnumerable<ISymbol> OrderMembers(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Delegate)
        {
            return [];
        }

        var members = _surface.MembersOf(type);
        if (type.TypeKind == TypeKind.Enum)
        {
            return members.OfType<IFieldSymbol>().Where(field => field.HasConstantValue);
        }

        return members
            .Select((member, index) => (Member: member, Index: index))
            .OrderBy(entry => entry.Member switch
            {
                IFieldSymbol { IsConst: true } => 0,
                IFieldSymbol => 1,
                IMethodSymbol { MethodKind: MethodKind.Constructor } => 2,
                IPropertySymbol => 3,
                IEventSymbol => 4,
                IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } => 6,
                _ => 5,
            })
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Member);
    }

    /// <summary>
    /// The claimed folder's own types first, then those of the folders below it that an aggregate keeps: its entities,
    /// value objects, invariants and events, in that order, then any other.
    /// </summary>
    private static int FolderRank(string folder, string? claimed)
    {
        if (claimed is null || folder == claimed)
        {
            return 0;
        }

        var below = claimed.Length == 0 ? folder : folder[(claimed.Length + 1)..];
        return below.Split('/')[0] switch
        {
            "Entities" => 1,
            "ValueObjects" => 2,
            "Invariants" => 3,
            "Events" => 4,
            _ => 5,
        };
    }

    /// <summary>The template first, its attribute next, then the types that do things, then records, then enums.</summary>
    private static int KindRank(INamedTypeSymbol type, Dictionary<INamedTypeSymbol, INamedTypeSymbol> templates)
    {
        if (templates.ContainsKey(type))
        {
            return 0;
        }

        if (templates.Values.Contains(type, SymbolEqualityComparer.Default))
        {
            return 1;
        }

        return type switch
        {
            { TypeKind: TypeKind.Enum } => 4,
            _ when ApiSurface.IsRecord(type) => 3,
            _ => 2,
        };
    }

    /// <summary>
    /// The folder of a type only a generator wrote is that of what it was written for: the type it is nested in, or the
    /// type it derives from, as the always-valid twin of a value object derives from the value object.
    /// </summary>
    private static string? FolderOf(INamedTypeSymbol type, SourceFolders folders)
        => folders.FolderOf(type)
           ?? (type.ContainingType is { } containing ? FolderOf(containing, folders) : null)
           ?? (type.BaseType is { } baseType && SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, type.ContainingAssembly)
               ? FolderOf(baseType.OriginalDefinition, folders)
               : null);
}
