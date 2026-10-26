using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;

namespace Examples.Tenancy.Tenants.Domain.Aggregates.Seats;

// The seat's own rule about its own name, next to the package's: nested in the seat, as a partial of it, because the
// generator finds a class's rules among its own nested types.
public sealed partial class Seat
{
    /// <summary>
    /// A seat is shown by a name of 1 to <see cref="MaxDisplayNameLength"/> characters, without space around it. The
    /// application's rule about the application's field: Tenancy keeps no name and checks none.
    /// </summary>
    /// <remarks>
    /// <see cref="Rename"/> refuses with the same code and the same arguments before anything changes, so a client
    /// reads one code, and one translation serves both, whether the method refused or the save's check caught code
    /// that went round it.
    /// </remarks>
    public sealed class DisplayNameIsValid : IInvariant<Seat>
    {
        /// <summary>The code this rule is reported by.</summary>
        public const string ViolationCode = "tenants.seat.display-name";

        /// <summary>The English text, with its placeholder as written.</summary>
        public const string Text = "A seat's name is 1 to {Max} characters.";

        /// <summary>The input the rule is about, in the word the commands call it by.</summary>
        public const string Field = "displayName";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(Seat entity)
            => entity.DisplayName is { Length: > 0 and <= MaxDisplayNameLength } name && name == name.Trim()
                ? null
                : new InvariantFailure(Text.Replace("{Max}", MaxDisplayNameLength.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    .With("Max", MaxDisplayNameLength)
                    .With(RefusalException.FieldArgument, Field);
    }
}
