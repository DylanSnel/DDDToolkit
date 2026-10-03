using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>
/// Shares a document. Who may was decided before it runs, by what its request requires: the key that shares,
/// held on that document. This host has no rule beyond that for documents, so the handler asks nobody anything
/// about its caller: whoever holds the key shares with anyone, in any role, for as long as they say.
/// </summary>
/// <param name="held">What the check read of the document a request is about, kept for that request.</param>
/// <param name="admission">What makes a share well formed: a member that is known, a role there is.</param>
/// <param name="context">The host's context.</param>
/// <param name="callers">Who is calling, for who shared.</param>
/// <param name="clock">The clock a share starts by.</param>
public sealed class ShareDocumentHandler(
    Checked<MemberHold<DocumentId>> held,
    MemberAdmission<DocumentId, UserId, NamedRole> admission,
    FilingContext context,
    ICallerAccessor callers,
    TimeProvider clock)
{
    /// <summary>Shares the document of <paramref name="command"/>, which passed the host's checks.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="RefusalException">The member is not known, the role is none of a document's, or the document's own rules refuse the share.</exception>
    /// <exception cref="ConcurrencyConflictException">The document changed since it was checked.</exception>
    public async Task HandleAsync(ShareDocument command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // What the check read: the very document it checked, at the version it checked it at.
        var hold = held.TakeFor(command);

        await admission.RequireMemberAsync(command.With, cancellationToken);
        if (command.Role is { } asked)
        {
            await admission.RequireRoleAsync(asked, cancellationToken);
        }

        var document = await context.Documents.SingleOrDefaultAsync(candidate => candidate.Id == hold.Resource && candidate.Version == hold.Version, cancellationToken)
            ?? throw new ConcurrencyConflictException(typeof(Document), hold.Resource);

        var now = clock.GetUtcNow();
        var period = MemberPeriod.Between(now, command.Until);
        UserId? by = callers.Current.UserId is { } user ? new UserId(user) : null;
        if (command.Role is { } role)
        {
            document.ShareWith(command.With, role, period, now, by);
        }
        else
        {
            document.ShareWith(command.With, period, now, by);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
