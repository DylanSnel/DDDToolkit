namespace Examples.Tenancy.Projects.Domain.Aggregates.Projects;

/// <summary>
/// The marker type <c>ProjectFailures.resx</c> and <c>ProjectFailures.nl.resx</c> are named after: the English
/// and Dutch text of every code in <see cref="ProjectRefusals"/>, for the edge to phrase a refusal in its
/// reader's language. The module's own registration adds it to the toolkit's localizer.
/// </summary>
/// <remarks>
/// The files are beside the refusals they translate, so a new code and its two texts are one change in one
/// folder. The English file repeats the table's text, and a test holds the two equal: the domain says what is
/// wrong in its own words and never reads a resource, and a translator still has every text in one file per
/// language. The Dutch texts have no word for the reader, as the Tenancy package's have none.
/// </remarks>
public sealed class ProjectFailures;
