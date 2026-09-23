using DDDToolkit.Invariants;
using DDDToolkit.Validation;

namespace DDDToolkit.Localization;

/// <summary>
/// Phrases a failure in the reader's language.
/// <para>
/// The domain reports what is wrong with a stable code, a message in its own words and the values the
/// message was built from. None of that depends on who is reading, and it must not: an invariant runs on
/// a save in a background job as readily as in a request, and there is no reader there to ask. Choosing
/// a language is the edge's job, at the moment a failure becomes a response, and this is the one thing
/// the edge calls to do it.
/// </para>
/// <para>
/// The language is <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>, the same one
/// <c>IStringLocalizer</c> reads, so ASP.NET Core's request localization middleware sets it per request
/// with nothing else to wire. Values in the message are formatted with
/// <see cref="System.Globalization.CultureInfo.CurrentCulture"/>.
/// </para>
/// <para>
/// The interface lives here, in the core, so an integration such as <c>DDDToolkit.HotChocolate</c> can
/// ask for one without depending on how translations are stored. The implementation that reads them
/// through <c>IStringLocalizer</c> is <c>FailureLocalizer</c> in <c>DDDToolkit.Localization</c>; an
/// application that registers none gets the domain's own messages.
/// </para>
/// </summary>
public interface IFailureLocalizer
{
    /// <summary>
    /// The failure in the reader's language, or its own <see cref="ValidationError.Message"/> when no
    /// translation is known for its code.
    /// </summary>
    /// <param name="error">The failure to phrase.</param>
    string Localize(ValidationError error);

    /// <summary>
    /// The violation in the reader's language, or its own <see cref="InvariantViolation.Message"/> when no
    /// translation is known for its code.
    /// </summary>
    /// <param name="violation">The violation to phrase.</param>
    string Localize(InvariantViolation violation);
}
