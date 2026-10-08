using DDDToolkit.Access;
using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Folders;

/// <summary>
/// Puts a member of staff on a folder. Its request requires the key that decides a folder's staff, as a
/// document's requires the key that shares. The folders also have a rule the host wrote itself, here in plain
/// sight: nobody puts staff on a folder for longer than they themselves hold that key.
/// <para>
/// The package decides nothing of the kind. It answers until when the caller holds the key
/// (<see cref="MemberHold{TResourceId}.Until"/>), asked here of the folders' questions in one statement, and
/// the rule is two lines over that answer. The keeper holds every key of a folder with no end, and so does the
/// application's own work, so neither is held back by it.
/// </para>
/// </summary>
/// <param name="questions">The folders' access questions: until when the caller holds the key.</param>
/// <param name="admission">What makes an admission well formed: a member that is known, a role there is.</param>
/// <param name="context">The host's context.</param>
/// <param name="callers">Who is calling, for who admitted.</param>
/// <param name="clock">The clock an admission starts by.</param>
public sealed class AdmitStaffHandler(
    IMemberQuestions<FolderId> questions,
    MemberAdmission<FolderId, StaffCode, NamedRole> admission,
    FilingContext context,
    ICallerAccessor callers,
    TimeProvider clock)
{
    /// <summary>Puts the member of staff of <paramref name="command"/> on its folder.</summary>
    /// <param name="command">The request, which passed the host's checks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="RefusalException">
    /// <c>folders.longer-than-held</c> when the admission would outlast the caller's own hold of the key; or the
    /// member is not known, the role is none of a folder's, or the folder's own rules refuse the admission.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The folder is at another version than the caller named, or changed while this was saved.</exception>
    public async Task HandleAsync(AdmitStaff command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Until when the caller holds the key its request required: asked of the questions, since the check let
        // the request through and the handler takes nothing of what it read. It refuses as the check refuses.
        var hold = await questions.RequireAsync(command.Folder, FolderKeys.Staff, cancellationToken);

        // The host's own rule. A hold with no end gives for as long as it likes; one that ends gives until it
        // ends at the latest, so an admission with no end is beyond it too.
        if (hold.Until is { } mine && (command.Until is not { } theirs || theirs > mine))
        {
            throw new RefusalException(
                FolderRefusals.LongerThanHeld,
                RefusalKind.NotPermitted,
                "Nobody is put on a folder for longer than you hold the key to put them there.",
                new Dictionary<string, object?> { ["Until"] = mine });
        }

        await admission.RequireMemberAsync(command.Staff, cancellationToken);
        await admission.RequireRoleAsync(command.Role, cancellationToken);

        // The folder the request names, which its check read, at the version the caller named, if it named one.
        var folder = await context.Folders.AsTracking().SingleOrDefaultAsync(candidate => candidate.Id == command.Folder, cancellationToken)
            ?? throw FolderRefusals.Membership.Refuse(MembershipRefusals.NotFound);
        context.ExpectVersion(folder, command.ExpectedVersion);

        StaffCode? by = callers.Current.Claim("app_metadata.staff") is { } code ? new StaffCode(code) : null;
        var now = clock.GetUtcNow();
        folder.Admit(command.Staff, command.Role, MemberPeriod.Between(now, command.Until), now, by);
        await context.SaveChangesAsync(cancellationToken);
    }
}
