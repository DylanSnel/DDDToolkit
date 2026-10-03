using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Bodies that leave out what a command cannot do without. Each is a 400 <c>invalid-request</c> that says so, never
/// a command run with an empty id, which would answer with a refusal about a unit or seat nobody named, and never a
/// role's keys emptied because a field was misspelt. A question to the directory without its ids is the same: a
/// 400, never an empty answer that reads as "none of these is a seat here".
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class RequestBodyTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;
    private static readonly Guid PierSeven = Harbor.ProjectNamed("Pier 7").Id.Value;
    private static readonly Guid NorthCoast = Harbor.UnitNamed("North Coast").Value;
    private static readonly Guid LeoSeat = Harbor.SeatOf(DemoPeople.Leo).Value;
    private static readonly Guid Observer = Harbor.Roles[SampleCatalogue.Observer].Value;
    private static readonly Guid ObserverOnACrew = Harbor.ProjectRoles[SampleCatalogue.Observer].Value;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Ada holds every key in harbor, so nothing but the body stands between her and each command.</summary>
    public static TheoryData<string, string, string, string> BodiesWithoutTheirIds => new()
    {
        { "POST", "/projects", """{"number":"P-100","name":"Harbor wall"}""", "unitId" },
        { "PUT", $"/projects/{PierSeven}/unit", "{}", "unitId" },
        { "POST", $"/projects/{PierSeven}/crew", """{"roleId":null}""", "seatId" },
        { "POST", $"/projects/{PierSeven}/crew/{Harbor.SeatOf(DemoPeople.Vic).Value}/roles", "{}", "roleId" },
        { "POST", $"/projects/{PierSeven}/crew/{Harbor.SeatOf(DemoPeople.Vic).Value}/roles", """{"until":null}""", "roleId" },
        { "PUT", $"/projects/{PierSeven}/owner", "{}", "seatId" },
        { "POST", "/tenancy/units", """{"name":"North Harbor","kind":"area"}""", "parentId" },
        { "PUT", $"/tenancy/units/{NorthCoast}/parent", "{}", "parentId" },
        { "POST", "/tenancy/shape", "{}", "shape" },
        { "POST", $"/tenancy/seats/{LeoSeat}/placements", """{"primary":false}""", "unitId" },
        { "POST", $"/tenancy/seats/{LeoSeat}/grants", $$"""{"roleId":"{{Observer}}"}""", "unitId" },
        { "POST", $"/tenancy/seats/{LeoSeat}/grants", $$"""{"unitId":"{{NorthCoast}}"}""", "roleId" },
    };

    /// <summary>The directory's three questions by id, each with a body that does not give the ids as a list.</summary>
    public static TheoryData<string, string> DirectoryBodiesWithoutTheirIds => new()
    {
        { "/tenancy/directory/seats", "{}" },
        { "/tenancy/directory/seats", $$"""{"id":["{{LeoSeat}}"]}""" },
        { "/tenancy/directory/seats", """{"ids":null}""" },
        { "/tenancy/directory/seats", """{"ids":["not-an-id"]}""" },
        { "/tenancy/directory/seats", $$"""{"ids":"{{LeoSeat}}"}""" },
        { "/tenancy/directory/units", "{}" },
        { "/tenancy/directory/units", """{"ids":null}""" },
        { "/tenancy/directory/units", """{"ids":[null]}""" },
        { "/tenancy/directory/roles", "{}" },
        { "/tenancy/directory/roles", """{"ids":null}""" },
        { "/tenancy/directory/roles", """{"ids":[42]}""" },
    };

    public static TheoryData<string> KeysBodies => new()
    {
        "{}",
        """{"key":["projects.view"]}""",
        """{"keys":null}""",
    };

    [Theory]
    [MemberData(nameof(BodiesWithoutTheirIds))]
    public async Task A_body_without_an_id_its_command_needs_is_400_invalid_request(string method, string path, string body, string missing)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.SendAsync(Json(new HttpMethod(method), path, body), Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        refused.Body.GetProperty("detail").GetString().Should().Contain($"'{missing}'", "the answer names what is missing")
            .And.NotContain("Examples.Tenancy", "and no type of the server's").And.NotContain("DDDToolkit");
    }

    [Theory]
    [InlineData("POST", "/tenancy/shape", """{"shape":{}}""", "$.shape")]
    [InlineData("POST", "/projects", """{"number":7,"name":"Harbor wall","unitId":"b0000000-0000-4000-8000-000000000101"}""", "$.number")]
    public async Task A_body_that_does_not_read_says_where_and_names_no_type_of_the_servers(string method, string path, string body, string where)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.SendAsync(Json(new HttpMethod(method), path.Replace("{role}", Observer.ToString(), StringComparison.Ordinal), body), Cancellation);

        // The place in the body, by its path. What the body was being read into is the server's business: neither a
        // namespace nor the name of a class is in the answer.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        refused.Body.GetProperty("detail").GetString().Should().Contain(where)
            .And.NotContain("Examples.Tenancy").And.NotContain("DDDToolkit").And.NotContain("Endpoints").And.NotContain("System.");
        refused.Body.GetRawText().Should().NotContain("Examples.Tenancy").And.NotContain("DDDToolkit");
    }

    [Theory]
    [InlineData("POST", "/tenancy/directory/roles", """{"ids":[42]}""")]
    [InlineData("POST", "/projects", """{"number":"P-100","name":"Harbor wall","unitId":"north"}""")]
    public async Task A_body_that_is_no_json_or_holds_no_id_is_400_invalid_request_and_names_no_type(string method, string path, string body)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.SendAsync(Json(new HttpMethod(method), path.Replace("{role}", Observer.ToString(), StringComparison.Ordinal), body), Cancellation);

        // Where a body breaks off, or a value that is no id, the reader knows no path to name. The answer still says
        // nothing of what the body was read into.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        refused.Body.GetRawText().Should().NotContain("Examples.Tenancy").And.NotContain("DDDToolkit").And.NotContain("Endpoints").And.NotContain("System.");
    }

    /// <summary>
    /// Routes that take a body, sent none, and sent the JSON <c>null</c>, each with the name of the type the server
    /// reads the body into: what the framework's own message for it names.
    /// </summary>
    public static TheoryData<string, string, string?, string> RoutesSentNoBody => new()
    {
        { "POST", "/tenancy/invitations", null, "PersonToInvite" },
        { "POST", "/tenancy/invitations", "null", "PersonToInvite" },
        { "POST", "/projects", null, "ProjectToOpen" },
        { "PUT", $"/tenancy/roles/{Observer}/keys", "null", "KeysToSet" },
        { "POST", "/tenancy/directory/seats", null, "IdsAsked" },
    };

    [Theory]
    [MemberData(nameof(RoutesSentNoBody))]
    public async Task A_request_without_the_body_its_route_takes_is_told_so_in_the_hosts_words(string method, string path, string? body, string readInto)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var request = body is null ? new HttpRequestMessage(new HttpMethod(method), path) : Json(new HttpMethod(method), path, body);

        using var response = await ada.SendAsync(request, Cancellation);

        // The framework's message for it names the parameter the body is read into, by its type: the server's
        // business, as the type a body failed to read into is.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        refused.Body.GetProperty("detail").GetString().Should().Be("The body is missing or null, and this route takes one.");
        refused.Body.GetRawText().Should().NotContain(readInto).And.NotContain("parameter").And.NotContain("Examples.Tenancy").And.NotContain("DDDToolkit");
    }

    [Theory]
    [MemberData(nameof(DirectoryBodiesWithoutTheirIds))]
    public async Task A_directory_question_without_its_ids_is_400_invalid_request(string path, string body)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.SendAsync(Json(HttpMethod.Post, path, body), Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }

    [Fact]
    public async Task A_directory_question_with_an_empty_list_is_answered_an_empty_list()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        foreach (var path in new[] { "/tenancy/directory/seats", "/tenancy/directory/units", "/tenancy/directory/roles" })
        {
            using var response = await ada.SendAsync(Json(HttpMethod.Post, path, """{"ids":[]}"""), Cancellation);

            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetArrayLength().Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(KeysBodies))]
    public async Task Setting_a_roles_keys_without_the_list_is_refused_and_keeps_its_keys(string body)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.SendAsync(Json(HttpMethod.Put, $"/tenancy/roles/{Observer}/keys", body), Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        var roles = await ada.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation);
        roles.EnumerateArray().Single(role => role.GetProperty("id").GetGuid() == Observer)
            .GetProperty("keys").EnumerateArray().Select(key => key.GetString())
            .Should().Equal(ProjectKeys.View);
    }

    [Fact]
    public async Task An_end_given_as_until_is_the_end_of_the_grant_of_the_membership_and_of_a_crew_role()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var until = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(7), TimeSpan.Zero);

        using (var granted = await rhea.PostAsJsonAsync($"/tenancy/seats/{LeoSeat}/grants", new { unitId = NorthCoast, roleId = Observer, until }, Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var ada = Harbor.Administrator.Id.Value;
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven}/crew", new { seatId = ada, roleId = ObserverOnACrew, until }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var me = await leo.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("placements").EnumerateArray().Single().GetProperty("grants").EnumerateArray().Single()
            .GetProperty("endsAt").GetDateTimeOffset().Should().Be(until);

        // A role given with a member ends when the membership does; one given later ends when it says.
        var surveyor = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value;
        var sooner = until.AddDays(-2);
        using (var given = await leo.PostAsJsonAsync($"/projects/{PierSeven}/crew/{ada}/roles", new { roleId = surveyor, until = sooner }, Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var crew = (await leo.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven}", Cancellation)).GetProperty("crew").EnumerateArray();
        var member = crew.Single(member => member.GetProperty("seatId").GetGuid() == ada);
        member.GetProperty("endsAt").GetDateTimeOffset().Should().Be(until);
        member.GetProperty("roles").EnumerateArray()
            .Select(held => (Role: held.GetProperty("roleId").GetGuid(), Ends: held.GetProperty("endsAt").GetDateTimeOffset()))
            .Should().BeEquivalentTo([(ObserverOnACrew, until), (surveyor, sooner)]);
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, string body)
        => new(method, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
