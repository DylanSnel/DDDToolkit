using System.Reflection;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// What a request declares where Tenancy is the one that knows: a closed set of cases, each a requirement of the
/// toolkit's, spelled with <see cref="TenancyAccess"/> and equal when they say the same, so a test of an
/// application can hold every request to the case it is meant to declare. What each case lets through is the
/// storage package's check to prove.
/// </summary>
public class TenancyRequirementTests
{
    private const string OperatorRole = "operator";

    [Fact]
    public void Two_requirements_that_say_the_same_are_equal()
    {
        TenancyAccess.InTenant().Should().Be(TenancyAccess.InTenant());
        TenancyAccess.RequiresOperator().Should().Be(TenancyAccess.RequiresOperator());
        TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage).Should().Be(TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage))
            .And.NotBe(TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage), "another key is another requirement");

        AccessRequirement inTenant = TenancyAccess.InTenant();
        inTenant.Should().NotBe(TenancyAccess.RequiresOperator(), "two cases are never each other")
            .And.NotBe(AccessRequirement.SignedIn(), "nor one of the core's");
        TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage).Key.Should().Be(TenancyKeys.RolesManage);

        var north = OrganizationUnitId.CreateSequential();
        TenancyAccess.AtUnit(TenancyKeys.UnitsManage, north).Should().Be(TenancyAccess.AtUnit(TenancyKeys.UnitsManage, north))
            .And.NotBe(TenancyAccess.AtUnit(TenancyKeys.UnitsManage, OrganizationUnitId.CreateSequential()), "another unit is another requirement")
            .And.NotBe(TenancyAccess.AtUnit(TenancyKeys.SeatsManage, north), "and so is another key");
        ((AccessRequirement)TenancyAccess.AtUnit(TenancyKeys.UnitsManage, north)).Should().NotBe(TenancyAccess.ForTheWholeTenant(TenancyKeys.UnitsManage));
        var atNorth = TenancyAccess.AtUnit(TenancyKeys.UnitsManage, north);
        (atNorth.Key, atNorth.Unit).Should().Be((TenancyKeys.UnitsManage, north));
    }

    [Fact]
    public void Each_method_answers_its_case_closed_over_the_unit_id_it_is_given()
    {
        var north = OrganizationUnitId.CreateSequential();

        TenancyAccess.InTenant().Should().BeOfType<TenancyRequirement.InTenant>();
        TenancyAccess.ForTheWholeTenant(TenancyKeys.RolesManage).Should().BeOfType<TenancyRequirement.ForTheWholeTenant>();
        TenancyAccess.AtUnit(TenancyKeys.UnitsManage, north).Should().BeOfType<TenancyRequirement.AtUnit<OrganizationUnitId>>("the unit's type is taken from the argument")
            .Which.Unit.Should().Be(north);
        TenancyAccess.RequiresOperator().Should().BeOfType<TenancyRequirement.Operator>();
    }

    [Fact]
    public void The_cases_are_made_by_TenancyAccess_alone_the_one_spelling_a_request_writes()
    {
        Type[] cases =
        [
            typeof(TenancyRequirement.InTenant),
            typeof(TenancyRequirement.ForTheWholeTenant),
            typeof(TenancyRequirement.AtUnit<OrganizationUnitId>),
            typeof(TenancyRequirement.Operator),
        ];

        cases.Should().AllSatisfy(type => type.GetConstructors().Should().BeEmpty("{0} is made by TenancyAccess, as the core's cases are made by AccessRequirement", type.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_key_for_the_whole_tenant_is_never_blank(string? key)
        => FluentActions.Invoking(() => TenancyAccess.ForTheWholeTenant(key!)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_key_at_a_unit_is_never_blank(string? key)
        => FluentActions.Invoking(() => TenancyAccess.AtUnit(key!, OrganizationUnitId.CreateSequential())).Should().Throw<ArgumentException>();

    [Fact]
    public void The_cases_are_these_four_and_nobody_adds_one()
    {
        // No case says that the package decides: every request says what it requires. Anyone, a signed-in user and
        // system work are the core's, which a host without Tenancy has too.
        var cases = typeof(TenancyRequirement).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        cases.Select(each => each.Name).Should().BeEquivalentTo(
            nameof(TenancyRequirement.InTenant),
            nameof(TenancyRequirement.ForTheWholeTenant),
            typeof(TenancyRequirement.AtUnit<>).Name,
            nameof(TenancyRequirement.Operator));
        cases.Should().OnlyContain(each => each.IsSealed && each.IsNestedPublic && each.BaseType == typeof(TenancyRequirement));

        typeof(TenancyRequirement).IsAbstract.Should().BeTrue();
        typeof(TenancyRequirement).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters().Length == 0)
            .Should().ContainSingle().Which.IsPrivate.Should().BeTrue("a case of another assembly could not be made");
        typeof(AccessRequirement).IsAssignableFrom(typeof(TenancyRequirement)).Should().BeTrue("the toolkit's access checks hold a request to it");
    }

    [Fact]
    public void The_requirements_live_with_the_access_questions()
    {
        typeof(TenancyRequirement).Namespace.Should().Be(typeof(ITenancyAnswers<,,,>).Namespace, "a module that asks Tenancy names both with one using");
        typeof(TenancyAccess).Namespace.Should().Be(typeof(TenancyRequirement).Namespace, "and the methods a request spells them with as well");
    }

    [Fact]
    public void Who_is_an_operator_is_said_in_one_place()
    {
        var options = new TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>();
        var odette = Guid.NewGuid();

        options.IsOperator(Caller.User(odette, OperatorRole)).Should().BeFalse("an application has no operators until it lists a token role");

        options.OperatorTokenRoles.Add(OperatorRole);

        options.IsOperator(Caller.User(odette, OperatorRole)).Should().BeTrue();
        Caller?[] others =
        [
            Caller.User(odette),
            Caller.User(odette, "analyst"),
            Caller.User(odette, role: null),
            Caller.User(odette, "Operator"),
            Caller.User(null, OperatorRole),
            Caller.Anonymous,
            Caller.System,
            Caller.SystemIn(TenancyWork.SystemScope),
            null,
        ];
        foreach (var other in others)
        {
            options.IsOperator(other).Should().BeFalse("{0} is no operator", other?.ToString() ?? "no caller");
        }
    }
}
