using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>
/// Takes a user off a document. Who may was decided before it runs, at the door: the user itself, on a document it sees,
/// or whoever holds the key that shares it. The handler loads the document its request names, at the version the caller
/// named, finds the member on it, takes it off and saves.
/// </summary>
/// <remarks>
/// A document's rules name the key that changes its members, for a database that checks every row: there a member row
/// is written only by a caller that holds that key, and a member who leaves on its own need not hold it. So the save of
/// a member's own place is the application's own work, begun for that one save once the request is the one in hand,
/// the one whose check let it through in this very handling. Any other save, of somebody else's place, runs as the
/// caller, which the database judges as it judges every other.
/// </remarks>
/// <param name="context">The host's context.</param>
/// <param name="callers">Who is calling, for whether the member is the caller itself.</param>
public sealed class LeaveDocumentHandler(FilingContext context, ICallerAccessor callers)
{
    /// <summary>Takes the member of <paramref name="command"/> off its document, which passed the host's checks.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="RefusalException">The document is not found, or the user is no member of it.</exception>
    /// <exception cref="ConcurrencyConflictException">The document is at another version than the caller named, or changed while this was saved.</exception>
    public async Task HandleAsync(LeaveDocument command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = await context.Documents.AsTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.Document, cancellationToken)
            ?? throw DocumentRefusals.Membership.Refuse(MembershipRefusals.NotFound);
        context.ExpectVersion(document, command.ExpectedVersion);

        // The door let the caller act on itself, not on a document it has nothing to do with.
        if (document.Shares.All(share => share.MemberId != command.Member))
        {
            throw DocumentRefusals.Membership.Refuse(MembershipRefusals.MemberNotFound);
        }

        document.Unshare(command.Member);
        using (OwnPlace(command))
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The application's own work for the save of the caller's own place, where <paramref name="command"/> names the caller
    /// and passed its check in this handling; <see langword="null"/>, the save as the caller, otherwise.
    /// </summary>
    private IDisposable? OwnPlace(LeaveDocument command)
        => callers.Current.UserId is { } user && new UserId(user) == command.Member && ReferenceEquals(RequestInHand.Current?.Request, command)
            ? Callers.Begin(DDDToolkit.Abstractions.Access.Caller.System)
            : null;
}
