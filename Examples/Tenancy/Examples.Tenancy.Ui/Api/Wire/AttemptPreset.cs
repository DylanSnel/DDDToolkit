using System.Text.Json;

namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A try-it preset (<c>GET /dev/attempts</c>): a call of a demonstration person, nearly always one the person may
/// not make, ready to send, with the answer the API should give.
/// </summary>
/// <param name="Id">Its stable name.</param>
/// <param name="Title">What is tried, and by whom.</param>
/// <param name="Person">Whom it runs as.</param>
/// <param name="Tenant">The slug it names, or <see langword="null"/>.</param>
/// <param name="SendToken">Whether it carries the person's token.</param>
/// <param name="SendTenant">Whether it carries the <c>Tenant</c> header.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, every id filled in.</param>
/// <param name="Body">The JSON body, or <see langword="null"/>.</param>
/// <param name="ExpectedStatus">The status the API should answer.</param>
/// <param name="ExpectedCode">The problem's code it should answer, or <see langword="null"/> for none.</param>
public sealed record AttemptPreset(
    string Id,
    string Title,
    string Person,
    string? Tenant,
    bool SendToken,
    bool SendTenant,
    string Method,
    string Path,
    JsonElement? Body,
    int ExpectedStatus,
    string? ExpectedCode)
{
    /// <summary>Whether <paramref name="outcome"/> is the answer this preset expects: the same status and the same code.</summary>
    public bool IsAnsweredBy(ApiOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Status == ExpectedStatus && string.Equals(outcome.Problem?.Code, ExpectedCode, StringComparison.Ordinal);
    }
}
