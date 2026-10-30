using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Examples.Tenancy.Inspections.Infrastructure.Access;
using Examples.Tenancy.Projects.Contracts.ValueObjects;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Inspections.Access;

/// <summary>
/// What the database itself lets a seat read and record of the inspections, as Inspections writes it for a database
/// that checks every row: two rules that ask about a project by its id, translated to the SQL an export fills in. They
/// name the project's id and the set, and nothing of Projects' tables or of the functions that answer: what those answer
/// once they are in a database is <c>SampleOnPostgresTests</c>' to show.
/// </summary>
public sealed class InspectionRowRulesTests
{
    [Fact]
    public void A_seat_reads_the_inspections_of_the_projects_it_sees()
        => SeatsSeeTheInspectionsOfProjectsTheySee.RowAccessSql.Should().Be(
            $"({{col:ProjectId}} = ANY (ARRAY(SELECT {{fn:{ResourceAccessAnswer.NameOf(typeof(ProjectId), ResourceAccessSet.Seen)}}}())))");

    [Fact]
    public void A_seat_records_in_its_own_name_where_it_holds_the_permission_that_records()
        => SeatsRecordWhereTheyMay.RowAccessSql.Should().Be(
            $"(({{col:ProjectId}} = ANY (ARRAY(SELECT {{fn:{ResourceAccessAnswer.NameOf(typeof(ProjectId), ResourceAccessSet.HeldOn)}}}('inspections.record')))) AND ({{col:RecordedBy}} = (SELECT {{fn:tenancy/current_seat}}())))");
}
