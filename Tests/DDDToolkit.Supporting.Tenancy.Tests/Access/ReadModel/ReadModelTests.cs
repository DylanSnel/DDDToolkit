using System.Reflection;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The read model carries what an access rule reads, and nothing else. Its rows are mapped into every module's
/// model and answered by a database function each, so what a row has is what every module can read of Tenancy:
/// the list is pinned here, property by property.
/// </summary>
public class ReadModelTests
{
    /// <summary>
    /// Every row of the read model with its properties, in the order the class declares them: ids, keys, periods,
    /// statuses, a unit's parent, and a role's pack and keys.
    /// <para>
    /// A property added to a row fails here, and that is the point. A text that is shown to people never comes
    /// back: no seat's display name, no unit's name or kind, no role's name. A module that could read one would
    /// lean on Tenancy for what it shows; names are asked of the directory, by id. Add a property only when an
    /// access rule reads it, and add it to the database's read functions in the same change.
    /// </para>
    /// </summary>
    private static readonly Dictionary<Type, string[]> Rows = new()
    {
        [typeof(SeatRight<,,,>)] = ["TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt"],
        [typeof(OrganizationUnitPath<,>)] = ["TenantId", "AncestorId", "DescendantId", "Distance"],
        [typeof(OrganizationUnitRow<,>)] = ["Id", "TenantId", "ParentId", "Status"],
        [typeof(RoleRow<,>)] = ["Id", "TenantId", "FromPack", "Status", "Keys"],
        [typeof(PlacementRow<,>)] = ["SeatId", "UnitId", "IsPrimary"],
        [typeof(SeatRow<,>)] = ["Id", "TenantId", "Status"],
    };

    /// <summary>The rows a read source offers: the row type of each of its sets.</summary>
    private static Type[] RowsOfTheReadSource()
        => [.. typeof(ITenancyReadSource<,,,>)
            .GetProperties()
            .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            .Select(property => property.PropertyType.GetGenericArguments()[0].GetGenericTypeDefinition())];

    private static PropertyInfo[] PropertiesOf(Type row)
        => [.. row.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(property => property.MetadataToken)];

    [Fact]
    public void The_read_rows_carry_access_facts_only()
    {
        RowsOfTheReadSource().Should().BeEquivalentTo(Rows.Keys, "every set of the read source is a row pinned here, and no other row is");

        foreach (var (row, properties) in Rows)
        {
            PropertiesOf(row).Select(property => property.Name).Should().Equal(
                properties,
                row.Name + " has exactly these properties: a module's model maps every one of them, and a database function answers every one");
            row.GetFields(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty(row.Name + " has no public field either");
        }
    }

    [Fact]
    public void No_read_row_has_a_text_but_a_key_or_a_pack()
    {
        var texts = Rows.Keys
            .SelectMany(row => PropertiesOf(row).Where(property => property.PropertyType == typeof(string)).Select(property => row.Name + "." + property.Name))
            .ToArray();
        var lists = Rows.Keys
            .SelectMany(row => PropertiesOf(row)
                .Where(property => property.PropertyType != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType))
                .Select(property => row.Name + "." + property.Name))
            .ToArray();

        texts.Should().BeEquivalentTo(
            [typeof(SeatRight<,,,>).Name + ".Key", typeof(RoleRow<,>).Name + ".FromPack"],
            "a key and a pack's key are identifiers of the catalogue; any other text would be one that is shown to people");
        lists.Should().BeEquivalentTo([typeof(RoleRow<,>).Name + ".Keys"], "the only list of texts is a role's keys");
    }
}
