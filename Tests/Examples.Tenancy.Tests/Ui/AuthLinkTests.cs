using Examples.Tenancy.Ui.Auth;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// What the page that accepts an invitation reads off the address it was opened with: the invitation's token,
/// the sign-in Supabase Auth added behind it, or Auth's refusal. Against Supabase's own Auth server the same is
/// read from the address Auth really sends a browser to, in <see cref="InvitedByMailTests"/>.
/// </summary>
public sealed class AuthLinkTests
{
    private const string Page = "http://localhost:5091/invitations/accept";

    [Fact]
    public void A_link_somebody_handed_over_carries_the_token_alone()
    {
        var link = AuthLink.Read(Page + "#q83nA-b_7");

        link.Should().Be(new AuthLink("q83nA-b_7", null, null, null, null));
        link.IsEmpty.Should().BeFalse();
        AuthLink.Read(Page).IsEmpty.Should().BeTrue();
        AuthLink.Read(Page + "#").IsEmpty.Should().BeTrue();
        AuthLink.Read(null).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_link_from_auths_mail_carries_the_token_and_the_sign_in_behind_it()
    {
        var link = AuthLink.Read(Page + "#q83nA-b_7#access_token=header.payload.signature&expires_at=1790000000&expires_in=3600&refresh_token=not-read&token_type=bearer&type=invite");

        link.Should().Be(new AuthLink("q83nA-b_7", "header.payload.signature", DateTimeOffset.FromUnixTimeSeconds(1790000000), AuthLink.Invitation, null));

        // A sign-in with no token in front of it, as Auth sends where the host named no page for the token.
        AuthLink.Read(Page + "#access_token=header.payload.signature&expires_at=1790000000&type=invite")
            .Should().Be(link with { Token = null });

        // What it carries is in nothing it prints.
        link.ToString().Should().NotContain("q83nA").And.NotContain("header");
    }

    [Fact]
    public void A_link_auth_did_not_take_carries_its_refusal_and_nothing_to_sign_in_with()
    {
        var link = AuthLink.Read(Page + "#error=access_denied&error_code=otp_expired&error_description=Email+link+is+invalid+or+has+expired");

        link.Should().Be(new AuthLink(null, null, null, null, "otp_expired"));

        // A moment that is no moment is none: the page then starts no session.
        AuthLink.Read(Page + "#access_token=a.b.c&expires_at=soon").Expires.Should().BeNull();
        AuthLink.Read(Page + "#access_token=a.b.c&expires_at=99999999999999999").Expires.Should().BeNull();
    }
}
