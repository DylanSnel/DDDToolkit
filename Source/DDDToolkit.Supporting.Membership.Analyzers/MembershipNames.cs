using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DDDToolkit.Supporting.Membership.Analyzers;

/// <summary>
/// The name of a resource with members, as its codes and its functions are named, from the name of its class or
/// from what <c>[Members("...")]</c> says, and the scope of a module's system work from the module's name.
/// <para>
/// A name is words: a class's name is split where a lower case letter or a digit is followed by a capital, and
/// before the last capital of a run of them that a lower case letter follows (<c>HTMLPage</c> is <c>html</c>
/// <c>page</c>); <c>-</c> and <c>_</c> part words as well. The words are written in lower case, joined with
/// <c>-</c> for a code and a scope and with <c>_</c> for a function. A class's name is made plural on its last
/// word, by the plain English rules: <c>s</c>, <c>x</c>, <c>z</c>, <c>ch</c> and <c>sh</c> take <c>es</c>, a
/// consonant and <c>y</c> become <c>ies</c>, anything else takes <c>s</c>.
/// </para>
/// </summary>
internal static class MembershipNames
{
    /// <summary>The name of a resource whose class is called <paramref name="className"/>: the plural of its words.</summary>
    /// <returns>The name as a code is written, <c>crane-boards</c>; null for a class name this cannot read.</returns>
    public static string? OfClass(string className)
    {
        var words = Words(className);
        if (words is null)
        {
            return null;
        }

        words[words.Count - 1] = Plural(words[words.Count - 1]);
        return string.Join("-", words);
    }

    /// <summary>A name as <c>[Members]</c> or a module says it, converted as a class's is but never made plural.</summary>
    /// <returns>The name as a code is written; null where it holds a space, a symbol, or a digit first.</returns>
    public static string? Converted(string? said)
        => said is null || Words(said) is not { } words ? null : string.Join("-", words);

    /// <summary>The words of <paramref name="name"/> in lower case, or null where it is no name this can read.</summary>
    private static List<string>? Words(string name)
    {
        if (name.Length == 0 || !char.IsLetter(name[0]) || name[name.Length - 1] is '-' or '_')
        {
            return null;
        }

        var words = new List<string>();
        foreach (var part in name.Split('-', '_'))
        {
            if (part.Length == 0 || !part.All(static character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                return null;
            }

            var word = new StringBuilder();
            for (var index = 0; index < part.Length; index++)
            {
                var character = part[index];
                var startsWord = index > 0
                                 && char.IsUpper(character)
                                 && (!char.IsUpper(part[index - 1]) || (index + 1 < part.Length && char.IsLower(part[index + 1])));
                if (startsWord && word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }

                word.Append(char.ToLowerInvariant(character));
            }

            words.Add(word.ToString());
        }

        return words;
    }

    private static string Plural(string word)
    {
        if (word.EndsWith("s") || word.EndsWith("x") || word.EndsWith("z") || word.EndsWith("ch") || word.EndsWith("sh"))
        {
            return word + "es";
        }

        return word.Length > 1 && word[word.Length - 1] == 'y' && "aeiou".IndexOf(word[word.Length - 2]) < 0
            ? word.Substring(0, word.Length - 1) + "ies"
            : word + "s";
    }
}
