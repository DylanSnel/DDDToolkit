using Examples.Tenancy.Tenants.Domain.Aggregates.Organizations.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Examples.Tenancy.Tenants.Infrastructure.Persistence;

/// <summary>
/// Stores a <see cref="UnitKind"/> by its key, its name in lower case: <c>region</c>, the way a request spells it
/// and the way the rows spell it that were written while the kind was a permission of the Tenancy package's catalogue.
/// So the rows from before the enum and the rows after it read alike, to Entity Framework and to SQL.
/// </summary>
/// <remarks>
/// A read ignores case, so a row written by hand as <c>Region</c> reads as well. A stored permission the enum has no
/// member for fails the read: the enum is the list of kinds, and a permission outside it is a row to correct, not one to
/// guess at.
/// </remarks>
internal sealed class UnitKindKeyConverter() : ValueConverter<UnitKind, string>(
    kind => kind.ToString().ToLowerInvariant(),
    permission => Enum.Parse<UnitKind>(permission, true));
