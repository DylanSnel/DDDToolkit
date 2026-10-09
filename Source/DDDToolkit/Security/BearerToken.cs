namespace DDDToolkit.Security;

/// <summary>
/// A bearer token as <see cref="BearerTokens.New"/> makes it: the text to hand out, once, and the digest to keep.
/// </summary>
/// <param name="Token">
/// The token, <see cref="BearerTokens.TokenLength"/> characters of base64url. Whoever holds it is let in, so it
/// is handed to the one person meant to have it and kept nowhere: not in a store, a log, an event or a refusal's
/// arguments.
/// </param>
/// <param name="Digest">
/// The SHA-256 digest of the token's bytes, <see cref="BearerTokens.DigestLength"/> bytes: what a module stores,
/// and finds the token's row by.
/// </param>
public readonly record struct BearerToken(string Token, byte[] Digest)
{
    /// <summary>
    /// Says that this is a token and not which: a record prints its members by default, and a token that ends up
    /// in a log line through an interpolated string is a token given away.
    /// </summary>
    public override string ToString() => nameof(BearerToken);
}
