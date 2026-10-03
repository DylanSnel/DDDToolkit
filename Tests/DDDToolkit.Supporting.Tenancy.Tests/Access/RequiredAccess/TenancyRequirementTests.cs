using System.Reflection;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// What a request declares where Tenancy is the one that knows: a closed set of cases, each a requirement of the
/// toolkit's, equal when they say the same, so a test of an application can hold every request to the case it is
/// meant to declare. What each case lets through is the storage package's check to prove.
/// </summary>
public class TenancyRequirementTests
{
    private const string OperatorRole = "operator";

    [Fact]
    public void Two_requirements_that_say_the_same_are_equal()
    {
        new TenancyRequirement.DecidedByThePackage().Should().Be(new TenancyRequirement.DecidedByThePackage());
        new TenancyRequirement.InTenant().Should().Be(new TenancyRequirement.InTenant());
        new TenancyRequirement.SystemWorkInTenant().Should().Be(new TenancyRequirement.SystemWorkInTenant());
        new TenancyRequirement.OperatorsOnly().Should().Be(new TenancyRequirement.OperatorsOnly());
        new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage).Should().Be(new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage))
            .And.NotBe(new TenancyRequirement.ForTheWholeTenant(TenancyKeys.SeatsManage), "another key is another requirement");

        AccessRequirement inTenant = new TenancyRequirement.InTenant();
        inTenant.Should().NotBe(new TenancyRequirement.SystemWorkInTenant(), "two cases are never each other");
        new TenancyRequirement.ForTheWholeTenant(TenancyKeys.RolesManage).Key.Should().Be(TenancyKeys.RolesManage);

        var north = OrganizationUnitId.CreateSequential();
        new TenancyRequirement.AtUnit<OrganizationUnitId>(TenancyKeys.UnitsManage, north).Should().Be(new TenancyRequirement.AtUnit<OrganizationUnitId>(TenancyKeys.UnitsManage, north))
            .And.NotBe(new TenancyRequirement.AtUnit<OrganizationUnitId>(TenancyKeys.UnitsManage, OrganizationUnitId.CreateSequential()), "another unit is another requirement")
            .And.NotBe(new TenancyRequirement.AtUnit<OrganizationUnitId>(TenancyKeys.SeatsManage, north), "and so is another key");
        ((AccessRequirement)new TenancyRequirement.AtUnit<OrganizationUnitId>(TenancyKeys.UnitsManage, north)).Should().NotBe(new TenancyRequirement.ForTheWholeTenant(TenancyKeys.UnitsManage));
        var atNorth = new TenancyRequirement.AtUnit<OrganizationUnitId>(TenancyKeys.UnitsManage, north);
        (atNorth.Key, atNorth.Unit).Should().Be((TenancyKeys.UnitsManage, north));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_key_for_the_whole_tenant_is_never_blank(string? key)
        => FluentActions.Invoking(() => new TenancyRequirement.ForTheWholeTenant(key!)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_key_at_a_unit_is_never_blank(string? key)
        => FluentActions.Invoking(() => new TenancyRequirement.AtUnit<OrganizationUnitId>(key!, OrganizationUnitId.CreateSequential())).Should().Throw<ArgumentException>();

    [Fact]
    public void The_cases_are_these_six_and_nobody_adds_one()
    {
        var cases = typeof(TenancyRequirement).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        cases.Select(each => each.Name).Should().BeEquivalentTo(
            nameof(TenancyRequirement.DecidedByThePackage),
            nameof(TenancyRequirement.InTenant),
            nameof(TenancyRequirement.SystemWorkInTenant),
            nameof(TenancyRequirement.ForTheWholeTenant),
            typeof(TenancyRequirement.AtUnit<>).Name,
            nameof(TenancyRequirement.OperatorsOnly));
        cases.Should().OnlyContain(each => each.IsSealed && each.IsNestedPublic && each.BaseType == typeof(TenancyRequirement));

        typeof(TenancyRequirement).IsAbstract.Should().BeTrue();
        typeof(TenancyRequirement).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters().Length == 0)
            .Should().ContainSingle().Which.IsPrivate.Should().BeTrue("a case of another assembly could not be made");
        typeof(AccessRequirement).IsAssignableFrom(typeof(TenancyRequirement)).Should().BeTrue("the toolkit's access checks hold a request to it");
    }

    [Fact]
    public void The_requirements_live_with_the_access_questions()
        => typeof(TenancyRequirement).Namespace.Should().Be(typeof(ITenancyAnswers<,,,>).Namespace, "a module that asks Tenancy names both with one using");

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
