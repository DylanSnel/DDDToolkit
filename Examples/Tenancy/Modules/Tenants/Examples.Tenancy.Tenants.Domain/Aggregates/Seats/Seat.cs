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
/// person's own seats for the tenant picker, hand this class to the module's view of a seat, <c>SeatListing</c>, so
/// every answer shows the name with no read more.
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
