using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.Wire;

namespace Examples.Tenancy.Ui.Session;

/// <summary>
/// Who the UI is signed in as, which tenant it works in, which language it reads, the API's last answer and the
/// try-it history: one per circuit, so one per browser tab.
/// </summary>
/// <remarks>
/// <para>
/// The token lives here, on the server, and never in the browser's script. <see cref="SessionStore"/> keeps a copy
/// in the tab's session storage, encrypted with the server's data protection keys, so a reload signs back in.
/// </para>
/// <para>
/// Two events, because they call for different work: <see cref="Changed"/> when the person, the tenant or the
/// language changes, on which the pages load again; <see cref="Answered"/> on every answer, on which the layout's
/// "last response" strip redraws. A page that reloaded on every answer would call the API forever.
/// </para>
/// </remarks>
public sealed class UiSession
{
    /// <summary>How many try-it answers the history keeps.</summary>
    public const int HistoryLength = 50;

    /// <summary>The languages the UI offers, by the name each is sent to the API with. The first is the one a tab starts in.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["en", "nl"];

    private readonly List<TryItEntry> _history = [];

    /// <summary>The key of the signed-in person, such as <c>rhea</c>.</summary>
    public string? Person { get; private set; }

    /// <summary>The name the signed-in person is shown by.</summary>
    public string? PersonName { get; private set; }

    /// <summary>The access token every call made as the session carries.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>When <see cref="AccessToken"/> expires.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The slug of the tenant calls are made in, or <see langword="null"/> for none.</summary>
    public string? Tenant { get; private set; }

    /// <summary>
    /// The language the pages are shown in, and the one every call asks the API to answer in: one of
    /// <see cref="Languages"/>. It is the tab's, not the person's, so signing out keeps it.
    /// </summary>
    public string Language { get; private set; } = Languages[0];

    /// <summary>
    /// How often the signed-in person came by a seat during this sign-in, by accepting an invitation: whoever
    /// shows the person's seats asks for them again when this has changed.
    /// </summary>
    public int SeatsAdded { get; private set; }

    /// <summary>Whether someone is signed in.</summary>
    public bool IsSignedIn => AccessToken is not null;

    /// <summary>Why the last sign-in ended, when it did not end by logging out; shown on the login page.</summary>
    public string? SignedOutBecause { get; private set; }

    /// <summary>The API's latest answer to any call of this circuit.</summary>
    public ApiOutcome? LastAnswer { get; private set; }

    /// <summary>The try-it answers of this circuit, newest first.</summary>
    public IReadOnlyList<TryItEntry> History => _history;

    /// <summary>The person, the tenant or the language changed, or the session ended.</summary>
    public event Action? Changed;

    /// <summary>An answer arrived: <see cref="LastAnswer"/> or <see cref="History"/> changed.</summary>
    public event Action? Answered;

    /// <summary>
    /// Starts a sign-in for <paramref name="person"/>, working in <paramref name="tenant"/>. Signing in as someone
    /// else without logging out first starts an empty try-it history, as logging out would.
    /// </summary>
    public void SignIn(string person, string name, string accessToken, DateTimeOffset expiresAt, string? tenant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        if (Person is not null && Person != person)
        {
            _history.Clear();
        }

        Person = person;
        PersonName = string.IsNullOrWhiteSpace(name) ? person : name;
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
        Tenant = string.IsNullOrWhiteSpace(tenant) ? null : tenant;
        SignedOutBecause = null;
        Changed?.Invoke();
    }

    /// <summary>Works in the tenant <paramref name="slug"/> names from now on; blank for none.</summary>
    public void SelectTenant(string? slug)
    {
        var tenant = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim();
        if (tenant == Tenant)
        {
            return;
        }

        Tenant = tenant;
        Changed?.Invoke();
    }

    /// <summary>
    /// The signed-in person has a seat more than when they signed in, in the tenant <paramref name="slug"/> names:
    /// the session works there from now on, or where it did when the slug is blank.
    /// </summary>
    public void SeatAdded(string? slug)
    {
        SeatsAdded++;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            Tenant = slug.Trim();
        }

        Changed?.Invoke();
    }

    /// <summary>Reads and asks in <paramref name="language"/> from now on. A language the UI does not offer changes nothing.</summary>
    public void SelectLanguage(string? language)
    {
        if (language is null || language == Language || !Languages.Contains(language))
        {
            return;
        }

        Language = language;
        Changed?.Invoke();
    }

    /// <summary>
    /// Ends the sign-in, saying why when it was not the person's own choice. The answers of the sign-in go with
    /// it, the last one and the try-it history, so whoever signs in next in this tab sees none of them.
    /// </summary>
    public void SignOut(string? because = null)
    {
        if (!IsSignedIn)
        {
            return;
        }

        Person = null;
        PersonName = null;
        AccessToken = null;
        ExpiresAt = null;
        Tenant = null;
        SignedOutBecause = because;
        LastAnswer = null;
        _history.Clear();
        Changed?.Invoke();
        Answered?.Invoke();
    }

    /// <summary>Keeps <paramref name="outcome"/> as the latest answer.</summary>
    public void Record(ApiOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        LastAnswer = outcome;
        Answered?.Invoke();
    }

    /// <summary>Adds a try-it answer to the history, dropping the oldest past <see cref="HistoryLength"/>.</summary>
    public void Remember(TryItEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _history.Insert(0, entry);
        if (_history.Count > HistoryLength)
        {
            _history.RemoveRange(HistoryLength, _history.Count - HistoryLength);
        }

        Answered?.Invoke();
    }

    /// <summary>What <see cref="SessionStore"/> keeps across a reload, or <see langword="null"/> when nobody is signed in.</summary>
    public SessionSnapshot? Snapshot()
        => IsSignedIn ? new SessionSnapshot(Person!, PersonName!, AccessToken!, ExpiresAt!.Value, Tenant, Language) : null;

    /// <summary>
    /// Signs back in from a <paramref name="snapshot"/> kept before a reload, unless its token has expired by
    /// <paramref name="now"/>: that one would only earn a 401.
    /// </summary>
    /// <returns>Whether the session was restored.</returns>
    public bool Restore(SessionSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ExpiresAt <= now || string.IsNullOrWhiteSpace(snapshot.AccessToken))
        {
            return false;
        }

        // The language first and without an event of its own: signing in raises the one event the restore is.
        if (snapshot.Language is { } language && Languages.Contains(language))
        {
            Language = language;
        }

        SignIn(snapshot.Person, snapshot.PersonName, snapshot.AccessToken, snapshot.ExpiresAt, snapshot.Tenant);
        return true;
    }

    /// <summary>
    /// The tenant a fresh sign-in starts in: the first tenant by slug where the person's seat is active, or, for
    /// someone with no active seat, the first by slug anyway, so that the refusal their seat earns is the first
    /// thing they see.
    /// </summary>
    public static string? TenantToStartIn(IEnumerable<SeatOfMine> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);

        var bySlug = seats.OrderBy(mine => mine.Tenant.Slug, StringComparer.Ordinal).ToList();
        return (bySlug.FirstOrDefault(mine => mine.Seat.IsActive) ?? bySlug.FirstOrDefault())?.Tenant.Slug;
    }
}
