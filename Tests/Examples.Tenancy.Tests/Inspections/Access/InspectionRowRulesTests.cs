using Examples.Tenancy.Inspections.Infrastructure.Access;
using Examples.Tenancy.Projects.Contracts.RowAccess;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Inspections.Access;

/// <summary>
/// What the database itself lets a seat read and record of the inspections, as Inspections writes it for a database
/// that checks every row: two rules that ask Projects' contracts, translated to the SQL an export fills in. They name
/// the project's id and the set, and nothing of Projects' tables or of the functions that answer: what those answer
/// once they are in a database is <c>SampleOnPostgresTests</c>' to show.
/// </summary>
public sealed class InspectionRowRulesTests
{
    [Fact]
    public void A_seat_reads_the_inspections_of_the_projects_it_sees()
        => SeatsSeeTheInspectionsOfProjectsTheySee.RowAccessSql.Should().Be(
            $"({{col:ProjectId}} = ANY (ARRAY(SELECT {{fn:{ProjectsISee.Name}}}())))");

    [Fact]
    public void A_seat_records_in_its_own_name_where_it_holds_the_key_that_records()
        => SeatsRecordWhereTheyMay.RowAccessSql.Should().Be(
            $"(({{col:ProjectId}} = ANY (ARRAY(SELECT {{fn:{ProjectsWhereIHold.Name}}}('inspections.record')))) AND ({{col:RecordedBy}} = (SELECT {{fn:tenancy/caller_seat}}())))");
}
