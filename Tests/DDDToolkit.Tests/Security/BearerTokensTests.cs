using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Security;
using FluentAssertions;

namespace DDDToolkit.Tests.Security;

/// <summary>
/// A bearer token is 32 random bytes written one way, and what is kept of it is the digest of those bytes: a
/// stored digest finds the token and gives nobody the token, and no second spelling of a token finds its row.
/// </summary>
public class BearerTokensTests
{
    [Fact]
    public void A_new_token_is_43_characters_of_base64url_and_its_digest_32_bytes()
    {
        var token = BearerTokens.New();

        token.Token.Should().HaveLength(BearerTokens.TokenLength).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
        token.Digest.Should().HaveCount(BearerTokens.DigestLength);

        BearerTokens.TryDigest(token.Token, out var digest).Should().BeTrue("a token as it was handed out is a token");
        digest.Should().Equal(token.Digest);
        BearerTokens.Matches(token.Token, token.Digest).Should().BeTrue();
    }

    [Fact]
    public void The_digest_is_of_the_decoded_bytes_not_of_the_text()
    {
        var token = BearerTokens.New();
        var bytes = Base64Url.DecodeFromChars(token.Token);

        bytes.Should().HaveCount(32);
        token.Digest.Should().Equal(SHA256.HashData(bytes));
        token.Digest.Should().NotEqual(SHA256.HashData(Encoding.UTF8.GetBytes(token.Token)), "the text is one way of writing the bytes, and the bytes are the secret");
    }

    [Theory]
    [InlineData("padded")]
    [InlineData("plus")]
    [InlineData("slash")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("stray bits")]
    [InlineData("space")]
    public void A_non_canonical_spelling_has_no_digest(string spelling)
    {
        // Bytes whose text has a '-' and a '_' in it, so the plain base64 spelling of the same bytes differs.
        var bytes = new byte[32];
        Array.Fill(bytes, (byte)0xFB);
        bytes[1] = 0xEF;
        bytes[2] = 0xFF;
        var token = Base64Url.EncodeToString(bytes);
        token.Should().HaveLength(43).And.Contain("-").And.Contain("_");
        BearerTokens.TryDigest(token, out _).Should().BeTrue("the bytes written the one way are a token");

        var other = spelling switch
        {
            "padded" => token + "=",
            "plus" => token.Replace('-', '+'),
            "slash" => token.Replace('_', '/'),
            "short" => token[..^1],
            "long" => token + "A",

            // The last character carries four bits of the last byte and two that mean nothing: the same bytes
            // with one of those two set decode alike, and are another text.
            "stray bits" => token[..^1] + StrayBitsOf(token[^1]),
            _ => token[..^1] + " ",
        };

        other.Should().NotBe(token);
        BearerTokens.TryDigest(other, out var digest).Should().BeFalse(spelling + " is not how a token is written");
        digest.Should().BeNull();
        BearerTokens.Matches(other, SHA256.HashData(bytes)).Should().BeFalse();
    }

    [Fact]
    public void Matches_is_false_for_another_token_and_a_malformed_one()
    {
        var token = BearerTokens.New();
        var another = BearerTokens.New();

        BearerTokens.Matches(another.Token, token.Digest).Should().BeFalse();
        BearerTokens.Matches(null, token.Digest).Should().BeFalse();
        BearerTokens.Matches(string.Empty, token.Digest).Should().BeFalse();
        BearerTokens.Matches("some words, and no token", token.Digest).Should().BeFalse();
        BearerTokens.Matches(token.Token, token.Digest.AsSpan(0, 16)).Should().BeFalse("half a digest is no digest");
        BearerTokens.TryDigest(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Two_new_tokens_differ()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => BearerTokens.New()).ToList();

        tokens.Select(token => token.Token).Should().OnlyHaveUniqueItems();
        tokens.Select(token => Convert.ToHexString(token.Digest)).Should().OnlyHaveUniqueItems();

        // Each is read back to the digest it was made with, whatever its bytes.
        tokens.Should().OnlyContain(token => BearerTokens.Matches(token.Token, token.Digest));
    }

    [Fact]
    public void A_token_does_not_print_itself()
    {
        var token = BearerTokens.New();

        token.ToString().Should().Be("BearerToken").And.NotContain(token.Token);
        $"issued {token}".Should().NotContain(token.Token, "a token interpolated into a log line by mistake stays out of the log");
    }

    /// <summary>The character that writes the same four bits as <paramref name="last"/> with a stray bit set after them.</summary>
    private static char StrayBitsOf(char last)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var value = Alphabet.IndexOf(last);
        value.Should().BeGreaterThanOrEqualTo(0);
        (value % 4).Should().Be(0, "a token's last character has no stray bits");
        return Alphabet[value + 1];
    }
}
