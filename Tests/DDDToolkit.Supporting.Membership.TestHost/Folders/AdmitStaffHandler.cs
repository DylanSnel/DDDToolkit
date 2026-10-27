using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.TestHost.Folders;

/// <summary>
/// Puts a member of staff on a folder. Its request requires the permission that decides a folder's staff, as a
/// document's requires the permission that shares. The folders also have a rule the host wrote itself, here in plain
/// sight: nobody puts staff on a folder for longer than they themselves hold that permission.
/// <para>
/// The package decides nothing of the kind. It answers until when the caller holds the permission
/// (<see cref="MemberHold{TResourceId}.Until"/>), asked here of the folders' access manager in one statement, and
/// the rule is two lines over that answer, in front of the member manager. The keeper holds every permission of a
/// folder with no end, and so does the application's own work, so neither is held back by it.
/// </para>
/// </summary>
/// <param name="access">The folders' access manager: until when the caller holds the permission.</param>
/// <param name="folders">The folders' member manager.</param>
public sealed class AdmitStaffHandler(MembershipAccessManager<Folder> access, FolderMemberManager folders)
{
    /// <summary>Puts the member of staff of <paramref name="command"/> on its folder.</summary>
    /// <param name="command">The request, which passed the host's checks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The folder, with the member of staff on it.</returns>
    /// <exception cref="RefusalException">
    /// <c>folders.longer-than-held</c> when the admission would outlast the caller's own hold of the permission; or the
    /// role is none of a folder's, or the folder's own rules refuse the admission.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">The folder is at another version than the caller named, or changed while this was saved.</exception>
    public async Task<Folder> HandleAsync(AdmitStaff command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Until when the caller holds the permission its request required: asked of the access manager, since the check let
        // the request through and the handler takes nothing of what it read. It refuses as the check refuses.
        var hold = await access.RequireAsync(command.Folder, FolderPermissions.Staff, cancellationToken);

        // The host's own rule. A hold with no end gives for as long as it likes; one that ends gives until it
        // ends at the latest, so an admission with no end is beyond it too.
        if (hold.Until is { } mine && (command.Until is not { } theirs || theirs > mine))
        {
            throw new RefusalException(
                FolderRefusals.LongerThanHeld,
                RefusalKind.NotPermitted,
                "Nobody is put on a folder for longer than you hold the permission to put them there.",
                new Dictionary<string, object?> { ["Until"] = mine });
        }

        return await folders.AddAsync(command.Folder, command.Staff, command.Role, command.Until, cancellationToken, command.ExpectedVersion);
    }
}
