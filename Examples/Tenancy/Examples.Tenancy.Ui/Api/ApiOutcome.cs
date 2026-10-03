namespace Examples.Tenancy.Ui.Api;

/// <summary>
/// One answer of the API, whatever it was: what was asked, as whom and in which tenant, the status, the problem
/// when it refused, and the body as it came.
/// </summary>
/// <remarks>
/// A refusal is an answer, not an error, so the client never throws for one: the screens show it. The UI's point
/// is to let a person see what the API decides, and a 403 with its code is as much a result as a 200.
/// </remarks>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path and query, as sent.</param>
/// <param name="Caller">Whose token the call carried: the session's person, a preset's person, or <c>anonymous</c>.</param>
/// <param name="Tenant">The slug the <c>Tenant</c> header carried, or <see langword="null"/> when none was sent.</param>
/// <param name="Status">The status, or 0 when the API did not answer at all.</param>
/// <param name="Problem">The problem the API answered with, or why there is no answer; <see langword="null"/> on success.</param>
/// <param name="RawBody">The body, as it came.</param>
/// <param name="Elapsed">How long the call took.</param>
public record ApiOutcome(
    string Method,
    string Path,
    string Caller,
    string? Tenant,
    int Status,
    ApiProblem? Problem,
    string RawBody,
    TimeSpan Elapsed)
{
    /// <summary>Whether the API did what was asked: any 2xx, since commands answer 204 with no body.</summary>
    public bool Succeeded => Status is >= 200 and <= 299;

    /// <summary>Whether no answer came at all: the API is not running, or not where the settings say.</summary>
    public bool Unanswered => Status == 0;

    /// <summary>
    /// Whether the API answered that what was asked for is not there, a 404: the one answer that does not say
    /// why, since what a caller may not see is not found exactly as what does not exist. A page that explains
    /// that explains it for this answer only: any other refusal carries its own reason.
    /// </summary>
    public bool NotFound => Status == 404;
}

/// <summary>An <see cref="ApiOutcome"/> with the body read as <typeparamref name="T"/> when the call succeeded.</summary>
/// <typeparam name="T">What a successful answer carries.</typeparam>
/// <param name="Value">The body, read; <see langword="default"/> when the call failed or answered nothing.</param>
public sealed record ApiOutcome<T>(
    string Method,
    string Path,
    string Caller,
    string? Tenant,
    int Status,
    T? Value,
    ApiProblem? Problem,
    string RawBody,
    TimeSpan Elapsed)
    : ApiOutcome(Method, Path, Caller, Tenant, Status, Problem, RawBody, Elapsed);
