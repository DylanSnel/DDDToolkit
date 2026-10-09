using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Languages;
using Examples.Tenancy.Ui.Session;

namespace Examples.Tenancy.Ui.Api;

/// <summary>
/// The UI's one client of the Host's API. Every call is built here, from whom it is made as
/// (<see cref="CallAs"/>) and which tenant it names (<see cref="TenantChoice"/>), and every answer comes back as an
/// <see cref="ApiOutcome{T}"/>, refusals included. Every call asks to be answered in the session's language, in
/// its <c>Accept-Language</c> header, so the text of a refusal is in the language the page is.
/// </summary>
/// <remarks>
/// <para>
/// It is a typed client resolved in the circuit's scope, so it takes the circuit's <see cref="UiSession"/> in its
/// constructor and reads the session's token and tenant at the moment of each call. Nothing is set on the
/// <see cref="HttpClient"/> itself: a preset runs as another person on the same client without touching the
/// session.
/// </para>
/// <para>
/// Only a 401 on a call that carried the session's own token ends the session: that token is no longer accepted,
/// so the person must sign in again. A 401 on an anonymous call or on a preset's token is the answer being
/// demonstrated, and leaves the session as it is.
/// </para>
/// <para>
/// It never retries: the registration removes the resilience handlers the service defaults add, so a refused or
/// failed POST is sent once and its first answer is shown.
/// </para>
/// <para>
/// A token stays on the server. The answer to a token request is not recorded as the session's last answer, and
/// comes back with the tokens blanked in its raw body, so no page can render one; only its read value carries
/// the token, for the code that sends it.
/// </para>
/// </remarks>
public sealed class SampleApi(HttpClient http, UiSession session)
{
    /// <summary>The header that names the tenant by its slug.</summary>
    public const string TenantHeader = "Tenant";

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    // The person's own seats, in every tenant, for the tenant picker: signed in, no tenant.

    /// <summary><c>GET /me/seats</c> as the session.</summary>
    public Task<ApiOutcome<IReadOnlyList<SeatOfMine>>> SeatsOfMineAsync(CancellationToken cancellationToken = default)
        => SeatsOfMineAsync(CallAs.Session, cancellationToken);

    /// <summary><c>GET /me/seats</c> as <paramref name="caller"/>, such as a person who is signing in.</summary>
    public Task<ApiOutcome<IReadOnlyList<SeatOfMine>>> SeatsOfMineAsync(CallAs caller, CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<SeatOfMine>>(HttpMethod.Get, "/me/seats", null, caller, TenantChoice.None, cancellationToken);

    // Reads in the session's tenant.

    /// <summary><c>GET /me</c>: who the session's seat is, and what it may do where.</summary>
    public Task<ApiOutcome<WhoAmIAnswer>> WhoAmIAsync(CancellationToken cancellationToken = default)
        => GetAsync<WhoAmIAnswer>("/me", cancellationToken);

    /// <summary>
    /// <c>GET /projects</c>: a page of the projects the session's seat may see, by number. <paramref name="after"/>
    /// is the marker the page before gave as its next; <paramref name="state"/> and <paramref name="text"/> narrow
    /// the list to a state, <c>open</c> or <c>closed</c>, and to a number that starts with the text or a name that
    /// contains it; <paramref name="size"/> is how many projects a page holds, the API's own choice when left out.
    /// </summary>
    public Task<ApiOutcome<ProjectPageInfo>> VisibleProjectsAsync(string? after = null, string? state = null, string? text = null, int? size = null, CancellationToken cancellationToken = default)
        => GetAsync<ProjectPageInfo>(
            "/projects" + Query(("after", after), ("state", state), ("text", text), ("size", size?.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            cancellationToken);

    /// <summary><c>GET /projects/{id}</c>: one project, with what the session's seat may do to it.</summary>
    public Task<ApiOutcome<ProjectInfo>> ProjectDetailAsync(string projectId, CancellationToken cancellationToken = default)
        => GetAsync<ProjectInfo>($"/projects/{Segment(projectId)}", cancellationToken);

    /// <summary>
    /// <c>GET /projects/{id}/inspections</c>: a page of its inspections, newest first, and whether the session's
    /// seat may record one. <paramref name="after"/> is the marker the page before gave as its next.
    /// </summary>
    public Task<ApiOutcome<InspectionsInfo>> InspectionsAsync(string projectId, string? after = null, CancellationToken cancellationToken = default)
        => GetAsync<InspectionsInfo>($"/projects/{Segment(projectId)}/inspections" + Query(("after", after)), cancellationToken);

    /// <summary>
    /// <c>GET /tenancy/history</c>: a page of the tenant's access history, newest first, for a seat that holds the
    /// key that reads it. <paramref name="after"/> is the marker the page before gave as its next, and
    /// <paramref name="size"/> how many rows a page holds, the API's own choice when left out.
    /// </summary>
    public Task<ApiOutcome<HistoryPageInfo>> AccessHistoryAsync(string? after = null, int? size = null, CancellationToken cancellationToken = default)
        => GetAsync<HistoryPageInfo>(
            "/tenancy/history" + Query(("size", size?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("after", after)),
            cancellationToken);

    /// <summary>
    /// <c>GET /tenancy/invitations</c>: the invitations that can still be accepted, into the units where the
    /// session's seat manages seats.
    /// </summary>
    public Task<ApiOutcome<IReadOnlyList<InvitationInfo>>> OpenInvitationsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<InvitationInfo>>("/tenancy/invitations", cancellationToken);

    /// <summary><c>GET /tenancy/seats</c>: every seat of the tenant, by name.</summary>
    public Task<ApiOutcome<IReadOnlyList<SeatInfo>>> TenantSeatsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<SeatInfo>>("/tenancy/seats", cancellationToken);

    /// <summary><c>GET /tenancy/units</c>: the units the session's seat is placed in and every unit below them.</summary>
    public Task<ApiOutcome<IReadOnlyList<UnitInfo>>> TenantUnitsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<UnitInfo>>("/tenancy/units", cancellationToken);

    /// <summary><c>GET /tenancy/roles</c>: the tenant's roles.</summary>
    public Task<ApiOutcome<IReadOnlyList<RoleInfo>>> TenantRolesAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<RoleInfo>>("/tenancy/roles", cancellationToken);

    /// <summary><c>GET /project-roles</c>: the tenant's project roles, the ones its crews hold, archived ones too.</summary>
    public Task<ApiOutcome<IReadOnlyList<ProjectRoleInfo>>> ProjectRolesAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<ProjectRoleInfo>>("/project-roles", cancellationToken);

    /// <summary><c>GET /tenancy/catalogue</c>: every key the application knows.</summary>
    public Task<ApiOutcome<CatalogueInfo>> KeyCatalogueAsync(CancellationToken cancellationToken = default)
        => GetAsync<CatalogueInfo>("/tenancy/catalogue", cancellationToken);

    // The directory by id, in the session's tenant: what the ids another answer carried are called. A page asks
    // these to fill in the answer it is about, so they are not recorded as the session's last answer.

    /// <summary><c>POST /tenancy/directory/seats</c>: the seats with these ids, by name. An id of another tenant is left out.</summary>
    public Task<ApiOutcome<IReadOnlyList<SeatInfo>>> SeatsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => AskDirectoryAsync<SeatInfo>("/tenancy/directory/seats", ids, cancellationToken);

    /// <summary><c>POST /tenancy/directory/units</c>: the units with these ids, by path, whichever the session's seat is placed under.</summary>
    public Task<ApiOutcome<IReadOnlyList<UnitInfo>>> UnitsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => AskDirectoryAsync<UnitInfo>("/tenancy/directory/units", ids, cancellationToken);

    /// <summary><c>POST /tenancy/directory/roles</c>: the roles with these ids, by name.</summary>
    public Task<ApiOutcome<IReadOnlyList<RoleInfo>>> RolesByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => AskDirectoryAsync<RoleInfo>("/tenancy/directory/roles", ids, cancellationToken);

    /// <summary>
    /// <c>GET /project-roles</c>, asked to name the project roles a crew holds: a tenant has few, so they are read
    /// all at once rather than by id.
    /// </summary>
    public Task<ApiOutcome<IReadOnlyList<ProjectRoleInfo>>> ProjectRoleNamesAsync(CancellationToken cancellationToken = default)
        => ExchangeAsync<IReadOnlyList<ProjectRoleInfo>>(HttpMethod.Get, "/project-roles", null, CallAs.Session, TenantChoice.Session, record: false, cancellationToken);

    // Commands in the session's tenant. Each answers 2xx, or a refusal with its code.

    /// <summary><c>PUT /projects/{id}/name</c>.</summary>
    public Task<ApiOutcome<JsonElement?>> RenameAsync(string projectId, string? name, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Put, $"/projects/{Segment(projectId)}/name", new { name }, cancellationToken);

    /// <summary>
    /// <c>PUT /projects/{id}/planned-range</c>: plans the project for a range of days, or, with both left out, takes
    /// its plan away.
    /// </summary>
    public Task<ApiOutcome<JsonElement?>> PlanAsync(string projectId, DateOnly? from, DateOnly? until, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Put, $"/projects/{Segment(projectId)}/planned-range", new { from, until }, cancellationToken);

    /// <summary><c>PUT /projects/{id}/unit</c>.</summary>
    public Task<ApiOutcome<JsonElement?>> MoveToUnitAsync(string projectId, string? unitId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Put, $"/projects/{Segment(projectId)}/unit", new { unitId = Blank(unitId) }, cancellationToken);

    /// <summary><c>POST /projects/{id}/close</c>.</summary>
    public Task<ApiOutcome<JsonElement?>> CloseAsync(string projectId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, $"/projects/{Segment(projectId)}/close", null, cancellationToken);

    /// <summary><c>POST /projects/{id}/reopen</c>.</summary>
    public Task<ApiOutcome<JsonElement?>> ReopenAsync(string projectId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, $"/projects/{Segment(projectId)}/reopen", null, cancellationToken);

    /// <summary>
    /// <c>POST /projects/{id}/crew</c>: puts a seat on the crew until a moment or for good, with a project role for
    /// the same period or none.
    /// </summary>
    public Task<ApiOutcome<JsonElement?>> AddToCrewAsync(string projectId, string? seatId, string? roleId, DateTimeOffset? until, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, $"/projects/{Segment(projectId)}/crew", new { seatId = Blank(seatId), roleId = Blank(roleId), until }, cancellationToken);

    /// <summary>
    /// <c>POST /projects/{id}/crew/{seatId}/roles</c>: gives a crew member a project role, next to the ones it holds,
    /// until a moment or for good.
    /// </summary>
    public Task<ApiOutcome<JsonElement?>> GiveCrewRoleAsync(string projectId, string? seatId, string? roleId, DateTimeOffset? until, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, $"/projects/{Segment(projectId)}/crew/{Segment(seatId)}/roles", new { roleId = Blank(roleId), until }, cancellationToken);

    /// <summary><c>DELETE /projects/{id}/crew/{seatId}/roles/{roleId}</c>: takes a project role from a crew member, who stays on the crew.</summary>
    public Task<ApiOutcome<JsonElement?>> TakeCrewRoleAsync(string projectId, string seatId, string roleId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Delete, $"/projects/{Segment(projectId)}/crew/{Segment(seatId)}/roles/{Segment(roleId)}", null, cancellationToken);

    /// <summary><c>POST /project-roles</c>: makes a project role with a name, what it is for, and the keys it gives.</summary>
    public Task<ApiOutcome<JsonElement?>> MakeProjectRoleAsync(string? name, string? description, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, "/project-roles", new { name, description = Blank(description), keys }, cancellationToken);

    /// <summary><c>PUT /project-roles/{id}</c>: a project role's name and description, as they are to be.</summary>
    public Task<ApiOutcome<JsonElement?>> RenameProjectRoleAsync(string roleId, string? name, string? description, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Put, $"/project-roles/{Segment(roleId)}", new { name, description = Blank(description) }, cancellationToken);

    /// <summary><c>PUT /project-roles/{id}/keys</c>: every key a project role is to give, none taking them all off.</summary>
    public Task<ApiOutcome<JsonElement?>> SetProjectRoleKeysAsync(string roleId, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Put, $"/project-roles/{Segment(roleId)}/keys", new { keys }, cancellationToken);

    /// <summary><c>POST /project-roles/{id}/archive</c>: the role gives nothing from now on and is given no more.</summary>
    public Task<ApiOutcome<JsonElement?>> ArchiveProjectRoleAsync(string roleId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, $"/project-roles/{Segment(roleId)}/archive", null, cancellationToken);

    /// <summary><c>DELETE /projects/{id}/crew/{seatId}</c>: takes a seat off the crew, with every role it holds there.</summary>
    public Task<ApiOutcome<JsonElement?>> RemoveFromCrewAsync(string projectId, string seatId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Delete, $"/projects/{Segment(projectId)}/crew/{Segment(seatId)}", null, cancellationToken);

    /// <summary><c>PUT /projects/{id}/owner</c>.</summary>
    public Task<ApiOutcome<JsonElement?>> ChangeOwnerAsync(string projectId, string? seatId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Put, $"/projects/{Segment(projectId)}/owner", new { seatId = Blank(seatId) }, cancellationToken);

    /// <summary>
    /// <c>POST /projects/{id}/inspections</c>: records an inspection for a range of days, or, with both left out,
    /// for today.
    /// </summary>
    public Task<ApiOutcome<JsonElement?>> RecordInspectionAsync(string projectId, string? title, DateOnly? from, DateOnly? until, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Post, $"/projects/{Segment(projectId)}/inspections", new { title, from, until }, cancellationToken);

    /// <summary>
    /// <c>POST /tenancy/invitations</c>: invites a person by address into a unit, with a role there until a moment
    /// or for good. The invitation's token is in <see cref="ApiOutcome{T}.Value"/> only: the raw body, which is
    /// what the session keeps as its last answer and a page shows, has it blanked.
    /// </summary>
    public async Task<ApiOutcome<IssuedInvitationInfo>> InvitePersonAsync(
        string? address,
        string? unitId,
        string? roleId,
        DateTimeOffset? until,
        CancellationToken cancellationToken = default)
    {
        var outcome = await ExchangeAsync<IssuedInvitationInfo>(
            HttpMethod.Post,
            "/tenancy/invitations",
            new { address = Blank(address), unitId = Blank(unitId), roleId = Blank(roleId), until },
            CallAs.Session,
            TenantChoice.Session,
            record: false,
            cancellationToken);

        outcome = outcome with { RawBody = WithoutTokens(outcome.RawBody) };
        if (session.IsSignedIn)
        {
            session.Record(outcome);
        }

        return outcome;
    }

    /// <summary><c>DELETE /tenancy/invitations/{id}</c>: revokes an open invitation, so its token no longer works.</summary>
    public Task<ApiOutcome<JsonElement?>> CancelInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        => CommandAsync(HttpMethod.Delete, $"/tenancy/invitations/{invitationId}", null, cancellationToken);

    /// <summary>
    /// <c>POST /invitations/accept</c>: the signed-in person accepts the invitation a token is for. It names no
    /// tenant: the token says which. The token travels in the body, and the answer carries none. The name is the one the
    /// person is shown by in that tenant, which the seat's rule requires.
    /// </summary>
    public Task<ApiOutcome<AcceptedInvitationInfo>> AcceptInvitationAsync(string? token, string? displayName, CancellationToken cancellationToken = default)
        => SendAsync<AcceptedInvitationInfo>(
            HttpMethod.Post,
            "/invitations/accept",
            new { token = Blank(token), displayName = Blank(displayName) },
            CallAs.Session,
            TenantChoice.None,
            cancellationToken);

    // The dev login: anonymous, and in no tenant.

    /// <summary><c>GET /dev/people</c>.</summary>
    public Task<ApiOutcome<IReadOnlyList<PersonCard>>> PeopleAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<PersonCard>>(HttpMethod.Get, "/dev/people", null, CallAs.Anonymous, TenantChoice.None, cancellationToken);

    /// <summary>
    /// <c>POST /dev/auth/token</c>: a token for <paramref name="person"/>, in <see cref="ApiOutcome{T}.Value"/>
    /// only. The raw body shows the tokens blanked, and the answer is not recorded as the session's last one.
    /// </summary>
    public async Task<ApiOutcome<TokenAnswer>> TokenAsync(string person, CancellationToken cancellationToken = default)
    {
        var outcome = await ExchangeAsync<TokenAnswer>(HttpMethod.Post, "/dev/auth/token", new { person }, CallAs.Anonymous, TenantChoice.None, record: false, cancellationToken);
        return outcome with { RawBody = WithoutTokens(outcome.RawBody) };
    }

    /// <summary><c>GET /dev/attempts</c>: the try-it presets.</summary>
    public Task<ApiOutcome<IReadOnlyList<AttemptPreset>>> AttemptsAsync(CancellationToken cancellationToken = default)
        => SendAsync<IReadOnlyList<AttemptPreset>>(HttpMethod.Get, "/dev/attempts", null, CallAs.Anonymous, TenantChoice.None, cancellationToken);

    /// <summary>
    /// Sends one call and reads its answer. Every other method comes through here, and so do the try-it form and
    /// the presets.
    /// </summary>
    /// <typeparam name="T">What a successful answer's body is read as.</typeparam>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path and query, ids already escaped.</param>
    /// <param name="body">
    /// The body: a <see cref="JsonElement"/> is sent as it is, any other object is serialised, and
    /// <see langword="null"/> or a JSON null sends none.
    /// </param>
    /// <param name="caller">Whose token to send.</param>
    /// <param name="tenant">Which <c>Tenant</c> header to send.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ApiOutcome<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CallAs caller,
        TenantChoice tenant,
        CancellationToken cancellationToken = default)
        => ExchangeAsync<T>(method, path, body, caller, tenant, record: true, cancellationToken);

    /// <summary>Sends one call, and records its answer as the session's last one when <paramref name="record"/> is set.</summary>
    private async Task<ApiOutcome<T>> ExchangeAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CallAs caller,
        TenantChoice tenant,
        bool record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(tenant);

        var token = caller.IsSession ? session.AccessToken : caller.AccessToken;
        var slug = tenant.FromSession ? session.Tenant : tenant.Slug;
        var who = token is null ? "anonymous" : caller.IsSession ? session.Person ?? "session" : caller.PersonKey ?? "someone";

        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (slug is not null)
        {
            request.Headers.Add(TenantHeader, slug);
        }

        // Whoever the call is made as, its answer is read by the person at this screen.
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(session.Language));

        request.Content = Content(body);

        var clock = Stopwatch.StartNew();
        ApiOutcome<T> outcome;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            outcome = Read<T>(method.Method, path, who, slug, (int)response.StatusCode, raw, clock.Elapsed, session.Language);

            if (response.StatusCode == HttpStatusCode.Unauthorized && caller.IsSession && token is not null && token == session.AccessToken)
            {
                // Signing out forgets the session's answers, so the answer that ended it is recorded after, and is
                // what the login page shows.
                session.SignOut(UiTexts.For("api.token-refused", session.Language));
                session.Record(outcome);
                return outcome;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            outcome = new ApiOutcome<T>(
                method.Method,
                path,
                who,
                slug,
                0,
                default,
                ApiProblem.WithoutArguments(null, UiTexts.For("api.no-answer", session.Language), exception.Message),
                string.Empty,
                clock.Elapsed);
        }

        if (record)
        {
            session.Record(outcome);
        }

        return outcome;
    }

    private Task<ApiOutcome<IReadOnlyList<T>>> AskDirectoryAsync<T>(string path, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ExchangeAsync<IReadOnlyList<T>>(HttpMethod.Post, path, new { ids }, CallAs.Session, TenantChoice.Session, record: false, cancellationToken);
    }

    private Task<ApiOutcome<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Get, path, null, CallAs.Session, TenantChoice.Session, cancellationToken);

    private Task<ApiOutcome<JsonElement?>> CommandAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        => SendAsync<JsonElement?>(method, path, body, CallAs.Session, TenantChoice.Session, cancellationToken);

    /// <summary>The query string of the values that are not blank, each escaped; nothing when all are blank.</summary>
    private static string Query(params (string Name, string? Value)[] values)
    {
        var given = values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Name + "=" + Uri.EscapeDataString(pair.Value!.Trim())).ToList();
        return given.Count == 0 ? string.Empty : "?" + string.Join("&", given);
    }

    /// <summary>An id put into a path as typed, escaped, so a malformed one reaches the API and is refused there.</summary>
    public static string Segment(string? id) => Uri.EscapeDataString(id?.Trim() ?? string.Empty);

    /// <summary><see langword="null"/> for a blank value, so an empty picker sends a JSON null rather than an empty string.</summary>
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// <paramref name="raw"/> with the value of every <c>access_token</c>, <c>refresh_token</c> and <c>token</c>
    /// in its top-level object blanked; anything that is not such an object is returned as it is.
    /// </summary>
    private static string WithoutTokens(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        try
        {
            if (JsonNode.Parse(raw) is not JsonObject answer)
            {
                return raw;
            }

            foreach (var name in new[] { "access_token", "refresh_token", "token" })
            {
                if (answer.ContainsKey(name))
                {
                    answer[name] = "(kept on the server)";
                }
            }

            return answer.ToJsonString();
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static HttpContent? Content(object? body) => body switch
    {
        null => null,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement element => new StringContent(element.GetRawText(), Encoding.UTF8, "application/json"),
        _ => JsonContent.Create(body, body.GetType(), options: Json),
    };

    private static ApiOutcome<T> Read<T>(string method, string path, string who, string? tenant, int status, string raw, TimeSpan elapsed, string language)
    {
        var succeeded = status is >= 200 and <= 299;
        if (!succeeded)
        {
            return new ApiOutcome<T>(method, path, who, tenant, status, default, ProblemIn(raw), raw, elapsed);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ApiOutcome<T>(method, path, who, tenant, status, default, null, raw, elapsed);
        }

        try
        {
            return new ApiOutcome<T>(method, path, who, tenant, status, JsonSerializer.Deserialize<T>(raw, Json), null, raw, elapsed);
        }
        catch (JsonException exception)
        {
            // The API answered, but not in the shape this UI reads: say so rather than show an empty screen.
            return new ApiOutcome<T>(method, path, who, tenant, status, default, ApiProblem.WithoutArguments(null, UiTexts.For("api.unreadable", language), exception.Message), raw, elapsed);
        }
    }

    /// <summary>
    /// The problem in a refusal's body: its <c>code</c>, <c>title</c>, <c>detail</c> and <c>arguments</c>. A body
    /// that is not a JSON object, or none, gives a problem with no code.
    /// </summary>
    private static ApiProblem ProblemIn(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ApiProblem.WithoutArguments(null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                return ApiProblem.WithoutArguments(null, null);
            }

            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("arguments", out var given) && given.ValueKind is JsonValueKind.Object)
            {
                foreach (var argument in given.EnumerateObject())
                {
                    arguments[argument.Name] = argument.Value.Clone();
                }
            }

            return new ApiProblem(Text(root, "code"), Text(root, "title"), Text(root, "detail"), arguments);
        }
        catch (JsonException)
        {
            return ApiProblem.WithoutArguments(null, null);
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}
