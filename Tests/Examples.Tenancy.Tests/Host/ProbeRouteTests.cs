using System.Net;
using System.Net.Http.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Requests that are wrong in themselves, whoever sends them: a key the catalogue does not know, an id that is
/// not an id, in the routes of a project, of its crew and of a project role. Each is a 400 with a code, never a 500
/// and never a 404 that would pass for an answer.
/// </summary>
/// <remarks>
/// The first test is how a refusal reaches a client over HTTP at all: problem+json with a code, a title and the
/// arguments. <c>RefusalShapeTests</c> asks the handler that writes it case by case, with no host.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ProbeRouteTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task A_refusal_is_problem_json_with_code_title_and_arguments()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.GetAsync("/access/units?key=projects.nothing", Cancellation);

        var problem = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
        problem.Title.Should().NotBeNullOrWhiteSpace().And.Contain("projects.nothing");
        problem.Argument("Keys").Should().Be("projects.nothing");
        problem.Argument(RefusalException.FieldArgument).Should().Be("keys", "a refusal about one input names it, for a form to put the text under");
        problem.Body.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task An_unknown_key_in_an_access_probe_is_400_with_code()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using var response = await rhea.GetAsync($"/access/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}?key=projects.nothing", Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
        refused.Argument("Keys").Should().Be("projects.nothing");
    }

    [Theory]
    [InlineData("/projects/not-a-project")]
    [InlineData("/projects/not-a-project/crew")]
    [InlineData("/access/projects/not-a-project?key=projects.view")]
    public async Task A_malformed_project_id_is_400_invalid_request(string path)
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using var response = await rhea.GetAsync(path, Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }

    [Theory]
    [InlineData("DELETE", "/projects/{project}/crew/not-a-seat")]
    [InlineData("POST", "/projects/{project}/crew/not-a-seat/roles")]
    [InlineData("DELETE", "/projects/{project}/crew/not-a-seat/roles/{role}")]
    [InlineData("DELETE", "/projects/{project}/crew/{seat}/roles/not-a-role")]
    [InlineData("DELETE", "/projects/not-a-project/crew/{seat}/roles/{role}")]
    [InlineData("PUT", "/project-roles/not-a-role")]
    [InlineData("POST", "/project-roles/not-a-role/archive")]
    public async Task A_malformed_id_in_a_crew_or_role_route_is_400_invalid_request(string method, string template)
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        var surveyor = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value;
        var path = template
            .Replace("{project}", Harbor.ProjectNamed("Pier 7").Id.Value.ToString(), StringComparison.Ordinal)
            .Replace("{seat}", Harbor.SeatOf(DemoPeople.Vic).Value.ToString(), StringComparison.Ordinal)
            .Replace("{role}", surveyor.ToString(), StringComparison.Ordinal);

        // A body that binds, where the route takes one, so the id in the path is what is wrong.
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Content = method switch
        {
            "POST" => JsonContent.Create(new { roleId = surveyor }),
            "PUT" => JsonContent.Create(new { name = "Rigger" }),
            _ => null,
        };

        using var response = await rhea.SendAsync(request, Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }

    [Fact]
    public async Task The_route_that_changed_a_members_one_role_is_gone()
    {
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);

        // A member holds any number of roles now, each given and taken on its own, so there is no one role to put.
        using var response = await leo.PutAsJsonAsync(
            $"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/crew/{Harbor.SeatOf(DemoPeople.Vic).Value}",
            new { roleId = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value },
            Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
