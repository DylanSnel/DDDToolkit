using System.Net;
using System.Net.Http.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Host.Languages;

/// <summary>
/// A refusal is answered in the language its request asks for, in its <c>Accept-Language</c> header: Dutch or
/// English, and English when it asks for neither. Only the text changes. The status, the code and the arguments
/// are the same in every language, so a client that branches on them never reads a sentence.
/// </summary>
/// <remarks>Nothing here changes data: every request is one the API refuses. So the tests share one host.</remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class LanguageScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task The_same_refused_request_is_answered_in_dutch_or_in_english_as_it_asks()
    {
        // Vic observes Pier 7's crew and may change nothing, so renaming it is refused by Projects.
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        var dutch = await RenameAsync(vic, "nl");
        var english = await RenameAsync(vic, "en");

        dutch.Title.Should().Be("Hiervoor is op dit project het recht projects.edit nodig.");
        english.Title.Should().Be("Doing this to the project needs the key projects.edit.");
        dutch.Argument("Key").Should().Be(english.Argument("Key")).And.Be(ProjectKeys.Edit, "the arguments are values, the same in every language");

        // A regional variant reads its language; a language the sample has no texts in, and no header at all, read English.
        (await RenameAsync(vic, "nl-BE")).Title.Should().Be(dutch.Title);
        (await RenameAsync(vic, "fr, nl;q=0.5")).Title.Should().Be(dutch.Title, "the first language of the header the sample has is the one answered in");
        (await RenameAsync(vic, "fr")).Title.Should().Be(english.Title);
        (await RenameAsync(vic, null)).Title.Should().Be(english.Title);
    }

    [Fact]
    public async Task The_packages_texts_and_the_hosts_own_follow_the_language_too()
    {
        // The Tenancy package's text, refused at the door of every route in a tenant: seth's seat is suspended.
        using var seth = await sample.ClientAsync("seth", Harbor.Slug);
        using var me = await SendAsync(seth, HttpMethod.Get, "/me", "nl");
        (await me.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SeatSuspended)).Title.Should().Be("Deze plaats in de tenant is geschorst.");

        // The host's own title, for an id that does not parse. From vic, who has a seat: a route reads its
        // arguments only after the seat policy let the caller in.
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);
        using var unreadable = await SendAsync(vic, HttpMethod.Get, "/projects/not-an-id", "nl");
        (await unreadable.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest)).Title.Should().Be("Het verzoek kon niet worden gelezen.");

        // A language is the request's own: the next one, asking for English, is answered in English.
        using var again = await SendAsync(seth, HttpMethod.Get, "/me", "en");
        (await again.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SeatSuspended)).Title
            .Should().Be(TenancyRefusals.Of(TenancyRefusals.SeatSuspended).Message);
    }

    [Fact]
    public async Task The_seat_a_request_lacks_is_said_in_its_language_by_a_route_and_by_graphql_alike()
    {
        // Rhea has no seat in meadow. A route is refused by the seat policy, and a root field of the GraphQL
        // schema by the gate in front of it: one refusal, so one text, in the language the request asks for.
        using var rhea = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);
        rhea.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl");

        using var route = await rhea.GetAsync("/me", Cancellation);
        var refused = await route.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        refused.Title.Should().Be("Geen plaats in de gevraagde tenant.");

        // A query answers it as an error of the field, a mutation as a typed error in its payload.
        var query = (await rhea.GraphQLAsync("{ projects { nodes { id } } }")).SingleError();
        query.Code().Should().Be(TenancyRefusals.NotSeated);
        query.GetProperty("message").GetString().Should().Be(refused.Title);

        var mutation = (await rhea.GraphQLDataAsync(
            $$"""
            mutation($unit: UUID!) {
              projectOpenAtUnit(input: { number: "P-100", name: "Harbor wall", unitId: $unit }) { project { id } {{SampleGraphQLCalls.Errors}} }
            }
            """,
            new { unit = Harbor.UnitNamed("North Coast").Value })).GetProperty("projectOpenAtUnit");
        var error = mutation.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("code").GetString().Should().Be(TenancyRefusals.NotSeated);
        error.GetProperty("message").GetString().Should().Be(refused.Title);

        // The same request in English reads the package's own English.
        using var english = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);
        (await english.GraphQLAsync("{ projects { nodes { id } } }")).SingleError().GetProperty("message").GetString()
            .Should().Be(TenancyRefusals.Of(TenancyRefusals.NotSeated).Message);
    }

    private static async Task<Problem> RenameAsync(HttpClient client, string? acceptLanguage)
    {
        var pierSeven = Harbor.ProjectNamed("Pier 7").Id.Value;
        using var response = await SendAsync(client, HttpMethod.Put, $"/projects/{pierSeven}/name", acceptLanguage, new { name = "Pier Seven" });

        return await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? acceptLanguage, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage).Should().BeTrue();
        }

        return client.SendAsync(request, Cancellation);
    }
}
