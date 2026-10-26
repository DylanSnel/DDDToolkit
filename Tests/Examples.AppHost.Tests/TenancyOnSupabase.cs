using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Examples.AppHost.Tests;

/// <summary>
/// The Tenancy sample's AppHost, started as a person starts it, with no switch to set: Supabase's own Postgres
/// and Auth images and a mail catcher, the database made from <c>Examples/Tenancy/supabase/migrations</c> by the
/// role that owns it, and the API logged in as a role that owns nothing. The one thing it is given is the
/// password of the demonstration people, which a person reads from the dashboard and a test has to know.
/// </summary>
public sealed class TenancyOnSupabaseImages : ShopFixture<Projects.Examples_Tenancy_AppHost>
{
    /// <summary>The password the demonstration people sign in with at Auth, for this run: given to the AppHost as its parameter.</summary>
    public static string DemoPassword { get; } = "Pw" + Guid.NewGuid().ToString("N");

    protected override string ShopResource => "api";

    protected override IEnumerable<string> ResourcesToWaitFor => ["api", "ui"];

    protected override string[] Arguments => ["--Parameters:demo-password=" + DemoPassword];
}

/// <summary>
/// The Tenancy sample, whole, on Supabase's own images, which is the one way its AppHost runs it. That the API
/// answers at all says the migration step ran and the login role was turned on: the API applies no migration,
/// and does not start as the role that owns the database. What is asked here is the real sign-in, and where the
/// link in the mail of an invitation leads when it is opened as it is written, across the containers and the two
/// processes.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.AppHost")]
public sealed class TenancyOnSupabase(TenancyOnSupabaseImages sample) : IClassFixture<TenancyOnSupabaseImages>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_demonstration_person_signs_in_at_the_auth_server_and_reads_her_projects()
    {
        sample.SkipUnlessStarted();

        // At Auth, with her address and the password the API gave her when it started.
        using var auth = sample.Client("auth");
        using var signIn = await auth.PostAsJsonAsync("token?grant_type=password", new { email = "rhea@example.test", password = TenancyOnSupabaseImages.DemoPassword }, Cancellation);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync(Cancellation));
        var token = (await signIn.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("access_token").GetString();

        // Auth signed it with the key the AppHost gave it for this run, and not with the secret the dev login
        // signs with: the API takes it by the key Auth publishes, or not at all.
        using (var header = JsonDocument.Parse(Base64Url.DecodeFromChars(token.AsSpan(0, token!.IndexOf('.')))))
        {
            header.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        }

        // At the API, with Auth's token: the projects her seat reaches, under the policies.
        using var api = sample.Client();
        (await ProjectsAsync(api, token)).Should().BeEquivalentTo("Pier 7", "Inland depot");

        // And the dev login beside it, for the same person.
        using var devLogin = await api.PostAsJsonAsync("/dev/auth/token", new { person = "rhea" }, Cancellation);
        devLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var devToken = (await devLogin.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("access_token").GetString();
        (await ProjectsAsync(api, devToken)).Should().BeEquivalentTo("Pier 7", "Inland depot");
    }

    [Fact]
    public async Task The_mail_of_an_invitation_leads_to_the_uis_page_that_accepts_it()
    {
        sample.SkipUnlessStarted();

        // tove, meadow's administrator, invites an address Auth has never seen, into meadow's root as a surveyor.
        using var api = sample.Client();
        using var devLogin = await api.PostAsJsonAsync("/dev/auth/token", new { person = "tove" }, Cancellation);
        var address = $"wren-{Guid.NewGuid():N}@example.test";
        using var invite = new HttpRequestMessage(HttpMethod.Post, "/tenancy/invitations")
        {
            Content = JsonContent.Create(new { address, unitId = "b0000000-0000-4000-8000-000000000201", roleId = "e0000000-0000-4000-8000-000000000204" }),
        };
        invite.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await devLogin.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("access_token").GetString());
        invite.Headers.Add("Tenant", "meadow");
        using var invited = await api.SendAsync(invite, Cancellation);
        invited.StatusCode.Should().Be(HttpStatusCode.OK, await invited.Content.ReadAsStringAsync(Cancellation));
        var token = (await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("token").GetString();

        // Auth mails the address a link, and the link is taken exactly as the mail carries it. It names Auth's own
        // address on this machine and the path Auth answers itself, so nothing has to stand between a person who
        // opens it and Auth.
        using var mail = sample.Client("mail");
        var written = Regex.Match(await TextOfTheMailToAsync(mail, address), @"http://[^\s)]+/verify\?[^\s)]+");
        written.Success.Should().BeTrue("the mail carries Auth's link");
        var link = new Uri(WebUtility.HtmlDecode(written.Value));

        using var auth = sample.Client("auth");
        link.GetLeftPart(UriPartial.Authority).Should().Be(auth.BaseAddress!.GetLeftPart(UriPartial.Authority), "the link names the scheme, the host and the port Auth answers on, on this machine");
        link.AbsolutePath.Should().Be("/verify", "that is the path Auth answers without a gateway in front of it");

        // Auth proves the address and sends the browser on: to the UI's page, because the API was told where it
        // is and it is on Auth's site, with the invitation's token and the sign-in after the '#'. The browser
        // here knows no address of its own and follows nothing, so what is opened is the link and what is read
        // is Auth's answer to it.
        using var ui = sample.Client("ui");
        using var browser = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        using var answer = await browser.GetAsync(link, Cancellation);
        answer.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        answer.Headers.Location!.OriginalString.Should().StartWith(new Uri(ui.BaseAddress!, "invitations/accept") + "#" + token + "#access_token=");
    }

    /// <summary>The text of the one mail the catcher holds for <paramref name="address"/>, once it has arrived.</summary>
    private static async Task<string> TextOfTheMailToAsync(HttpClient mail, string address)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var found = await mail.GetFromJsonAsync<JsonElement>("api/v1/search?query=" + Uri.EscapeDataString("to:" + address), Cancellation);
            if (found.GetProperty("messages").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } summary)
            {
                return (await mail.GetFromJsonAsync<JsonElement>("api/v1/message/" + summary.GetProperty("ID").GetString(), Cancellation)).GetProperty("Text").GetString()!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
        }

        throw new TimeoutException("Auth sent " + address + " no mail within ten seconds.");
    }

    private static async Task<IEnumerable<string?>> ProjectsAsync(HttpClient api, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/projects");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Tenant", "harbor");

        using var response = await api.SendAsync(request, Cancellation);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancellation));
        // A page: its items, and no marker for a next one, since her projects fit on the first.
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        page.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);
        return [.. page.GetProperty("items").EnumerateArray().Select(project => project.GetProperty("name").GetString())];
    }
}
