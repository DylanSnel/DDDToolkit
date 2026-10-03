using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// Supabase Auth's admin API, for server code only: every call sends the project's secret key. It makes a
/// user ahead of a person's first sign-in, has Auth invite an address or a user it has, reads and changes
/// a user by id, and deletes one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A user is known by its id.</b> Nothing here looks a user up by an address, and
/// <see cref="SupabaseAuthUser"/> carries none. An application gives access to the id of the account a
/// person signed in with; matching on an address instead would hand that access to whoever gets hold of
/// the address.
/// </para>
/// <para>
/// <b>An invitation is for a user the application made.</b> Auth's own invitation of an address also
/// mails a user who was there already and never proved the address, and in a project that lets anybody
/// sign up that user can be a stranger's. So make the user with <see cref="CreateUserAsync"/>, which
/// refuses an address that has any user, and have it mailed by its id with <see cref="InviteUserAsync"/>.
/// <see cref="SupabaseIdentityAccounts"/> does exactly that behind the application's port.
/// </para>
/// <para>
/// <b>Nothing is retried.</b> An invitation sent twice is two mails, and a failure the caller never saw
/// cannot be reasoned about, so each call is one request, or for <see cref="InviteUserAsync"/> two that
/// follow each other, and the caller decides what a failure means.
/// For the same reason <see cref="DependencyInjection.AddSupabaseAuthAdmin"/> gives this a client of its
/// own rather than one from <c>IHttpClientFactory</c>, where a host's defaults for every client, a retry
/// handler among them, would apply.
/// </para>
/// <para>
/// <b>Nothing is logged.</b> The address travels in the request's body and the key in its headers, never
/// in the URL, which is what a host's request logging and tracing record. A refusal is a
/// <see cref="SupabaseAuthAdminException"/> with the status and Auth's error code and none of Auth's own
/// text. A failure to reach Auth at all is the <see cref="HttpRequestException"/> of the client, which
/// names the server and nothing that was sent.
/// </para>
/// <para>
/// <b>The key goes to the Auth URL and nowhere else.</b> The client this makes for itself follows no
/// redirect: following one would send the key along to wherever it points. An answer that redirects is a
/// refusal like any other. A handler the host passes is refused while it follows redirects. A client the
/// host configured itself cannot be looked into from here, so there the host switches redirects off.
/// </para>
/// <para>
/// <b>The key is not sent in the clear.</b> The Auth URL is https. Plain http is taken for an Auth server on
/// this machine, <c>localhost</c> or a loopback address, where nothing travels; for one on a private
/// network of the host's own the host says so, with <c>allowPlainHttp</c>.
/// </para>
/// </remarks>
public sealed class SupabaseAuthAdmin : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _auth;
    private readonly bool _ownsClient;

    /// <summary>
    /// An admin client over a client the host configured itself: its <see cref="HttpClient.BaseAddress"/>
    /// is the Auth URL, and its default headers carry the secret key as <c>apikey</c> and as the bearer
    /// token. The client stays the host's to dispose. Give it a handler that follows no redirect and
    /// retries nothing, and keep it out of the host's defaults for every client, for the reasons this
    /// class gives.
    /// </summary>
    /// <param name="http">The configured client.</param>
    /// <param name="allowPlainHttp">
    /// Whether a base address in plain http is taken for a server that is not on this machine: for an Auth
    /// server on a private network of the host's own. Every call sends the secret key, unencrypted there.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="http"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="http"/> has no base address, one that is not an http or https URL, or one in plain http
    /// to another machine without <paramref name="allowPlainHttp"/>.
    /// </exception>
    public SupabaseAuthAdmin(HttpClient http, bool allowPlainHttp = false)
    {
        ArgumentNullException.ThrowIfNull(http);

        _auth = AuthAddressOf(http.BaseAddress?.OriginalString ?? "", nameof(http), allowPlainHttp);
        _http = http;
    }

    /// <summary>An admin client for the Auth server at <paramref name="authUrl"/>, with a client of its own.</summary>
    /// <param name="authUrl">
    /// Where Auth answers: <c>https://&lt;ref&gt;.supabase.co/auth/v1</c> for a project, which is
    /// <see cref="SupabaseTokens.IssuerOf"/> of its URL, or the address of an Auth server with no gateway in
    /// front of it.
    /// </param>
    /// <param name="secretKey">
    /// The project's secret key (<c>sb_secret_...</c>, or the legacy <c>service_role</c> key); for a bare
    /// Auth server, a token with the service role. It is sent as <c>apikey</c> and as the bearer token, and
    /// nowhere else.
    /// </param>
    /// <param name="handler">
    /// What sends the requests, for a host that reaches Auth through a proxy of its own, or a test. It
    /// stays the caller's to dispose, and it follows no redirect: one of the runtime's own handlers that
    /// does is refused. Left out, the client pools connections itself, follows no redirect, keeps no cookies
    /// and looks the server's address up again every few minutes.
    /// </param>
    /// <param name="allowPlainHttp">
    /// Whether an <paramref name="authUrl"/> in plain http is taken for a server that is not on this machine:
    /// for an Auth server on a private network of the host's own. Every call sends the secret key,
    /// unencrypted there.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="authUrl"/> is not an http or https URL, or is plain http to another machine without
    /// <paramref name="allowPlainHttp"/>; <paramref name="secretKey"/> is empty, is a publishable key, or has
    /// a character a header cannot carry; or <paramref name="handler"/> follows redirects. The message never
    /// repeats the key.
    /// </exception>
    public SupabaseAuthAdmin(string authUrl, string secretKey, HttpMessageHandler? handler = null, bool allowPlainHttp = false)
    {
        _auth = AuthAddressOf(authUrl, nameof(authUrl), allowPlainHttp);
        var key = SecretKeyOf(secretKey, nameof(secretKey));

        _http = handler is null
            ? new HttpClient(OwnHandler(), disposeHandler: true)
            : new HttpClient(FollowingNoRedirect(handler, nameof(handler)), disposeHandler: false);
        _ownsClient = true;

        // The gateway of a project reads apikey, Auth itself the bearer token; both get the one key.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>The user with this id, or <see langword="null"/> when Auth has none.</summary>
    /// <param name="id">The user's id.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <exception cref="SupabaseAuthAdminException">
    /// Auth refused, or answered "not found" without saying that it is the user it did not find: a wrong
    /// Auth URL must not read as a user who is gone.
    /// </exception>
    public async Task<SupabaseAuthUser?> FindUserAsync(Guid id, CancellationToken cancellationToken)
    {
        const string Operation = "reading a user";

        var answer = await SendAsync(HttpMethod.Get, UserPath(id), body: null, cancellationToken).ConfigureAwait(false);
        if (answer.Succeeded)
        {
            return UserOf(answer, Operation);
        }

        var code = ErrorCodeOf(answer);
        return IsNoSuchUser(answer, code) ? null : throw new SupabaseAuthAdminException(answer.Status, code, Operation);
    }

    /// <summary>
    /// Makes a user without mailing anyone. With <see cref="SupabaseNewUser.Id"/> the user gets that id,
    /// so an application can refer to a person before they exist at Auth: seeded users, imported people,
    /// and whoever is given access ahead of their first sign-in.
    /// </summary>
    /// <param name="user">The user to make.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <returns>The user as Auth made it, under the id that was given when one was.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="user"/> is null.</exception>
    /// <exception cref="ArgumentException">The address is empty, or the id is the empty one.</exception>
    /// <exception cref="SupabaseAuthAdminException">
    /// Auth refused. <see cref="SupabaseAuthAdminException.AddressAlreadyRegistered"/> says the address
    /// already belongs to a user; an id that is taken is a status 500 with the database's code for a
    /// duplicate key, <see cref="SupabaseAuthAdminException.DuplicateKey"/>. So is the address when two
    /// calls for it arrive together: Auth lets one of them make the user, and the database refuses the other.
    /// </exception>
    public async Task<SupabaseAuthUser> CreateUserAsync(SupabaseNewUser user, CancellationToken cancellationToken)
    {
        const string Operation = "creating a user";

        ArgumentNullException.ThrowIfNull(user);
        var address = AddressOf(user.Email, nameof(user));
        if (user.Id == Guid.Empty)
        {
            throw new ArgumentException("The id to make the user under is the empty one. Leave it out for Auth to choose, or give a real one.", nameof(user));
        }

        var body = Json(json =>
        {
            json.WriteString("email", address);
            if (user.Id is { } id)
            {
                json.WriteString("id", id);
            }

            if (user.Password is not null)
            {
                json.WriteString("password", user.Password);
            }

            if (user.EmailConfirmed)
            {
                json.WriteBoolean("email_confirm", true);
            }

            WriteMetadata(json, "user_metadata", user.UserMetadata);
            WriteMetadata(json, "app_metadata", user.AppMetadata);
        });

        var answer = await SendAsync(HttpMethod.Post, "admin/users", body, cancellationToken).ConfigureAwait(false);
        if (!answer.Succeeded)
        {
            throw new SupabaseAuthAdminException(answer.Status, ErrorCodeOf(answer), Operation);
        }

        var made = UserOf(answer, Operation);

        // An Auth server from before ids could be given ignores the field and chooses its own. Giving
        // access to an id nobody will ever sign in with is the one thing this call exists to prevent.
        return user.Id is { } given && made.Id != given
            ? throw SupabaseAuthAdminException.AnotherId(answer.Status, made.Id)
            : made;
    }

    /// <summary>Changes a user's password, the entries of its <c>app_metadata</c>, or both.</summary>
    /// <param name="id">The user's id.</param>
    /// <param name="change">What changes.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="change"/> names nothing to change.</exception>
    /// <exception cref="SupabaseAuthAdminException">Auth refused; with <c>user_not_found</c> when it has no such user, since there is nothing a change to nobody could mean.</exception>
    public async Task UpdateUserAsync(Guid id, SupabaseUserChange change, CancellationToken cancellationToken)
    {
        const string Operation = "changing a user";

        ArgumentNullException.ThrowIfNull(change);
        if (change.Password is null && change.AppMetadata is null)
        {
            throw new ArgumentException("The change names nothing to change: give a password, app metadata, or both.", nameof(change));
        }

        var body = Json(json =>
        {
            if (change.Password is not null)
            {
                json.WriteString("password", change.Password);
            }

            WriteMetadata(json, "app_metadata", change.AppMetadata);
        });

        var answer = await SendAsync(HttpMethod.Put, UserPath(id), body, cancellationToken).ConfigureAwait(false);
        if (!answer.Succeeded)
        {
            throw new SupabaseAuthAdminException(answer.Status, ErrorCodeOf(answer), Operation);
        }

        // Auth answers with the user it changed. Anything else that calls itself a success changed nothing.
        if (UserOf(answer, Operation).Id != id)
        {
            throw SupabaseAuthAdminException.Unreadable(answer.Status, Operation);
        }
    }

    /// <summary>
    /// Deletes a user for good, with its sessions and identities: what erasing a person ends with, and what
    /// removes a user who was made for somebody who never signed in.
    /// </summary>
    /// <param name="id">The user's id.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <returns>
    /// <see langword="true"/> when the user was deleted, <see langword="false"/> when Auth had none. A user
    /// who is already gone is not an error, so an erasure that failed halfway can be run again.
    /// </returns>
    /// <exception cref="SupabaseAuthAdminException">
    /// Auth refused, or answered "not found" without saying that it is the user it did not find: a wrong
    /// Auth URL must not read as a user who is gone.
    /// </exception>
    public async Task<bool> DeleteUserAsync(Guid id, CancellationToken cancellationToken)
    {
        const string Operation = "deleting a user";

        var answer = await SendAsync(HttpMethod.Delete, UserPath(id), body: null, cancellationToken).ConfigureAwait(false);
        if (answer.Succeeded)
        {
            // Auth answers a deletion with an empty object. A success of any other kind, a page from
            // something in between, deleted nobody, and an erasure must not take it for done.
            return IsJsonObject(answer) ? true : throw SupabaseAuthAdminException.Unreadable(answer.Status, Operation);
        }

        var code = ErrorCodeOf(answer);
        return IsNoSuchUser(answer, code) ? false : throw new SupabaseAuthAdminException(answer.Status, code, Operation);
    }

    /// <summary>
    /// Has Auth invite an address: Auth makes the user and sends its own mail, with a link that lets the
    /// person in. The id comes back at once, so the application can give the user access before they have
    /// signed in. It works with sign-ups switched off, which is how a project that only admits invited
    /// people is set up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auth also invites a user who was made earlier and never proved the address, and answers with the id
    /// that user already had. In a project where anybody may sign up, such a user can have been made by
    /// somebody else, who chose its password and keeps it. So where sign-ups are on, make the user first
    /// with <see cref="CreateUserAsync"/>, which refuses an address that has a user, proven or not, and
    /// have it mailed by its id with <see cref="InviteUserAsync"/>: the user that is mailed is then the
    /// application's own, and a sign-up by somebody else in between does not change it. See
    /// <see cref="SupabaseInvitationResult.Sent"/>.
    /// </para>
    /// <para>
    /// Of two invitations for one new address that arrive together, Auth lets one make the user. The
    /// other is refused by the database, which is a <see cref="SupabaseAuthAdminException"/> whose
    /// <see cref="SupabaseAuthAdminException.DuplicateKey"/> is true.
    /// </para>
    /// </remarks>
    /// <param name="email">The address to invite.</param>
    /// <param name="options">Where the link leads and what the new user's metadata is; <see langword="null"/> for Auth's defaults.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <returns>
    /// <see cref="SupabaseInvitationResult.Sent"/> with the user's id, or
    /// <see cref="SupabaseInvitationResult.AlreadyRegistered"/> for an address that has an account
    /// already; nothing looks an account up by address. Keep the difference on the server: see
    /// <see cref="SupabaseInvitationResult"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="email"/> is empty.</exception>
    /// <exception cref="SupabaseAuthAdminException">
    /// Auth refused for another reason, such as <c>validation_failed</c> for something that is not an
    /// address or <c>over_email_send_rate_limit</c> when it has mailed too much.
    /// </exception>
    public async Task<SupabaseInvitationResult> InviteByEmailAsync(string email, SupabaseInvitation? options, CancellationToken cancellationToken)
    {
        const string Operation = "inviting an address";

        var address = AddressOf(email, nameof(email));

        // The redirect is the one thing in a URL here, and it is the application's own page. The address
        // goes in the body: URLs are what proxies, request logs and traces keep.
        var path = options?.RedirectTo is { Length: > 0 } redirect
            ? "invite?redirect_to=" + Uri.EscapeDataString(redirect)
            : "invite";

        var body = Json(json =>
        {
            json.WriteString("email", address);
            WriteMetadata(json, "data", options?.Data);
        });

        var answer = await SendAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
        if (answer.Succeeded)
        {
            return new SupabaseInvitationResult.Sent(UserOf(answer, Operation).Id);
        }

        var code = ErrorCodeOf(answer);
        return answer.Status == 422 && code == SupabaseAuthAdminException.AddressExists
            ? new SupabaseInvitationResult.AlreadyRegistered()
            : throw new SupabaseAuthAdminException(answer.Status, code, Operation);
    }

    /// <summary>
    /// Has Auth invite the user with this id: Auth mails the user's address a link that lets the person
    /// in. It is the invitation for a user the application made itself with <see cref="CreateUserAsync"/>,
    /// and a new one for a user whose earlier invitation went unanswered.
    /// </summary>
    /// <remarks>
    /// Auth invites by address, so this reads the user first and has Auth invite the address it has for
    /// them, then checks that the user Auth mailed is the one that was asked for. The address goes from
    /// one answer into the next request and nowhere else. It is two requests, and neither is retried.
    /// </remarks>
    /// <param name="id">The user's id.</param>
    /// <param name="options">Where the link leads; <see langword="null"/> for Auth's defaults. <see cref="SupabaseInvitation.Data"/> is for a new user and changes nothing about one that exists.</param>
    /// <param name="cancellationToken">Stops waiting for Auth.</param>
    /// <returns>
    /// <see cref="SupabaseInvitationResult.Sent"/> with <paramref name="id"/> when Auth mailed the user,
    /// <see cref="SupabaseInvitationResult.AlreadyRegistered"/> when the user has proven the address, in
    /// which case nobody was mailed, or <see langword="null"/> when Auth has no user with this id.
    /// </returns>
    /// <exception cref="SupabaseAuthAdminException">
    /// Auth refused; the user has no e-mail address to mail; or Auth mailed another user than the one asked
    /// for, which is what happens when the user was removed in between, or signs in through single sign-on,
    /// where an address is not theirs alone.
    /// </exception>
    public async Task<SupabaseInvitationResult?> InviteUserAsync(Guid id, SupabaseInvitation? options, CancellationToken cancellationToken)
    {
        const string Operation = "inviting a user";

        var answer = await SendAsync(HttpMethod.Get, UserPath(id), body: null, cancellationToken).ConfigureAwait(false);
        if (!answer.Succeeded)
        {
            var code = ErrorCodeOf(answer);
            return IsNoSuchUser(answer, code) ? null : throw new SupabaseAuthAdminException(answer.Status, code, Operation);
        }

        if (UserOf(answer, Operation).Id != id)
        {
            throw SupabaseAuthAdminException.Unreadable(answer.Status, Operation);
        }

        var address = AddressIn(answer) ?? throw SupabaseAuthAdminException.NoAddress(answer.Status, id);

        return await InviteByEmailAsync(address, options, cancellationToken).ConfigureAwait(false) switch
        {
            SupabaseInvitationResult.Sent sent when sent.UserId != id => throw SupabaseAuthAdminException.InvitedAnother(id),
            var result => result,
        };
    }

    /// <summary>Disposes the client this made for itself; a client the host passed in stays the host's.</summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// The handler of a client this makes for itself. It follows no redirect: the runtime drops the bearer
    /// token when it follows one and keeps every other header, so <c>apikey</c> would travel to wherever
    /// the redirect points, and the secret key is for the Auth URL alone. It keeps no cookies, which the
    /// admin API does not use, and it looks the server's address up again every few minutes, as a client
    /// that lives as long as the host has to.
    /// </summary>
    private static SocketsHttpHandler OwnHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>
    /// <paramref name="handler"/>, refused when it, or the handler at the end of a chain of delegating ones,
    /// is one of the runtime's own with redirects on, which is their default. The runtime drops the bearer
    /// token when it follows a redirect and keeps every other header, so the secret key would travel as
    /// <c>apikey</c> to wherever the redirect points. A handler of another kind is taken as it is.
    /// </summary>
    internal static HttpMessageHandler FollowingNoRedirect(HttpMessageHandler handler, string parameter)
    {
        for (var inner = handler; inner is not null; inner = (inner as DelegatingHandler)?.InnerHandler)
        {
            if (inner is HttpClientHandler { AllowAutoRedirect: true } or SocketsHttpHandler { AllowAutoRedirect: true })
            {
                throw new ArgumentException(
                    "The handler follows redirects, and the secret key would travel as apikey to wherever one points: the runtime drops the bearer token when it follows a redirect and keeps every other header. " +
                    "Set AllowAutoRedirect = false on it.",
                    parameter);
            }
        }

        return handler;
    }

    /// <summary>
    /// The Auth URL as the base every call is resolved against, ending in a slash so that a path such as
    /// <c>/auth/v1</c> is kept rather than replaced. The message of a refusal does not repeat the value: a
    /// URL pasted from a connection setting can have a password in it.
    /// </summary>
    internal static Uri AuthAddressOf(string authUrl, string parameter, bool allowPlainHttp = false)
    {
        if (string.IsNullOrWhiteSpace(authUrl)
            || !Uri.TryCreate(authUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            throw new ArgumentException(
                "The Auth URL is not one: pass an absolute http or https URL without a query, such as https://<ref>.supabase.co/auth/v1 for a project, or the address of an Auth server of your own.",
                parameter);
        }

        // Every call carries the secret key. On this machine nothing travels; anywhere else plain http shows
        // it to whatever is in between, so the host has to say that the network in between is its own.
        if (SupabaseTokens.IsPlainHttpToAnotherMachine(uri) && !allowPlainHttp)
        {
            throw new ArgumentException(
                "The Auth URL is plain http to another machine, so every call would send the secret key unencrypted. Use https. " +
                "For an Auth server on a private network of your own, pass allowPlainHttp: true.",
                parameter);
        }

        var root = uri.GetLeftPart(UriPartial.Path);
        return new Uri(root.EndsWith('/') ? root : root + "/");
    }

    /// <summary>The secret key as a header can carry it. No message here repeats any part of it.</summary>
    internal static string SecretKeyOf(string secretKey, string parameter)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new ArgumentException("The secret key is empty. The admin API takes the project's secret key, or on a bare Auth server a token with the service role.", parameter);
        }

        var key = secretKey.Trim();
        if (key.StartsWith("sb_publishable_", StringComparison.Ordinal))
        {
            throw new ArgumentException("That is the project's publishable key, which is for browsers. The admin API takes the secret key, which stays on the server.", parameter);
        }

        foreach (var character in key)
        {
            if (character is <= ' ' or >= (char)0x7F)
            {
                throw new ArgumentException("The secret key has a space, a line break or another character a header cannot carry. Check how it was copied.", parameter);
            }
        }

        return key;
    }

    private static string AddressOf(string? email, string parameter)
        => string.IsNullOrWhiteSpace(email)
            ? throw new ArgumentException("The e-mail address is empty.", parameter)
            : email.Trim();

    private static string UserPath(Guid id) => "admin/users/" + id.ToString("D");

    private static bool IsNoSuchUser(Answer answer, string? code)
        => answer.Status == 404 && code == SupabaseAuthAdminException.NoSuchUser;

    private async Task<Answer> SendAsync(HttpMethod method, string path, byte[]? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_auth, path));
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new Answer((int)response.StatusCode, content);
    }

    private static byte[] Json(Action<Utf8JsonWriter> members)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            members(json);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteMetadata(Utf8JsonWriter json, string name, IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        json.WritePropertyName(name);
        JsonSerializer.Serialize(json, metadata);
    }

    /// <summary>
    /// The user in a successful answer. Only the id and three timestamps are read; the address in the
    /// answer is not part of what comes out.
    /// </summary>
    private static SupabaseAuthUser UserOf(Answer answer, string operation)
    {
        try
        {
            using var document = JsonDocument.Parse(answer.Body);
            var user = document.RootElement;
            if (user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.TryGetGuid(out var userId))
            {
                // The user's own last_sign_in_at. Each entry of "identities" has one too, which Auth sets
                // when it makes the identity, sign-in or not.
                return new SupabaseAuthUser(userId, TimeOf(user, "invited_at"), TimeOf(user, "last_sign_in_at"), TimeOf(user, "email_confirmed_at"));
            }
        }
        catch (JsonException)
        {
            // Not JSON at all, which is reported the same way as JSON without a user.
        }

        throw SupabaseAuthAdminException.Unreadable(answer.Status, operation);
    }

    /// <summary>
    /// The address Auth has for the user in a successful answer, or <see langword="null"/> when it has
    /// none, as for a user who signs in by phone. Read for one purpose: to say it back to Auth in the
    /// request that invites the user. It is returned to no caller and written to no message.
    /// </summary>
    private static string? AddressIn(Answer answer)
    {
        using var document = JsonDocument.Parse(answer.Body);
        return document.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(email.GetString())
            ? email.GetString()
            : null;
    }

    private static bool IsJsonObject(Answer answer)
    {
        try
        {
            using var document = JsonDocument.Parse(answer.Body);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset? TimeOf(JsonElement user, string name)
        => user.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var time)
            ? time
            : null;

    /// <summary>
    /// Auth's error code in a refusal: <c>error_code</c> in the answers it gives by default, <c>code</c>
    /// in the newer shape, where the status is no longer a member. Anything that does not look like a
    /// code is dropped, so that what ends up in an exception's message is a code and cannot be a text
    /// with somebody's address in it.
    /// </summary>
    private static string? ErrorCodeOf(Answer answer)
    {
        try
        {
            using var document = JsonDocument.Parse(answer.Body);
            var error = document.RootElement;
            if (error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in (ReadOnlySpan<string>)["error_code", "code"])
            {
                if (error.TryGetProperty(name, out var code) && code.ValueKind == JsonValueKind.String && IsCode(code.GetString()))
                {
                    return code.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // A gateway's page or an empty body: there is no code to read.
        }

        return null;
    }

    private static bool IsCode(string? text)
    {
        if (text is not { Length: > 0 and <= 64 })
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct Answer(int Status, byte[] Body)
    {
        public bool Succeeded => Status is >= 200 and <= 299;
    }
}
