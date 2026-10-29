using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>
/// Shares a document. Who may was decided before it runs, by what its request requires: the permission that shares,
/// held on that document, which the documents' rules mark as the one that manages the members. This host has no rule
/// beyond that for documents, so the handler is one call: whoever holds the permission shares with anyone, in any role,
/// for as long as they say.
/// <para>
/// The member manager takes the check's answer rather than asking again, loads the document the request names at the
/// version the caller named, shares it, and saves.
/// </para>
/// </summary>
/// <param name="documents">The documents' member manager.</param>
public sealed class ShareDocumentHandler(DocumentShareManager documents)
{
    /// <summary>Shares the document of <paramref name="command"/>, which passed the host's checks.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The document, shared.</returns>
    /// <exception cref="RefusalException">The role is none of a document's, or the document's own rules refuse the share.</exception>
    /// <exception cref="ConcurrencyConflictException">The document is at another version than the caller named, or changed while this was saved.</exception>
    public Task<Document> HandleAsync(ShareDocument command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return documents.AddAsync(command.Document, command.With, command.Role, command.Until, cancellationToken, command.ExpectedVersion);
    }
}
