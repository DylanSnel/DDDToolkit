namespace Examples.Tenancy.Tenants.Application.Invitations;

/// <summary>
/// Where a person accepts an invitation: the application's own page for it. The module knows no user interface,
/// so a host that has such a page says where it is, through the module's entry.
/// </summary>
/// <remarks>
/// With it, the mail the identity provider sends a new account leads to that page with the invitation's token
/// after the <c>#</c>, so the person needs nothing from whoever invited them. Without it the mail only signs the
/// person in, and whoever invited hands the token over.
/// <para>
/// The price is that the provider is handed the token: it gets the page's address with the token in it, as a
/// value in the address of the request that asks for the mail, which its request log keeps, and puts it in the
/// mail. An invitation that is mailed this way rests on the sign-in with the invited address, which accepting
/// takes as well, more than on the token.
/// </para>
/// </remarks>
public sealed record InvitationPage
{
    /// <summary>The page at <paramref name="address"/>.</summary>
    /// <param name="address">The page's address as a browser reaches it: absolute, http or https, and without a fragment, which is the token's place.</param>
    /// <exception cref="ArgumentException"><paramref name="address"/> is not such an address.</exception>
    public InvitationPage(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) || address.Fragment.Length > 0)
        {
            throw new ArgumentException("The page that accepts an invitation has to be an absolute http or https address without a fragment.", nameof(address));
        }

        Address = address;
    }

    /// <summary>The page's address.</summary>
    public Uri Address { get; }

    /// <summary>
    /// The address that opens the page with <paramref name="token"/> after the <c>#</c>. A browser sends that
    /// part of an address to no server, so opening the page tells the page's own server nothing. Whoever is
    /// handed this address holds the token, though: the identity provider is, to put it in its mail, and what it
    /// is sent its request log keeps. A token is letters, digits, <c>-</c> and <c>_</c>, so it is a fragment as it
    /// is.
    /// </summary>
    /// <param name="token">The invitation's token.</param>
    public Uri With(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new UriBuilder(Address) { Fragment = token }.Uri;
    }
}
