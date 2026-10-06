using System.Net.Http.Json;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>A route of the API, filled in with harbor's ids and a body that binds.</summary>
/// <param name="Template">The route as the API table writes it, which also names it in a theory.</param>
/// <param name="Method">Its method.</param>
/// <param name="Path">The path to call.</param>
/// <param name="Body">The JSON body, for a route that takes one.</param>
public sealed record ApiRoute(string Template, HttpMethod Method, string Path, object? Body = null)
{
    /// <summary>A request for the route; a new one each time, since a request is sent once.</summary>
    public HttpRequestMessage Request()
        => new(Method, Path) { Content = Body is null ? null : JsonContent.Create(Body) };
}

/// <summary>
/// The routes that need a token, filled in with harbor's ids. Every body binds, so that what answers is the
/// rule a test is after, for a caller with a seat too: a body that did not bind would answer such a caller 400.
/// </summary>
public static class SeatedRoutes
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;
    private static readonly SeatId Leo = Harbor.SeatOf(DemoPeople.Leo);
    private static readonly SeatId Rhea = Harbor.SeatOf(DemoPeople.Rhea);
    private static readonly SeatId Juno = Harbor.SeatOf(DemoPeople.Juno);
    private static readonly SeatId Vic = Harbor.SeatOf(DemoPeople.Vic);
    private static readonly OrganizationUnitId North = Harbor.UnitNamed("North");
    private static readonly OrganizationUnitId NorthCoast = Harbor.UnitNamed("North Coast");
    private static readonly RoleId AccessAdmin = Harbor.Roles[SampleCatalogue.AccessAdmin];
    private static readonly RoleId Surveyor = Harbor.Roles[SampleCatalogue.Surveyor];
    private static readonly RoleId Observer = Harbor.Roles[SampleCatalogue.Observer];
    private static readonly ProjectRoleId SurveyorOnACrew = Harbor.ProjectRoles[SampleCatalogue.Surveyor];
    private static readonly ProjectRoleId ObserverOnACrew = Harbor.ProjectRoles[SampleCatalogue.Observer];
    private static readonly Guid PierSeven = Harbor.ProjectNamed("Pier 7").Id.Value;

    /// <summary>Tenancy's routes that need a seat in the tenant the request names.</summary>
    public static IReadOnlyList<ApiRoute> Tenancy { get; } =
    [
        Get("GET /me", "/me"),
        Get("GET /tenancy/units", "/tenancy/units"),
        Send("POST /tenancy/units", HttpMethod.Post, "/tenancy/units", new { parentId = North.Value, name = "North Harbor", kind = "area" }),
        Send("PUT /tenancy/units/{id}/parent", HttpMethod.Put, $"/tenancy/units/{NorthCoast.Value}/parent", new { parentId = Harbor.Root.Value }),
        Send("POST /tenancy/units/{id}/archive", HttpMethod.Post, $"/tenancy/units/{NorthCoast.Value}/archive"),
        Send("POST /tenancy/shape", HttpMethod.Post, "/tenancy/shape", new { shape = "hierarchical" }),
        Get("GET /tenancy/seats", "/tenancy/seats"),
        Get("GET /tenancy/seats/{seatId}/grants", $"/tenancy/seats/{Leo.Value}/grants"),
        Send("POST /tenancy/seats/{seatId}/placements", HttpMethod.Post, $"/tenancy/seats/{Leo.Value}/placements", new { unitId = North.Value, primary = false }),
        Send("DELETE /tenancy/seats/{seatId}/placements/{unitId}", HttpMethod.Delete, $"/tenancy/seats/{Leo.Value}/placements/{NorthCoast.Value}"),
        Send("POST /tenancy/seats/{seatId}/grants", HttpMethod.Post, $"/tenancy/seats/{Leo.Value}/grants", new { unitId = NorthCoast.Value, roleId = AccessAdmin.Value }),
        Send("DELETE /tenancy/seats/{seatId}/grants/{unitId}/{roleId}", HttpMethod.Delete, $"/tenancy/seats/{Harbor.Administrator.Id.Value}/grants/{Harbor.Root.Value}/{AccessAdmin.Value}"),
        Send("POST /tenancy/seats/{seatId}/suspend", HttpMethod.Post, $"/tenancy/seats/{Leo.Value}/suspend"),
        Send("POST /tenancy/seats/{seatId}/reactivate", HttpMethod.Post, $"/tenancy/seats/{Leo.Value}/reactivate"),
        Send("POST /tenancy/seats/{seatId}/deactivate", HttpMethod.Post, $"/tenancy/seats/{Leo.Value}/deactivate"),
        Get("GET /tenancy/roles", "/tenancy/roles"),
        Send("POST /tenancy/roles", HttpMethod.Post, "/tenancy/roles", new { name = "Site keeper", description = "Keeps a site", keys = new[] { "tenancy.units.manage" } }),
        Send("PUT /tenancy/roles/{id}/keys", HttpMethod.Put, $"/tenancy/roles/{AccessAdmin.Value}/keys", new { keys = new[] { "tenancy.units.manage" } }),
        Send("POST /tenancy/roles/{id}/archive", HttpMethod.Post, $"/tenancy/roles/{AccessAdmin.Value}/archive"),
        Send("POST /tenancy/directory/seats", HttpMethod.Post, "/tenancy/directory/seats", new { ids = new[] { Leo.Value, Vic.Value } }),
        Send("POST /tenancy/directory/units", HttpMethod.Post, "/tenancy/directory/units", new { ids = new[] { NorthCoast.Value } }),
        Send("POST /tenancy/directory/roles", HttpMethod.Post, "/tenancy/directory/roles", new { ids = new[] { Surveyor.Value, Observer.Value } }),
        Get("GET /tenancy/catalogue", "/tenancy/catalogue"),
        Get("GET /access/units?key=K", "/access/units?key=tenancy.units.manage"),
        Get("GET /tenancy/history", "/tenancy/history"),
        Get("GET /tenancy/invitations", "/tenancy/invitations"),
        Send("POST /tenancy/invitations", HttpMethod.Post, "/tenancy/invitations", new { address = "wren@example.test", unitId = NorthCoast.Value, roleId = Observer.Value }),
        Send("DELETE /tenancy/invitations/{invitationId}", HttpMethod.Delete, $"/tenancy/invitations/{Guid.Empty}"),
    ];

    /// <summary>
    /// Projects' routes, on Pier 7 where a route names a project and on the observer's project role where one names
    /// a role; each needs a seat in the tenant the request names.
    /// </summary>
    public static IReadOnlyList<ApiRoute> Projects { get; } =
    [
        Get("GET /projects", "/projects"),
        Get("GET /projects/{id}", $"/projects/{PierSeven}"),
        Send("POST /projects", HttpMethod.Post, "/projects", new { number = "P-100", name = "Harbor wall", unitId = NorthCoast.Value }),
        Send("PUT /projects/{id}/name", HttpMethod.Put, $"/projects/{PierSeven}/name", new { name = "Pier 7 east" }),
        Send("PUT /projects/{id}/planned-range", HttpMethod.Put, $"/projects/{PierSeven}/planned-range", new { from = "2026-10-05", until = "2026-10-30" }),
        Send("PUT /projects/{id}/unit", HttpMethod.Put, $"/projects/{PierSeven}/unit", new { unitId = North.Value }),
        Send("POST /projects/{id}/close", HttpMethod.Post, $"/projects/{PierSeven}/close"),
        Send("POST /projects/{id}/reopen", HttpMethod.Post, $"/projects/{PierSeven}/reopen"),
        Get("GET /projects/{id}/crew", $"/projects/{PierSeven}/crew"),
        Send("POST /projects/{id}/crew", HttpMethod.Post, $"/projects/{PierSeven}/crew", new { seatId = Rhea.Value, roleId = ObserverOnACrew.Value }),
        Send("POST /projects/{id}/crew/{seatId}/roles", HttpMethod.Post, $"/projects/{PierSeven}/crew/{Vic.Value}/roles", new { roleId = SurveyorOnACrew.Value }),
        Send("DELETE /projects/{id}/crew/{seatId}/roles/{roleId}", HttpMethod.Delete, $"/projects/{PierSeven}/crew/{Vic.Value}/roles/{ObserverOnACrew.Value}"),
        Send("DELETE /projects/{id}/crew/{seatId}", HttpMethod.Delete, $"/projects/{PierSeven}/crew/{Vic.Value}"),
        Send("PUT /projects/{id}/owner", HttpMethod.Put, $"/projects/{PierSeven}/owner", new { seatId = Juno.Value }),
        Get("GET /access/projects/{id}?key=K", $"/access/projects/{PierSeven}?key=projects.view"),
        Get("GET /access/keys?keys=K", "/access/keys?keys=projects.open"),
        Send("POST /access/projects/keys", HttpMethod.Post, "/access/projects/keys", new { keys = new[] { "projects.edit" }, projects = new[] { PierSeven } }),
        Get("GET /project-roles", "/project-roles"),
        Send("POST /project-roles", HttpMethod.Post, "/project-roles", new { name = "Rigger", description = "Rigs the hoists", keys = new[] { "projects.view" } }),
        Send("PUT /project-roles/{id}", HttpMethod.Put, $"/project-roles/{ObserverOnACrew.Value}", new { name = "Onlooker" }),
        Send("PUT /project-roles/{id}/keys", HttpMethod.Put, $"/project-roles/{ObserverOnACrew.Value}/keys", new { keys = new[] { "projects.view" } }),
        Send("POST /project-roles/{id}/archive", HttpMethod.Post, $"/project-roles/{ObserverOnACrew.Value}/archive"),
    ];

    /// <summary>Inspections' routes, on Pier 7; each needs a seat in the tenant the request names.</summary>
    public static IReadOnlyList<ApiRoute> Inspections { get; } =
    [
        Get("GET /projects/{id}/inspections", $"/projects/{PierSeven}/inspections"),
        Send("POST /projects/{id}/inspections", HttpMethod.Post, $"/projects/{PierSeven}/inspections", new { title = "Loose railing on the east side" }),
    ];

    /// <summary>Every route that needs a seat in the tenant the request names.</summary>
    public static IReadOnlyList<ApiRoute> InTenant { get; } = [.. Tenancy, .. Projects, .. Inspections];

    /// <summary>
    /// The routes that need a token and no tenant: a person's own seats, for picking one, and accepting an
    /// invitation, which is how a person comes by a seat.
    /// </summary>
    public static IReadOnlyList<ApiRoute> SignedIn { get; } =
    [
        Get("GET /me/seats", "/me/seats"),
        Send("POST /invitations/accept", HttpMethod.Post, "/invitations/accept", new { token = "a-token-nobody-was-given" }),
    ];

    /// <summary>
    /// The routes of the application's own staff, on harbor and Pier 7: each needs a token that carries the
    /// operators' role, and names its tenant in the path, since an operator has a seat in none.
    /// </summary>
    public static IReadOnlyList<ApiRoute> Operations { get; } =
    [
        Get("GET /operations/tenants", "/operations/tenants"),
        Get("GET /operations/tenants/{tenant}/history", $"/operations/tenants/{Harbor.Id.Value}/history"),
        Get("GET /operations/tenants/{tenant}/projects", $"/operations/tenants/{Harbor.Id.Value}/projects"),
        Get("GET /operations/tenants/{tenant}/projects/{project}/inspections", $"/operations/tenants/{Harbor.Id.Value}/projects/{PierSeven}/inspections"),
    ];

    /// <summary>Every route that needs a token.</summary>
    public static IReadOnlyList<ApiRoute> All { get; } = [.. SignedIn, .. InTenant, .. Operations];

    /// <summary>The route with this template.</summary>
    public static ApiRoute Named(string template) => All.Single(route => route.Template == template);

    /// <summary>The templates of <paramref name="routes"/>, for a theory.</summary>
    public static TheoryData<string> Templates(IEnumerable<ApiRoute> routes) => new(routes.Select(route => route.Template));

    private static ApiRoute Get(string template, string path) => new(template, HttpMethod.Get, path);

    private static ApiRoute Send(string template, HttpMethod method, string path, object? body = null) => new(template, method, path, body);
}
