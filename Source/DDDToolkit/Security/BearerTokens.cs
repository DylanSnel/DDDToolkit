using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace DDDToolkit.Security;

/// <summary>
/// Bearer tokens for whatever a module hands out as a secret link or code: an invitation, a share, a feed. A
/// token is 32 random bytes written as 43 characters of base64url without padding. It is shown once and never
/// stored: a module keeps the SHA-256 digest of the 32 bytes, not of the text, and looks the token up by that.
/// <code>
/// var token = BearerTokens.New();
/// share.Digest = token.Digest;            // what is stored
/// return token.Token;                     // what the person gets, once
///
/// // later, with what the person sent back
/// if (!BearerTokens.TryDigest(sent, out var digest)) { /* not a token at all */ }
/// var row = await rows.SingleOrDefaultAsync(row =&gt; row.Digest == digest);
/// </code>
/// <para>
/// A stored digest gives nobody the token: 256 random bits cannot be guessed, and a digest of them cannot be
/// turned back, so a fast hash is enough and no salt is needed, unlike for a password a person chose. For the
/// same reason guessing needs no rate limit of its own.
/// </para>
/// <para>
/// A token has one spelling. Base64url lets several texts decode to the same bytes, with padding, with the
/// characters of plain base64, or with stray bits in the last character; all of those are refused, so a token
/// that was handed out is the only text that finds its row.
/// </para>
/// <para>
/// Keep the token out of everything that is kept or passed on: a log, an event, a refusal's arguments, the query
/// string of an address. It travels in a request's body, or in the fragment of a link, which a browser does not
/// send.
/// </para>
/// </summary>
public static class BearerTokens
{
    /// <summary>How many characters a token has.</summary>
    public const int TokenLength = 43;

    /// <summary>How many bytes a token's digest has.</summary>
    public const int DigestLength = 32;

    /// <summary>How many random bytes a token is made of.</summary>
    private const int ByteLength = 32;

    /// <summary>A new token, from the operating system's random number generator, with its digest.</summary>
    public static BearerToken New()
    {
        Span<byte> bytes = stackalloc byte[ByteLength];
        RandomNumberGenerator.Fill(bytes);
        return new BearerToken(Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>
    /// The digest of <paramref name="token"/>, when it is written the one way a token is written: exactly
    /// <see cref="TokenLength"/> characters of <c>A-Z a-z 0-9 - _</c>, no padding, and a last character that
    /// carries no stray bits. Anything else has no digest, so two spellings of one token cannot exist.
    /// </summary>
    /// <param name="token">What somebody sent as a token.</param>
    /// <param name="digest">Its digest, <see cref="DigestLength"/> bytes, when it is a token.</param>
    /// <returns>Whether <paramref name="token"/> is a token as <see cref="New"/> writes one.</returns>
    public static bool TryDigest(string? token, [NotNullWhen(true)] out byte[]? digest)
    {
        digest = null;
        if (token is not { Length: TokenLength })
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[ByteLength];
        if (!TryDecode(token, bytes))
        {
            return false;
        }

        digest = SHA256.HashData(bytes);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="token"/> is the token <paramref name="digest"/> was kept for, compared in a time
    /// that does not depend on where the two differ. False for anything that is not written as a token is.
    /// </summary>
    /// <param name="token">What somebody sent as a token.</param>
    /// <param name="digest">The digest that was stored when the token was made.</param>
    public static bool Matches(string? token, ReadOnlySpan<byte> digest)
        => TryDigest(token, out var sent) && CryptographicOperations.FixedTimeEquals(sent, digest);

    /// <summary>
    /// The bytes <paramref name="token"/> writes, when it writes them the one way: every character one of the
    /// alphabet's, and the last one with no bit set beyond the bytes. Decoded here, strictly, rather than by a
    /// decoder that accepts padding, white space or stray bits, or throws for what is no token.
    /// </summary>
    /// <param name="token">Exactly <see cref="TokenLength"/> characters.</param>
    /// <param name="bytes">Where the bytes go: exactly <see cref="ByteLength"/>.</param>
    private static bool TryDecode(ReadOnlySpan<char> token, Span<byte> bytes)
    {
        // Ten groups of four characters are thirty bytes.
        var written = 0;
        for (var read = 0; read < TokenLength - 3; read += 4)
        {
            var (first, second, third, fourth) = (ValueOf(token[read]), ValueOf(token[read + 1]), ValueOf(token[read + 2]), ValueOf(token[read + 3]));
            if ((first | second | third | fourth) < 0)
            {
                return false;
            }

            bytes[written++] = (byte)((first << 2) | (second >> 4));
            bytes[written++] = (byte)((second << 4) | (third >> 2));
            bytes[written++] = (byte)((third << 6) | fourth);
        }

        // The last three characters are the last two bytes, and two bits that belong to no byte and are zero.
        var (high, middle, low) = (ValueOf(token[^3]), ValueOf(token[^2]), ValueOf(token[^1]));
        if ((high | middle | low) < 0 || (low & 0b11) != 0)
        {
            return false;
        }

        bytes[written++] = (byte)((high << 2) | (middle >> 4));
        bytes[written] = (byte)((middle << 4) | (low >> 2));
        return true;
    }

    /// <summary>The six bits a character of base64url stands for, or -1 for any other character.</summary>
    private static int ValueOf(char character)
        => character switch
        {
            >= 'A' and <= 'Z' => character - 'A',
            >= 'a' and <= 'z' => character - 'a' + 26,
            >= '0' and <= '9' => character - '0' + 52,
            '-' => 62,
            '_' => 63,
            _ => -1,
        };
}
