using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Documents;

/// <summary>
/// Shares a document. Who may was decided before it runs, by what its request requires: the key that shares,
/// held on that document. This host has no rule beyond that for documents, so the handler asks nobody anything
/// about its caller: whoever holds the key shares with anyone, in any role, for as long as they say.
/// <para>
/// The default path: it loads the document its request names, which is the one the check read, at the version
/// the caller named when it named one, and saves. Nothing of what the check read is taken here.
/// </para>
/// </summary>
/// <param name="admission">What makes a share well formed: a member that is known, a role there is.</param>
/// <param name="context">The host's context.</param>
/// <param name="callers">Who is calling, for who shared.</param>
/// <param name="clock">The clock a share starts by.</param>
public sealed class ShareDocumentHandler(
    MemberAdmission<DocumentId, UserId, NamedRole> admission,
    FilingContext context,
    ICallerAccessor callers,
    TimeProvider clock)
{
    /// <summary>Shares the document of <paramref name="command"/>, which passed the host's checks.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="RefusalException">The member is not known, the role is none of a document's, or the document's own rules refuse the share.</exception>
    /// <exception cref="ConcurrencyConflictException">The document is at another version than the caller named, or changed while this was saved.</exception>
    public async Task HandleAsync(ShareDocument command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await admission.RequireMemberAsync(command.With, cancellationToken);
        if (command.Role is { } asked)
        {
            await admission.RequireRoleAsync(asked, cancellationToken);
        }

        // The document the request names, which its check read, at the version the caller named, if it named one.
        var document = await context.Documents.AsTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.Document, cancellationToken)
            ?? throw DocumentRefusals.Membership.Of(MembershipRefusals.NotFound);
        context.ExpectVersion(document, command.ExpectedVersion);

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
