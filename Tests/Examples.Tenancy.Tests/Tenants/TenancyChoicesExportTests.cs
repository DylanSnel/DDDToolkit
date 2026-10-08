using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants;

/// <summary>
/// The two choices Tenancy leaves to the application that the database holds too, written by the exporter's own
/// build step: whether a seat hands on a key that manages access only where it holds it, containment, which the
/// sample leaves on, and who reads the seats, which the sample leaves to Tenancy's default, every member of the
/// tenant. Tenancy's SQL reaches the sample's files because its modules reference Tenancy's Postgres package, made
/// from the catalogue the sample marks, for the roles its exporter says once, from the contexts marked
/// <c>[SupabaseMigrations]</c>. Made the other way, each choice changes the Tenants module's access file in that
/// choice alone.
/// </summary>
/// <remarks>
/// The export runs over a copy of the sample's migrations and connects to nothing, so no database is needed. What the
/// written SQL then does on Postgres, Tenancy's own tests show (<c>ContainmentTests</c>, <c>ReadRulesTests</c>).
/// </remarks>
public sealed class TenancyChoicesExportTests
{
    /// <summary>The Tenants module's access file the sample's build wrote last.</summary>
    private const string TenantsAccessFile = "20261006170417_access.tenants.ddd.sql";

    /// <summary>
    /// A read rule on the sample's seat the sample does not have, as the generator writes one:
    /// <c>[RowAccess&lt;Seat&gt;(RowOperations.Read)]</c> on <c>MembersReadThePeopleOfTheirUnits</c>, whose <c>Allows</c>
    /// is <c>TenancyRowAccess.SeatsInMyUnits&lt;SeatId&gt;().Contains(seat.Id)</c>. Without <c>To</c>, as the docs write it.
    /// </summary>
    private static readonly RowAccessRule PeopleOfTheirUnits = RowAccessRule.For(
        typeof(Seat).FullName!, "Members read the people of their units", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:tenancy/seats_in_my_units}())))");

    [Fact]
    public void As_the_sample_is_the_build_writes_nothing_its_files_do_not_hold()
        => ExporterBuild.Written().Should().BeEmpty("the files are what the exporter's build writes, so this test writes what that build would");

    [Fact]
    public void A_read_rule_on_the_sample_s_seat_takes_the_place_of_tenancy_s_default_read_of_the_seats_and_changes_nothing_else()
    {
        var (removed, added) = Changed(ExporterBuild.Written(moreRules: [PeopleOfTheirUnits]));

        removed.Should().BeEquivalentTo(
            [
                "-- Members and the person read seats (select) for authenticated asks the policy 'Members and the person read seats' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.",
                "CREATE POLICY \"Members and the person read seats (select) for authenticated\" ON tenancy.\"Seats\" FOR SELECT TO authenticated",
                "    USING (\"TenantId\" = (SELECT tenancy.caller_tenant()) OR \"Identity\" = (SELECT auth.uid()));",
                "COMMENT ON POLICY \"Members and the person read seats (select) for authenticated\" ON tenancy.\"Seats\" IS 'DDDToolkit row access rule';",
            ],
            "Tenancy's default read of the seats is the one policy that goes");

        string.Join("\n", added).Should()
            .Contain("-- Seats (select) for authenticated asks the rule 'Members read the people of their units' (which names no role, so it is for the roles of the default) " +
                     "in place of the default 'Members and the person read seats' of the row access contribution " +
                     "DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres")
            .And.Contain("CREATE POLICY \"Seats (select) for authenticated\" ON tenancy.\"Seats\" FOR SELECT TO authenticated")
            .And.Contain(
                "    USING ((\"Identity\" = (SELECT auth.uid()) OR ((\"TenantId\" = (SELECT tenancy.caller_tenant())) AND ((SELECT tenancy.holds_key('tenancy.seats.manage')) " +
                "OR (SELECT tenancy.holds_key('tenancy.grants.manage')) OR (SELECT tenancy.holds_key('tenancy.units.manage')) OR (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')))))" +
                " OR ((\"TenantId\" = (SELECT tenancy.caller_tenant())) AND (\"Id\" = ANY (ARRAY(SELECT tenancy.seats_in_my_units())))));",
                "the rule decides within the calling seat's tenant, beside a person's own seats and what Tenancy's managers read, for authenticated, the user's role, which nothing had to say");
        added.Should().OnlyContain(line => line.Contains("Seats (select) for authenticated") || line.StartsWith("    USING ((\"Identity\"", StringComparison.Ordinal),
            "the rule's policy is all that comes in its place, and no other policy, function or grant changes");
    }

    [Fact]
    public void With_containment_off_the_export_writes_key_is_contained_to_answer_no_key_and_changes_nothing_else()
    {
        var (removed, added) = Changed(ExporterBuild.Written(application: catalogue => catalogue with { ContainAccessManagingKeys = false }));

        removed.Should().ContainSingle().Which.Should().StartWith("SELECT $1 IN ('projects.crew.manage', ", "on, the function answers the keys that manage access");
        added.Should().Equal(["SELECT false"], "off, it answers none, and the policies and the trigger that ask it stay as they are");
    }

    /// <summary>
    /// What the one Tenants access file the export wrote has that the committed one has not, and the other way round,
    /// line by line; and that it wrote nothing else.
    /// </summary>
    private static (List<string> Removed, List<string> Added) Changed(IReadOnlyDictionary<string, string> written)
    {
        var file = written.Should().ContainSingle("one choice changes one module's access file, the Tenants module's").Which;
        file.Key.Should().EndWith("_access.tenants.ddd.sql");

        var before = ExporterBuild.Committed(TenantsAccessFile).Split('\n');
        var after = file.Value.Split('\n');
        return ([.. Without(before, after)], [.. Without(after, before)]);
    }

    /// <summary>The lines of <paramref name="lines"/> that <paramref name="other"/> does not have as often.</summary>
    private static IEnumerable<string> Without(IEnumerable<string> lines, IEnumerable<string> other)
    {
        var left = other.GroupBy(line => line, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (left.TryGetValue(line, out var count) && count > 0)
            {
                left[line] = count - 1;
                continue;
            }

            yield return line;
        }
    }
}
