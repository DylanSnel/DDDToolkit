using System.Globalization;
using System.Net;
using System.Resources;
using Examples.Tenancy.Ui.Session;
using Microsoft.AspNetCore.Components;

namespace Examples.Tenancy.Ui.Languages;

/// <summary>
/// The UI's own texts, in the language the session reads: every heading, label and explanation of its pages, from
/// <c>UiTexts.resx</c> (English) and <c>UiTexts.nl.resx</c> (Dutch) beside this class.
/// </summary>
/// <remarks>
/// <para>
/// The language is named with every lookup, taken from the <see cref="UiSession"/>, and never read from the
/// current culture. A circuit keeps the culture it started with, so a switch in the top bar would not reach it;
/// and one tab reading Dutch must not turn another English.
/// </para>
/// <para>
/// What the API says is not here. A refusal's text comes from the API in the language the call asked for, and a
/// status, a key or the name a tenant gave a role is data, shown as it is. The demonstration's own data, the
/// people of the dev login and the titles of the try-it presets, is the Host's and stays English.
/// </para>
/// <para>
/// The Dutch texts have no word for the reader: they say what is the case or what is needed.
/// </para>
/// </remarks>
public sealed class UiTexts(UiSession session)
{
    private static readonly ResourceManager Resources = new(typeof(UiTexts));

    /// <summary>The text <paramref name="key"/> names, in the session's language.</summary>
    /// <param name="key">The text's name in the resource files, such as <c>login.title</c>.</param>
    public string this[string key] => For(key, session.Language);

    /// <summary>The text <paramref name="key"/> names, with <paramref name="values"/> in its numbered places.</summary>
    /// <param name="key">The text's name in the resource files.</param>
    /// <param name="values">What fills <c>{0}</c>, <c>{1}</c> and so on.</param>
    public string this[string key, params object?[] values]
        => string.Format(CultureInfo.InvariantCulture, this[key], values);

    /// <summary>
    /// A text that holds markup of its own, such as <c>&lt;code&gt;</c> around a route, with
    /// <paramref name="values"/> in its numbered places. The values are encoded, so a name the API sent is shown
    /// as text whatever it holds; the text itself is the UI's own and is trusted.
    /// </summary>
    /// <param name="key">The text's name in the resource files.</param>
    /// <param name="values">What fills <c>{0}</c>, <c>{1}</c> and so on.</param>
    public MarkupString Html(string key, params object?[] values)
        => new(string.Format(
            CultureInfo.InvariantCulture,
            this[key],
            [.. values.Select(value => WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture)))]));

    /// <summary>
    /// The text <paramref name="key"/> names in <paramref name="language"/>, for code that has no circuit's
    /// scope to take this class from. A name the files do not know is returned as it is, so a typing mistake
    /// shows on screen instead of failing the page.
    /// </summary>
    /// <param name="key">The text's name in the resource files.</param>
    /// <param name="language">One of <see cref="UiSession.Languages"/>.</param>
    public static string For(string key, string language)
        => Resources.GetString(key, CultureInfo.GetCultureInfo(language)) ?? key;
}
