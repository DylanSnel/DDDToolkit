using System.Globalization;
using System.Text;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Reads and fills the text of a refusal an index declares, such as
/// <c>"Number {Number} is taken."</c>. The placeholders are written as a translation writes them for
/// <c>DDDToolkit.Localization</c>: <c>{Name}</c>, <c>{Name:format}</c> for a .NET format string, and <c>{{</c>
/// and <c>}}</c> for literal braces, so one text serves as the message and as the neutral resource.
/// </summary>
internal static class RefusalTemplate
{
    /// <summary>The names the text's placeholders carry, each once, in the order they first appear.</summary>
    public static IReadOnlyList<string> Placeholders(string template)
    {
        var names = new List<string>();
        Walk(template, literal: null, (name, _, _) =>
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        });

        return names;
    }

    /// <summary>
    /// The text with each placeholder that <paramref name="values"/> knows replaced by its value, in the
    /// invariant culture, because the text is the domain's own and reads the same wherever it runs. A
    /// placeholder with no value is left as it was written.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, object?> values)
    {
        if (template.IndexOf('{') < 0 && template.IndexOf('}') < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        Walk(
            template,
            literal: (text, start, length) => result.Append(text, start, length),
            (name, format, written) =>
            {
                if (values.TryGetValue(name, out var value))
                {
                    result.Append(Render(value, format));
                }
                else
                {
                    result.Append(written);
                }
            });

        return result.ToString();
    }

    private delegate void Literal(string text, int start, int length);

    private delegate void Placeholder(string name, string? format, ReadOnlySpan<char> written);

    private static void Walk(string template, Literal? literal, Placeholder placeholder)
    {
        var index = 0;
        while (index < template.Length)
        {
            var character = template[index];

            if (character is '{' or '}' && index + 1 < template.Length && template[index + 1] == character)
            {
                literal?.Invoke(template, index, 1);
                index += 2;
                continue;
            }

            var close = character == '{' ? template.IndexOf('}', index + 1) : -1;
            if (close < 0)
            {
                literal?.Invoke(template, index, 1);
                index++;
                continue;
            }

            var inside = template.AsSpan(index + 1, close - index - 1);
            var colon = inside.IndexOf(':');
            var name = (colon < 0 ? inside : inside[..colon]).Trim().ToString();
            if (name.Length > 0)
            {
                placeholder(name, colon < 0 ? null : inside[(colon + 1)..].ToString(), template.AsSpan(index, close - index + 1));
            }
            else
            {
                literal?.Invoke(template, index, close - index + 1);
            }

            index = close + 1;
        }
    }

    private static string Render(object? value, string? format) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
