using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;
using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;

namespace DDDToolkit.Supporting.Tenancy.TestHost.Domain;

/// <summary>
/// The application's tenant: the package's, with a field and an entity of its own. It declares no
/// constructor; the package makes it through the one the generator writes.
/// </summary>
[TenantAggregate<TenantId>]
public sealed partial class HostTenant
{
    /// <summary>Whether this is a tenant for demonstrations, which the package knows nothing about.</summary>
    public bool IsDemo { get; private set; }

    /// <summary>Notes the application keeps on a tenant: an entity of its own on the package's aggregate.</summary>
    public partial IReadOnlyList<TenantNote> Notes { get; }

    /// <summary>Marks the tenant as one for demonstrations.</summary>
    public void MarkAsDemo() => IsDemo = true;

    /// <summary>Adds a note. Its length is a rule of the note, checked with the tenant's.</summary>
    public TenantNote AddNote(string text)
    {
        var note = new TenantNote(TenantNoteId.CreateSequential(), text);
        _notes.Add(note);
        return note;
    }
}

/// <summary>A note on a tenant, 1 to 200 characters.</summary>
[Entity<Guid>]
public sealed partial class TenantNote
{
    /// <summary>The longest note.</summary>
    public const int MaxLength = 200;

    /// <summary>Creates a note.</summary>
    public TenantNote(TenantNoteId id, string text) : base(id) => Text = text;

    /// <summary>What the note says.</summary>
    public string Text { get; private set; }

    /// <summary>A note says something, and not too much.</summary>
    public sealed class TextLength : IInvariant<TenantNote>
    {
        /// <inheritdoc />
        public string Code => "host.tenant.note";

        /// <inheritdoc />
        public InvariantFailure? Check(TenantNote entity)
            => string.IsNullOrWhiteSpace(entity.Text) || entity.Text.Length > MaxLength
                ? new InvariantFailure($"A note is 1 to {MaxLength} characters.").With("Max", MaxLength)
                : null;
    }
}
