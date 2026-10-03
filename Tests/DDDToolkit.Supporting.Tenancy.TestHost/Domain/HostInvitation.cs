using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>
/// The application's invitation: the package's, with a note of its own for whoever is invited, which the package
/// knows nothing about.
/// </summary>
[InvitationAggregate<InvitationId>]
public sealed partial class HostInvitation
{
    /// <summary>The longest note.</summary>
    public const int MaxNoteLength = 280;

    /// <summary>What the issuer wrote to go with the invitation, or <see langword="null"/>.</summary>
    public string? Note { get; private set; }

    /// <summary>Sets or clears the note.</summary>
    public void ChangeNote(string? note) => Note = note;
}
