namespace Examples.Tenancy.Projects.Application.Access;

/// <summary>
/// The marker type <c>ProjectPermissionTexts.resx</c> and <c>ProjectPermissionTexts.nl.resx</c> are named after: what
/// each permission of <see cref="ProjectPermissions"/> lets somebody do, under its name, for the screens that make
/// roles. The module's own registration adds it to the toolkit's localizer, beside the refusals' texts.
/// </summary>
/// <remarks>
/// The English is the permission's summary, which the catalogue lists already; the English file repeats it, and a
/// test holds the two equal, so a translator has every text in one file per language. The catalogue's answer asks
/// the localizer for each permission's name, with the summary as the text where nothing translates it.
/// </remarks>
public sealed class ProjectPermissionTexts;
