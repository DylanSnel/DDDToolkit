using DDDToolkit.Supporting.Tenancy.EntityFramework;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// A role class that marks its tenant <c>[OfTenant]</c> is mapped by both packages' conventions with no line in its
/// context: Tenancy keeps it to the caller's tenant, and Membership gives a name and a starter role once per tenant, in
/// unique indexes Entity Framework names as it names one written by hand.
/// </summary>
public sealed class RoleClassOfATenantTests
{
    [Fact]
    public void A_role_class_that_marks_its_tenant_gets_a_name_and_a_starter_role_once_per_tenant()
    {
        using var context = new CampusContext(new DbContextOptionsBuilder<CampusContext>().UseSqlite("DataSource=:memory:").Options);
        var role = context.Model.FindEntityType(typeof(CourseRole))!;

        TenancyModel.TenantPropertyOf(role)!.Name.Should().Be(nameof(CourseRole.TenantId), "Tenancy's convention keeps the role to its college");
        role.GetIndexes()
            .Select(index => (Properties: string.Join(", ", index.Properties.Select(property => property.Name)), index.IsUnique, Name: index.GetDatabaseName()))
            .Should().BeEquivalentTo(
            [
                (Properties: "TenantId, Name", IsUnique: true, Name: "IX_CourseRoles_TenantId_Name"),
                (Properties: "TenantId, MadeFrom", IsUnique: true, Name: "IX_CourseRoles_TenantId_MadeFrom"),
            ]);
    }
}
