using System.Globalization;
using System.Text;

namespace DDDToolkit.Localization;

/// <summary>
/// Fills a translated template such as <c>"{PropertyName} is at most {MaxLength} characters."</c> with a
/// failure's values.
/// <para>
/// Named rather than positional (<c>{0}</c>) placeholders, because a translator reorders a sentence and
/// should not have to know in which order the code happened to supply its values. <c>{Name:format}</c>
/// applies a .NET format string, <c>{{</c> and <c>}}</c> are literal braces, and a placeholder with no
/// value is left as it was written, so a typo in a translation shows up on screen rather than as an
/// exception on the failure path.
/// </para>
/// </summary>
internal static class FailureTemplate
{
    /// <summary>Resolves a placeholder's value by name. Returns false when the name is unknown.</summary>
    public delegate bool Resolver(string name, out object? value);

    public static string Format(string template, Resolver resolve)
    {
        if (template.IndexOf('{') < 0 && template.IndexOf('}') < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 16);
        var index = 0;

        while (index < template.Length)
        {
            var character = template[index];

            if (character == '{' && index + 1 < template.Length && template[index + 1] == '{')
            {
                result.Append('{');
                index += 2;
                continue;
            }

            if (character == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                result.Append('}');
                index += 2;
                continue;
            }

            var close = character == '{' ? template.IndexOf('}', index + 1) : -1;
            if (close < 0)
            {
                result.Append(character);
                index++;
                continue;
            }

            var placeholder = template.AsSpan(index + 1, close - index - 1);
            var colon = placeholder.IndexOf(':');
            var name = (colon < 0 ? placeholder : placeholder[..colon]).Trim().ToString();
            var format = colon < 0 ? null : placeholder[(colon + 1)..].ToString();

            if (name.Length > 0 && resolve(name, out var value))
            {
                result.Append(Render(value, format));
            }
            else
            {
                result.Append(template, index, close - index + 1);
            }

            index = close + 1;
        }

        return result.ToString();
    }

    private static string Render(object? value, string? format) => value switch
    {
        null => string.Empty,
        Type type => type.Name,
        IFormattable formattable => formattable.ToString(format, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
