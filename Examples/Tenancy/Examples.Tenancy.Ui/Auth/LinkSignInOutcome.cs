using Examples.Tenancy.Ui.Api;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>What came of a link's sign-in. At most one of the last three says why nobody was signed in.</summary>
/// <param name="SignedIn">Whether the tab's session is now the link's person.</param>
/// <param name="ChoosesPassword">Whether that person came by an invitation's mail, and so has no password yet.</param>
/// <param name="AsksToSignInAs">
/// The address the link would sign in, when the tab is signed in as somebody else: nothing changed, and the page
/// asks. <see langword="null"/> otherwise.
/// </param>
/// <param name="Refusal">Why Auth did not take the link's sign-in, in the session's language, or <see langword="null"/>.</param>
/// <param name="TokenRefused">The API's answer when it did not take the token Auth vouched for, or <see langword="null"/>.</param>
public sealed record LinkSignInOutcome(
    bool SignedIn = false,
    bool ChoosesPassword = false,
    string? AsksToSignInAs = null,
    string? Refusal = null,
    ApiOutcome? TokenRefused = null);
