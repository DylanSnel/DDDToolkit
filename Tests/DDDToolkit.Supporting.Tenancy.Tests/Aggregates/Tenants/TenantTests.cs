using DDDToolkit.Exceptions;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>A tenant's life moves one way: provisioned, active, suspended and back, and closed for good.</summary>
public class TenantTests
{
    private static HostTenant Active(TenantShape shape = TenantShape.Hierarchical)
    {
        var tenant = New.Tenant(shape);
        tenant.Activate<SeatId>();
        tenant.DrainEvents();
        return tenant;
    }

    [Fact]
    public void Provisioning_starts_in_provisioning_and_raises_TenantProvisioned()
    {
        var tenant = New.Tenant(TenantShape.Flat, " Harbor ", id: 42);

        tenant.Id.Should().Be(new TenantId(42));
        tenant.Status.Should().Be(TenantStatus.Provisioning);
        tenant.IsActive.Should().BeFalse();
        tenant.Shape.Should().Be(TenantShape.Flat);
        tenant.Slug.Value.Should().Be("harbor");
        tenant.StatusReason.Should().BeNull();

        tenant.PendingEvents().RaisedExactly<TenantProvisioned<TenantId, SeatId>>()
            .SingleEvent<TenantProvisioned<TenantId, SeatId>>()
            .Should().Match<TenantProvisioned<TenantId, SeatId>>(provisioned =>
                provisioned.TenantId == new TenantId(42) && provisioned.Slug == "harbor" && provisioned.Shape == TenantShape.Flat);
        tenant.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Activate_only_from_provisioning()
    {
        var scenario = New.Tenant().AsScenario().IgnorePendingEvents();

        scenario.When(tenant => tenant.Activate<SeatId>()).RaisedExactly<TenantActivated<TenantId, SeatId>>();
        scenario.Subject.Status.Should().Be(TenantStatus.Active);

        var refusal = scenario.WhenThrows<RefusalException>(tenant => tenant.Activate<SeatId>());
        refusal.Code.Should().Be(TenancyRefusals.TenantState);
        refusal.Arguments["Status"].Should().Be("active");
        refusal.Arguments["Action"].Should().Be("activate");
        refusal.Message.Should().Be("The tenant's current status does not allow this.");
    }

    [Fact]
    public void Suspend_needs_an_active_tenant_and_a_reason()
    {
        Refused.With(TenancyRefusals.TenantState, () => New.Tenant().Suspend<SeatId>("unpaid"));

        var scenario = Active().AsScenario();
        scenario.WhenThrows<RefusalException>(tenant => tenant.Suspend<SeatId>("   ")).Code.Should().Be(TenancyRefusals.ReasonRequired);
        scenario.WhenThrows<RefusalException>(tenant => tenant.Suspend<SeatId>(new string('x', 501))).Code.Should().Be(TenancyRefusals.NameInvalid);

        scenario.When(tenant => tenant.Suspend<SeatId>("  unpaid  "))
            .RaisedExactly<TenantSuspended<TenantId, SeatId>>()
            .SingleEvent<TenantSuspended<TenantId, SeatId>>().Reason.Should().Be("unpaid");
        scenario.Subject.Status.Should().Be(TenantStatus.Suspended);
        scenario.Subject.StatusReason.Should().Be("unpaid");
        scenario.Subject.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Reactivate_only_from_suspended()
    {
        var scenario = Active().AsScenario();
        scenario.WhenThrows<RefusalException>(tenant => tenant.Reactivate<SeatId>()).Code.Should().Be(TenancyRefusals.TenantState);

        scenario.When(tenant => tenant.Suspend<SeatId>("unpaid"));
        scenario.When(tenant => tenant.Reactivate<SeatId>()).RaisedExactly<TenantReactivated<TenantId, SeatId>>();

        scenario.Subject.Status.Should().Be(TenantStatus.Active);
        scenario.Subject.StatusReason.Should().Be("unpaid", "the last reason given is kept");
    }

    [Fact]
    public void Close_is_final_and_refuses_every_later_transition()
    {
        var scenario = Active().AsScenario();
        scenario.When(tenant => tenant.Suspend<SeatId>("unpaid"));

        scenario.When(tenant => tenant.Close<SeatId>("  wound up after the merger "))
            .RaisedExactly<TenantClosed<TenantId, SeatId>>()
            .SingleEvent<TenantClosed<TenantId, SeatId>>().Reason.Should().Be("wound up after the merger");
        scenario.Subject.Status.Should().Be(TenantStatus.Closed);
        scenario.Subject.StatusReason.Should().Be("wound up after the merger");

        foreach (var transition in new Action<HostTenant>[]
                 {
                     tenant => tenant.Activate<SeatId>(),
                     tenant => tenant.Suspend<SeatId>("again"),
                     tenant => tenant.Reactivate<SeatId>(),
                     tenant => tenant.Close<SeatId>("again"),
                     tenant => tenant.ChangeShape<SeatId>(TenantShape.Hierarchical),
                 })
        {
            scenario.WhenThrows<RefusalException>(transition).Code.Should().Be(TenancyRefusals.TenantState);
        }

        scenario.Subject.Status.Should().Be(TenantStatus.Closed);
    }

    [Fact]
    public void Provisioning_cannot_be_closed()
    {
        var refusal = Refused.With(TenancyRefusals.TenantState, () => New.Tenant().Close<SeatId>("never opened"));

        refusal.Arguments["Status"].Should().Be("provisioning");
        refusal.Arguments["Action"].Should().Be("close");
    }

    [Fact]
    public void Shape_goes_from_flat_to_hierarchical_only()
    {
        var scenario = Active(TenantShape.Flat).AsScenario();

        var changed = scenario.When(tenant => tenant.ChangeShape<SeatId>(TenantShape.Hierarchical))
            .RaisedExactly<TenantShapeChanged<TenantId, SeatId>>()
            .SingleEvent<TenantShapeChanged<TenantId, SeatId>>();
        changed.From.Should().Be(TenantShape.Flat);
        changed.To.Should().Be(TenantShape.Hierarchical);
        scenario.Subject.Shape.Should().Be(TenantShape.Hierarchical);

        scenario.WhenThrows<RefusalException>(tenant => tenant.ChangeShape<SeatId>(TenantShape.Flat)).Code.Should().Be(TenancyRefusals.ShapeChange);
        scenario.Subject.Shape.Should().Be(TenantShape.Hierarchical);
    }

    [Fact]
    public void The_same_shape_is_refused()
    {
        Refused.With(TenancyRefusals.ShapeChange, () => Active(TenantShape.Flat).ChangeShape<SeatId>(TenantShape.Flat));
        Refused.With(TenancyRefusals.ShapeChange, () => Active(TenantShape.Hierarchical).ChangeShape<SeatId>(TenantShape.Hierarchical));
    }

    [Theory]
    [InlineData("harbor", "harbor")]
    [InlineData("  Harbor-Works ", "harbor-works")]
    [InlineData("h", null)]
    [InlineData("-harbor", null)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Slug_follows_the_pattern(string given, string? expected)
    {
        var slug = TenantSlug.Create(given);

        if (expected is null)
        {
            slug.IsValid.Should().BeFalse();
            slug.ValidationErrors.Should().ContainSingle().Which.Code.Should().Be(TenancyRefusals.InvalidSlug);
            slug.Invoking(candidate => candidate.ToValid()).Should().Throw<InvalidValueObjectException>();
        }
        else
        {
            slug.IsValid.Should().BeTrue();
            slug.ToValid().Value.Should().Be(expected);
        }
    }
}
