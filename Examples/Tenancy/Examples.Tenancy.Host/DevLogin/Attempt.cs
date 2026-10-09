namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// One call of a demonstration person, ready to send, with the answer the API should give: nearly always a call
/// the person may not make.
/// </summary>
/// <remarks>
/// Every id in <see cref="Path"/> and <see cref="Body"/> is already filled in from <see cref="DemoData"/>,
/// so a client sends the call as it is: it signs in as <see cref="Person"/> through the dev login when
/// <see cref="SendToken"/> is set, adds the <c>Tenant</c> header when <see cref="SendTenant"/> is set, and compares
/// the status and the problem's <c>code</c> with the two it expects.
/// </remarks>
/// <param name="Id">A stable name for the attempt, for a client's history and a test's theory.</param>
/// <param name="Title">What is tried, and by whom.</param>
/// <param name="Person">The demonstration person it runs as: their key, such as <c>rhea</c>.</param>
/// <param name="Tenant">The slug the call is made in, or <see langword="null"/> when it names none.</param>
/// <param name="SendToken">Whether the call carries the person's access token.</param>
/// <param name="SendTenant">Whether the call carries the <c>Tenant</c> header with <paramref name="Tenant"/>.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, ids filled in.</param>
/// <param name="Body">The JSON body, ids filled in, or <see langword="null"/> for none.</param>
/// <param name="ExpectedStatus">The status the API answers.</param>
/// <param name="ExpectedCode">The problem's <c>code</c>, or <see langword="null"/> for an answer without one, such as a 401.</param>
public sealed record Attempt(
    string Id,
    string Title,
    string Person,
    string? Tenant,
    bool SendToken,
    bool SendTenant,
    string Method,
    string Path,
    object? Body,
    int ExpectedStatus,
    string? ExpectedCode);
