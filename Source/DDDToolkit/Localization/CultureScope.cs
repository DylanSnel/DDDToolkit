using System.Globalization;

namespace DDDToolkit.Localization;

/// <summary>
/// Makes a culture the current culture and the current UI culture of this flow of work until the scope is
/// disposed, and then puts back the two that were current before, also when the work inside threw.
/// <para>
/// It is for a text written outside a request, such as a mail, a notice or the result of a job, in the
/// language of whoever will read it. Inside a request the request localization middleware has chosen the
/// culture already. Outside one there is no reader to ask, so the code that writes the text says whose
/// language it is; the domain stays language-free either way.
/// </para>
/// <code>
/// using (CultureScope.Use(reader.Language))
/// {
///     mail.Subject = texts["report-ready.subject"];
///     mail.Body = localizer.Localize(refusal);
/// }
/// </code>
/// <para>
/// The current culture flows with the work: it follows an <see langword="await"/> inside the scope, and work
/// started inside the scope starts with it. It does not reach work that was already running, and it does not
/// flow back out of an <see langword="async"/> method to its caller. Open and dispose a scope in the same
/// method, with <see langword="using"/>; a scope opened in an <see langword="async"/> method and left open
/// is gone when that method returns.
/// </para>
/// </summary>
public static class CultureScope
{
    /// <summary>Makes <paramref name="culture"/> current until the scope is disposed.</summary>
    /// <param name="culture">
    /// The reader's culture. It becomes <see cref="CultureInfo.CurrentUICulture"/>, which decides the language
    /// a text is looked up in, and <see cref="CultureInfo.CurrentCulture"/>, which decides how the numbers and
    /// dates in it are written.
    /// </param>
    /// <returns>The scope. Disposing it puts back the cultures that were current when it was opened; disposing it again does nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public static IDisposable Use(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var scope = new Scope(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return scope;
    }

    /// <summary>Makes the culture called <paramref name="cultureName"/> current until the scope is disposed.</summary>
    /// <param name="cultureName">The culture's name, such as <c>nl</c> or <c>en-GB</c>, as a seat or a tenant keeps it.</param>
    /// <returns>The scope. Disposing it puts back the cultures that were current when it was opened; disposing it again does nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cultureName"/> is <see langword="null"/>.</exception>
    /// <exception cref="CultureNotFoundException"><paramref name="cultureName"/> names no culture.</exception>
    public static IDisposable Use(string cultureName)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        // Only a culture the platform knows: on Linux, ICU would otherwise make one up for any well-formed name.
        return Use(CultureInfo.GetCultureInfo(cultureName, predefinedOnly: true));
    }

    /// <summary>Remembers the two cultures a scope replaced, and puts them back once.</summary>
    private sealed class Scope(CultureInfo culture, CultureInfo uiCulture) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
