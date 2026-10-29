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
        TenancyAccess.Operator().Should().Be(TenancyAccess.Operator());
        TenancyAccess.AtTenant(TenancyPermissions.RolesManage).Should().Be(TenancyAccess.AtTenant(TenancyPermissions.RolesManage))
            .And.NotBe(TenancyAccess.AtTenant(TenancyPermissions.SeatsManage), "another permission is another requirement");

        AccessRequirement inTenant = TenancyAccess.InTenant();
        inTenant.Should().NotBe(TenancyAccess.Operator(), "two cases are never each other")
            .And.NotBe(AccessRequirement.SignedIn(), "nor one of the core's");
        TenancyAccess.AtTenant(TenancyPermissions.RolesManage).Permission.Should().Be(TenancyPermissions.RolesManage);

        var north = OrganizationUnitId.CreateSequential();
        TenancyAccess.AtUnit(TenancyPermissions.UnitsManage, north).Should().Be(TenancyAccess.AtUnit(TenancyPermissions.UnitsManage, north))
            .And.NotBe(TenancyAccess.AtUnit(TenancyPermissions.UnitsManage, OrganizationUnitId.CreateSequential()), "another unit is another requirement")
            .And.NotBe(TenancyAccess.AtUnit(TenancyPermissions.SeatsManage, north), "and so is another permission");
        ((AccessRequirement)TenancyAccess.AtUnit(TenancyPermissions.UnitsManage, north)).Should().NotBe(TenancyAccess.AtTenant(TenancyPermissions.UnitsManage));
        var atNorth = TenancyAccess.AtUnit(TenancyPermissions.UnitsManage, north);
        (atNorth.Permission, atNorth.Unit).Should().Be((TenancyPermissions.UnitsManage, north));
    }

    [Fact]
    public void An_operators_requirement_says_where_its_commands_work_acts_and_in_which_scope()
    {
        var harbor = new TenantId(1);

        TenancyAccess.Operator().Should().Be(TenancyAccess.Operator(OperatorTenant.FromCommand, TenancyWork.SystemScope), "no tenant and Tenancy's own scope by default");
        TenancyAccess.Operator().Should().NotBe(TenancyAccess.Operator(OperatorTenant.FromRequest), "the tenant the request names is another place")
            .And.NotBe(TenancyAccess.Operator(scope: "projects"), "and another scope is other work");
        (TenancyAccess.Operator(OperatorTenant.FromRequest, "projects").Tenant, TenancyAccess.Operator(OperatorTenant.FromRequest, "projects").Scope)
            .Should().Be((OperatorTenant.FromRequest, "projects"));

        TenancyAccess.Operator(harbor).Should().BeOfType<TenancyRequirement.OperatorIn<TenantId>>("the tenant's type is taken from the argument")
            .Which.Should().Be(TenancyAccess.Operator(harbor, scope: TenancyWork.SystemScope));
        TenancyAccess.Operator(harbor).Should().NotBe(TenancyAccess.Operator(new TenantId(2)), "another tenant is another requirement");
        ((AccessRequirement)TenancyAccess.Operator(harbor)).Should().NotBe(TenancyAccess.Operator());
        TenancyAccess.Operator(harbor, scope: "projects").Scope.Should().Be("projects");
    }

    [Fact]
    public void An_operators_requirement_names_one_tenant_and_a_scope_that_is_one()
    {
        var harbor = new TenantId(1);

        FluentActions.Invoking(() => TenancyAccess.Operator(harbor, OperatorTenant.FromRequest))
            .Should().Throw<ArgumentException>("a request that names its tenant acts in that one").WithParameterName("tenant");
        FluentActions.Invoking(() => TenancyAccess.Operator(scope: "Not A Scope")).Should().Throw<ArgumentException>().WithParameterName("scope");
        FluentActions.Invoking(() => TenancyAccess.Operator(harbor, scope: "")).Should().Throw<ArgumentException>().WithParameterName("scope");
        FluentActions.Invoking(() => TenancyAccess.Operator((OperatorTenant)7)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Each_method_answers_its_case_closed_over_the_unit_id_it_is_given()
    {
        var north = OrganizationUnitId.CreateSequential();

        TenancyAccess.InTenant().Should().BeOfType<TenancyRequirement.InTenant>();
        TenancyAccess.AtTenant(TenancyPermissions.RolesManage).Should().BeOfType<TenancyRequirement.AtTenant>();
        TenancyAccess.AtUnit(TenancyPermissions.UnitsManage, north).Should().BeOfType<TenancyRequirement.AtUnit<OrganizationUnitId>>("the unit's type is taken from the argument")
            .Which.Unit.Should().Be(north);
        TenancyAccess.Operator().Should().BeOfType<TenancyRequirement.Operator>();
        TenancyAccess.Operator(new TenantId(1)).Should().BeOfType<TenancyRequirement.OperatorIn<TenantId>>();
    }

    [Fact]
    public void The_cases_are_made_by_TenancyAccess_alone_the_one_spelling_a_request_writes()
    {
        Type[] cases =
        [
            typeof(TenancyRequirement.InTenant),
            typeof(TenancyRequirement.AtTenant),
            typeof(TenancyRequirement.AtUnit<OrganizationUnitId>),
            typeof(TenancyRequirement.Operator),
            typeof(TenancyRequirement.OperatorIn<TenantId>),
        ];

        cases.Should().AllSatisfy(type => type.GetConstructors().Should().BeEmpty("{0} is made by TenancyAccess, as the core's cases are made by AccessRequirement", type.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_permission_for_the_whole_tenant_is_never_blank(string? permission)
        => FluentActions.Invoking(() => TenancyAccess.AtTenant(permission!)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_permission_at_a_unit_is_never_blank(string? permission)
        => FluentActions.Invoking(() => TenancyAccess.AtUnit(permission!, OrganizationUnitId.CreateSequential())).Should().Throw<ArgumentException>();

    [Fact]
    public void The_cases_are_these_five_and_nobody_adds_one()
    {
        // No case says that the package decides: every request says what it requires. Anyone, a signed-in user and
        // system work are the core's, which a host without Tenancy has too.
        var cases = typeof(TenancyRequirement).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        cases.Select(each => each.Name).Should().BeEquivalentTo(
            nameof(TenancyRequirement.InTenant),
            nameof(TenancyRequirement.AtTenant),
            typeof(TenancyRequirement.AtUnit<>).Name,
            nameof(TenancyRequirement.Operator),
            typeof(TenancyRequirement.OperatorIn<>).Name);
        cases.Should().OnlyContain(each => each.IsSealed && each.IsNestedPublic && each.BaseType == typeof(TenancyRequirement));

        typeof(TenancyRequirement).IsAbstract.Should().BeTrue();
        typeof(TenancyRequirement).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters().Length == 0)
            .Should().ContainSingle().Which.IsPrivate.Should().BeTrue("a case of another assembly could not be made");
        typeof(AccessRequirement).IsAssignableFrom(typeof(TenancyRequirement)).Should().BeTrue("the toolkit's access checks hold a request to it");
    }

    [Fact]
    public void The_requirements_live_with_the_access_manager()
    {
        typeof(TenancyRequirement).Namespace.Should().Be(typeof(TenancyAccessManager<,,,>).Namespace, "a module that asks Tenancy names both with one using");
        typeof(TenancyAccess).Namespace.Should().Be(typeof(TenancyRequirement).Namespace, "and the methods a request spells them with as well");
    }

    [Fact]
    public void Who_is_an_operator_is_said_in_one_place()
    {
        var options = new TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>();
        var odette = Guid.NewGuid();

        options.IsOperator(CurrentUserSnapshot.User(odette, OperatorRole)).Should().BeFalse("an application has no operators until it lists a token role");

        options.OperatorTokenRoles.Add(OperatorRole);

        options.IsOperator(CurrentUserSnapshot.User(odette, OperatorRole)).Should().BeTrue();
        CurrentUserSnapshot?[] others =
        [
            CurrentUserSnapshot.User(odette),
            CurrentUserSnapshot.User(odette, "analyst"),
            CurrentUserSnapshot.User(odette, role: null),
            CurrentUserSnapshot.User(odette, "Operator"),
            CurrentUserSnapshot.User(null, OperatorRole),
            CurrentUserSnapshot.Anonymous,
            CurrentUserSnapshot.System,
            CurrentUserSnapshot.SystemIn(TenancyWork.SystemScope),
            null,
        ];
        foreach (var other in others)
        {
            options.IsOperator(other).Should().BeFalse("{0} is no operator", other?.ToString() ?? "no caller");
        }
    }
}
