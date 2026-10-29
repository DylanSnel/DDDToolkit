using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>
/// Takes a user off a document. Who may was decided before it runs, at the door: the user itself, on a document it sees,
/// or whoever holds the permission that shares it. The user itself leaves; anybody else is removed, which needs that
/// permission.
/// </summary>
/// <remarks>
/// A document's rules name the permission that changes its members, for a database that checks every row: there a member row
/// is written only by a caller that holds that permission, and a member who leaves on its own need not hold it. The member
/// manager saves a member's own place as the application's own work, for the request in hand whose check read the
/// document; any other save runs as the caller, which the database judges as it judges every other.
/// </remarks>
/// <param name="documents">The documents' member manager.</param>
/// <param name="user">Who is calling, for whether the member is the caller itself.</param>
public sealed class LeaveDocumentHandler(DocumentShareManager documents, ICurrentUser user)
{
    /// <summary>Takes the member of <paramref name="command"/> off its document, which passed the host's checks.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The document, without the member.</returns>
    /// <exception cref="RefusalException">The document is not found, or the user is no member of it.</exception>
    /// <exception cref="ConcurrencyConflictException">The document is at another version than the caller named, or changed while this was saved.</exception>
    public Task<Document> HandleAsync(LeaveDocument command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return user.Id is { } id && new UserId(id) == command.Member
            ? documents.LeaveAsync(command.Document, cancellationToken, command.ExpectedVersion)
            : documents.RemoveAsync(command.Document, command.Member, cancellationToken, command.ExpectedVersion);
    }
}
