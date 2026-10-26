namespace DDDToolkit.Build.ApiReference.Pages;

/// <summary>
/// The reference of one supporting domain: the assemblies it documents, the folder its pages go to, and its pages. Each
/// domain is read in a compilation of its own, from its own packages, so its pages link to the types of its own reference.
/// </summary>
/// <param name="Domain">The domain's name, which every page's title starts with: <c>Tenancy</c> makes "Tenancy API: Use cases".</param>
/// <param name="Folder">The folder the pages go to, relative to the repository, with <c>/</c> between its parts.</param>
/// <param name="Assemblies">
/// The documented assemblies, each a project under <c>Source</c>, in the order the pages take them: every type of theirs a
/// consumer can name is on a page.
/// </param>
/// <param name="Pages">The pages, in the order the sidebar shows them.</param>
internal sealed record ReferencePlan(string Domain, string Folder, IReadOnlyList<string> Assemblies, IReadOnlyList<PagePlan> Pages)
{
    /// <summary>Every domain's reference, in the order the tool writes them.</summary>
    public static readonly IReadOnlyList<ReferencePlan> All = [TenancyReference.Plan, MembershipReference.Plan];

    /// <summary>A page's title, which says whose API it is: in a browser tab or llms.txt there is no sidebar to say it.</summary>
    public string FullTitle(PagePlan page) => Domain + " API: " + page.Title;

    /// <summary>
    /// The assemblies a claim names that are not documented whole: shared packages, such as the attributes of
    /// <c>DDDToolkit.Abstractions</c>, of which the domain's API is used with a few types. Only the types a claim names
    /// by <see cref="Claim.Type"/> are taken from them, without their nested types.
    /// </summary>
    public IReadOnlyList<string> Borrowed =>
    [
        .. Pages.SelectMany(page => page.Groups).SelectMany(group => group.Claims)
            .Select(claim => claim.Assembly)
            .Where(assembly => !Assemblies.Contains(assembly))
            .Distinct(),
    ];
}

/// <summary>A page of a domain's reference: its file in the domain's folder, its title and its groups.</summary>
/// <param name="File">The file name.</param>
/// <param name="Title">The page's label in the sidebar, under the domain's API reference.</param>
/// <param name="Intro">
/// What the page holds, in Markdown, above its first group. <c>{type-parameters:Name}</c> in it is the table of the type
/// parameters of the documented type of that name (<see cref="PageWriter.TypeParameters"/>).
/// </param>
/// <param name="Groups">The sections of the page, in order.</param>
internal sealed record PagePlan(string File, string Title, string Intro, IReadOnlyList<GroupPlan> Groups);

/// <summary>A section of a page: the types its claims take, under one heading.</summary>
/// <param name="Heading">The heading.</param>
/// <param name="Claims">Which types the section takes.</param>
/// <param name="Intro">A line under the heading, in Markdown, or <see langword="null"/>.</param>
/// <param name="Lead">
/// Types, by name, that open the section in this order, ahead of the order the rest is put in: the template, its
/// attribute, the types that do things, records, enums.
/// </param>
internal sealed record GroupPlan(string Heading, IReadOnlyList<Claim> Claims, string? Intro = null, IReadOnlyList<string>? Lead = null);

/// <summary>
/// Types a section takes: those of <paramref name="Assembly"/> written in <paramref name="Folder"/> or a folder below it,
/// or the one named <paramref name="Type"/>. A type goes to the most specific claim that takes it: a type by name, then
/// the longest folder. A claim on an assembly the domain does not document whole names a type: only that one is taken.
/// </summary>
/// <param name="Assembly">The assembly's name.</param>
/// <param name="Folder">The folder, relative to the project; <c>""</c> is all of it.</param>
/// <param name="Type">A type's name, without its type parameters.</param>
internal sealed record Claim(string Assembly, string? Folder = null, string? Type = null);
