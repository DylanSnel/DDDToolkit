using System.Globalization;
using DDDToolkit.Exceptions;
using FluentAssertions;

namespace DDDToolkit.Tests.Exceptions;

/// <summary>
/// A refused command carries a code to branch on, a kind to choose a status from and the values its
/// message was built from. These pin what an edge reads to answer without parsing the message.
/// </summary>
public class RefusalExceptionTests
{
    [Fact]
    public void The_toolkits_own_refusals_have_a_code_a_kind_and_a_text()
    {
        ToolkitRefusals.Codes.Should().Equal(ToolkitRefusals.Refused, ToolkitRefusals.RoleNotAllowed, ToolkitRefusals.NotSignedIn, ToolkitRefusals.SystemOnly);
        ToolkitRefusals.Refused.Should().Be("access.refused");
        ToolkitRefusals.KindOf(ToolkitRefusals.Refused).Should().Be(RefusalKind.NotPermitted, "the caller may not do this, and a retry gives the same answer");
        ToolkitRefusals.TemplateOf(ToolkitRefusals.Refused).Should().Be("The database refused this change.");

        var cause = new InvalidOperationException("What the database said.");
        var refusal = ToolkitRefusals.Refuse(ToolkitRefusals.Refused, cause);
        refusal.Code.Should().Be(ToolkitRefusals.Refused);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("The database refused this change.");
        refusal.Arguments.Should().BeEmpty("the caller is told no more than that");
        refusal.InnerException.Should().BeSameAs(cause, "what it was made of is kept, for a log to show");
        ToolkitRefusals.Refuse(ToolkitRefusals.Refused).InnerException.Should().BeNull();

        FluentActions.Invoking(() => ToolkitRefusals.Refuse("access.unheard-of")).Should().Throw<ArgumentException>().WithMessage("*not one of the toolkit's refusal codes*");
        FluentActions.Invoking(() => ToolkitRefusals.KindOf(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_caller_who_is_not_who_a_request_requires_is_refused_with_one_of_two_codes()
    {
        ToolkitRefusals.NotSignedIn.Should().Be("access.not-signed-in");
        ToolkitRefusals.KindOf(ToolkitRefusals.NotSignedIn).Should().Be(RefusalKind.NotPermitted, "the same caller gets the same answer until it signs in");
        ToolkitRefusals.TemplateOf(ToolkitRefusals.NotSignedIn).Should().Be("Only a signed-in user can do this.");

        ToolkitRefusals.SystemOnly.Should().Be("access.system-only");
        ToolkitRefusals.KindOf(ToolkitRefusals.SystemOnly).Should().Be(RefusalKind.NotPermitted, "no key or role gives it");
        ToolkitRefusals.TemplateOf(ToolkitRefusals.SystemOnly).Should().Be("Only the application itself can do this.");

        ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly).Arguments.Should().BeEmpty("it says nothing of the caller, whatever they hold");
    }

    [Fact]
    public void A_role_that_is_not_allowed_is_refused_naming_the_role()
    {
        ToolkitRefusals.RoleNotAllowed.Should().Be("access.role-not-allowed");
        ToolkitRefusals.KindOf(ToolkitRefusals.RoleNotAllowed).Should().Be(RefusalKind.NotPermitted, "the same sign-in gets the same answer until the host lists its role");
        ToolkitRefusals.TemplateOf(ToolkitRefusals.RoleNotAllowed).Should().Be("The role this sign-in carries gives no access here: {Role}.");

        var refusal = ToolkitRefusals.Refuse(ToolkitRefusals.RoleNotAllowed, ("Role", "intern"));
        refusal.Code.Should().Be(ToolkitRefusals.RoleNotAllowed);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("The role this sign-in carries gives no access here: intern.", "the English text is filled from the arguments");
        refusal.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Role"] = "intern" }, "and a translation gets them to fill its own");
        refusal.Arguments["role"].Should().Be("intern", "names are matched without regard to case");
        refusal.InnerException.Should().BeNull();

        // A value is written the same wherever the code runs, and a placeholder nothing fills stays as written.
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            ToolkitRefusals.Refuse(ToolkitRefusals.RoleNotAllowed, ("Role", 1.5m)).Message.Should().EndWith(": 1.5.");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        ToolkitRefusals.Refuse(ToolkitRefusals.RoleNotAllowed, ("Role", null)).Message.Should().EndWith("here: .");
        ToolkitRefusals.Refuse(ToolkitRefusals.RoleNotAllowed, ("Other", "x")).Message.Should().EndWith("here: {Role}.");
        ToolkitRefusals.Refuse(ToolkitRefusals.Refused, ("Role", "intern")).Message.Should().Be("The database refused this change.", "a text without placeholders is as it is");

        FluentActions.Invoking(() => ToolkitRefusals.Refuse("access.unheard-of", ("Role", "intern"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ToolkitRefusals.Refuse(ToolkitRefusals.RoleNotAllowed, ((string, object?)[])null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Arguments_are_copied_and_read_only()
    {
        var arguments = new Dictionary<string, object?> { ["Plan"] = "gold" };

        var refusal = new RefusalException("subscription.plan-closed", RefusalKind.Conflict, "The gold plan is closed.", arguments);
        arguments["Plan"] = "silver";
        arguments["Extra"] = 1;

        refusal.Arguments.Should().HaveCount(1, "the refusal took a copy, so later changes to the caller's dictionary do not reach it");
        refusal.Arguments["plan"].Should().Be("gold", "names are matched without regard to case, like every other failure's");
        var writable = refusal.Arguments as IDictionary<string, object?>;
        if (writable is not null)
        {
            FluentActions.Invoking(() => writable["Plan"] = "silver").Should().Throw<NotSupportedException>("the arguments cannot be changed through a cast either");
        }

        new RefusalException("x.y", RefusalKind.Invalid, "No arguments.").Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Code_and_kind_are_required()
    {
        FluentActions.Invoking(() => new RefusalException(null!, RefusalKind.Invalid, "m")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new RefusalException(" ", RefusalKind.Invalid, "m")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RefusalException("x.y", (RefusalKind)42, "m")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new RefusalException("x.y", RefusalKind.Invalid, null!)).Should().Throw<ArgumentNullException>();

        var inner = new InvalidOperationException("cause");
        var refusal = new RefusalException("x.y", RefusalKind.NotFound, "There is no such plan.", innerException: inner);

        refusal.Code.Should().Be("x.y");
        refusal.Kind.Should().Be(RefusalKind.NotFound);
        refusal.Message.Should().Be("There is no such plan.");
        refusal.InnerException.Should().BeSameAs(inner);
        refusal.Should().BeAssignableTo<DDDToolkitException>();
    }
}
