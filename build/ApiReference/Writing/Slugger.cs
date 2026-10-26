using System.Globalization;
using System.Text;

namespace DDDToolkit.Build.ApiReference.Writing;

/// <summary>
/// The anchors of a page's headings, made as github-slugger makes them, which both Docusaurus and GitHub use: the
/// heading's text lower-cased, every character but a letter, a digit, a mark, <c>-</c>, <c>_</c> and a space dropped,
/// each space a <c>-</c>, and a repeat numbered. A link written here to a heading only lands if it is the anchor the
/// site gives that heading, so every heading of a page is made through one instance, in page order.
/// </summary>
internal sealed class Slugger
{
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    /// <summary>The anchor of the next heading whose text, inline code included, is <paramref name="text"/>.</summary>
    public string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in text.ToLowerInvariant())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (char.IsLetterOrDigit(character)
                || character is '-' or '_' or ' '
                || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                builder.Append(character == ' ' ? '-' : character);
            }
        }

        var original = builder.ToString();
        var slug = original;
        while (_occurrences.ContainsKey(slug))
        {
            _occurrences[original]++;
            slug = original + "-" + _occurrences[original];
        }

        _occurrences[slug] = 0;
        return slug;
    }
}
