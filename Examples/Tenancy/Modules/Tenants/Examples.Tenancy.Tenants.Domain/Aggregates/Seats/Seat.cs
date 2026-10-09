using System.Globalization;
using DDDToolkit.Exceptions;

namespace Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

/// <summary>
/// The application's seat, one person's place in one tenant: the package's, with the name it is shown by in that
/// tenant, a job title, and a rule about each.
/// </summary>
/// <remarks>
/// The seat keeps no e-mail address and no contact details: the package knows a person only by the verified
/// identity their token carries. An application that wants more about a person adds it here, as the name and the
/// job title are added. Placements and grants stay the package's; they are changed through its use cases only.
/// <para>
/// <b>The name is this application's, not Tenancy's.</b> No access rule reads what a seat is called, so the package
/// keeps no name, and this application keeps one per tenant: the people of this sample sign in through Supabase
/// Auth, and the same person may be "Ada" in one tenant and "Ada Lovelace" in another. Whoever makes a seat names it
/// in the callback of the package's use case that makes it: the host's demo seeder in <c>ConfigureFirstSeat</c> when
/// it provisions a tenant and in the <c>configure</c> of <c>AddSeatAsync</c>, and the module's
/// <c>AcceptInvitation</c> in the <c>configure</c> of <c>AcceptAsync</c>, with the name the person gives. Renaming is
/// the module's own command, <c>RenameSeat</c>, with its own rule. The package's directory, and its lookup of a
/// person's own seats for the tenant picker, answer this class whole, and the module's queries select what they
/// show of it into a <c>SeatListing</c>, so every answer shows the name with no read more.
/// </para>
/// <para>
/// <b>Guarding the fields is this application's too.</b> In the database the package holds its own columns of the
/// seat, its id, identity, tenant and status, and nothing of these. Its policy lets a seat change its own row, and a
/// seat that manages seats or grants anywhere in the tenant change any seat's row. The module's infrastructure holds
/// the name to the rule of <c>RenameSeat</c> with a column rule of its own; the job title has no command and no rule,
/// and is as writable as the row.
/// </para>
/// </remarks>
[SeatAggregate<SeatId>]
public sealed partial class Seat
{
    /// <summary>The longest name a seat is shown by.</summary>
    public const int MaxDisplayNameLength = 200;

    /// <summary>The longest job title.</summary>
    public const int MaxJobTitleLength = 80;

    /// <summary>The name the seat is shown by in its tenant: trimmed, 1 to <see cref="MaxDisplayNameLength"/> characters.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>What the person does, such as "Site surveyor", or <see langword="null"/>.</summary>
    public string? JobTitle { get; private set; }

    /// <summary>
    /// Gives the seat the name it is shown by. The rule is checked here, before anything changes, so a callback of the
    /// package's that names a new seat refuses before the seat is handed to the store; <see cref="DisplayNameIsValid"/>
    /// states the same rule again when the seat is saved.
    /// </summary>
    /// <param name="displayName">The name; the space around it is dropped.</param>
    /// <exception cref="RefusalException"><c>tenants.seat.display-name</c>: blank or too long.</exception>
    public void Rename(string? displayName)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxDisplayNameLength)
        {
            throw new RefusalException(
                DisplayNameIsValid.ViolationCode,
                RefusalKind.Invalid,
                DisplayNameIsValid.Text.Replace("{Max}", MaxDisplayNameLength.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Max"] = MaxDisplayNameLength,
                    [RefusalException.FieldArgument] = DisplayNameIsValid.Field,
                });
        }

        DisplayName = name;
    }

    /// <summary>Sets or clears the job title. <see cref="JobTitleLength"/> judges it when the seat is saved.</summary>
    public void ChangeJobTitle(string? jobTitle) => JobTitle = jobTitle;
}
