using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// The access check the request being handled passed on its way to its handler, kept with the flow of work that
/// handles it, so that what happens later in that flow can ask it again: does the caller still meet what the request
/// requires, now?
/// </summary>
/// <remarks>
/// <see cref="AccessChecks{TRequests}.RequireAsync"/> keeps it, for every request it lets through, and nothing else
/// has to. It is kept in the flow of the method that awaits the checks, and from there in that of the handler the
/// method calls next, so that method is an <see langword="async"/> one: the generated behavior is, and so is a
/// pipeline behavior, an endpoint filter or a dispatcher of your own written with <see langword="async"/> and
/// <see langword="await"/>. A method without <see langword="async"/> that hands on the checks' task keeps it in the
/// flow of whatever called that method instead: a request sent that way from a handler leaves its check in that
/// handler's flow, in the place of the check of the handler's own request.
/// <para>
/// The toolkit asks it when the database refuses a save the application allowed. Under row level security that
/// happens in two ways. The caller's rights changed between the check and the save, a key taken from a role, a
/// grant revoked, so the policies refuse what the check let through a moment earlier, and the check and the
/// policies agreed, each when it was asked. Or the check and the policies hold a rule differently, so the policies
/// refuse what the check still allows: something a developer should look at. Asking the check again tells the two
/// apart, at the cost of one more check, paid only by a save the database refused.
/// </para>
/// <para>
/// It follows the flow the way an <see cref="AsyncLocal{T}"/> does: into the handler and everything it awaits,
/// not back out to what sent the request. Each request a handler sends in turn passes its own check, and has its
/// own while it is handled. A request anyone may send (<see cref="AccessRequirement.AllowAnonymous"/>) passed no
/// check, and neither did a handler called directly, nor work outside any request: <see cref="Current"/> is then
/// <see langword="null"/>, and there is nothing to ask again.
/// </para>
/// <para>
/// What is asked again is the check the request declared. A rule a handler checks itself, past what its
/// request declared, is not part of it, and neither is the gate of a package's use case: a requirement that
/// leaves the checking to the use case asks nothing of the caller, and lets the caller through each time it is
/// asked.
/// </para>
/// </remarks>
public sealed class PassedAccessCheck
{
    private static readonly AsyncLocal<PassedAccessCheck?> Ambient = new();

    private static readonly AsyncLocal<bool> AskingAgain = new();

    private readonly IAccessCheck _check;

    private volatile bool _passed;

    private PassedAccessCheck(IAccessCheck check, AccessRequirement requirement, IRequireAccess request)
    {
        _check = check;
        Requirement = requirement;
        Request = request;
    }

    /// <summary>
    /// The access check the request this flow of work is handling passed, or <see langword="null"/> where no
    /// check let a request through: outside any request, for a request that requires nothing, and for one whose
    /// check refused or is still being asked.
    /// </summary>
    public static PassedAccessCheck? Current => Ambient.Value is { _passed: true } passed ? passed : null;

    /// <summary>Whether this flow is asking a check again, for <see cref="Checked{T}"/>, which then keeps nothing.</summary>
    internal static bool IsAskingAgain => AskingAgain.Value;

    /// <summary>The very request that passed.</summary>
    public IRequireAccess Request { get; }

    /// <summary>What the request declared it requires, as it was read from it when it passed.</summary>
    public AccessRequirement Requirement { get; }

    /// <summary>
    /// Asks the check again, now, as the caller of this flow, about the same requirement of the same request:
    /// <see langword="true"/> when it lets the caller through still, <see langword="false"/> when it refuses
    /// now, with a <see cref="RefusalException"/>.
    /// </summary>
    /// <remarks>
    /// The check reads what it read the first time, as it is now. Asked again, it keeps nothing for the handler
    /// (<see cref="Checked{T}"/>): the handler took what it acts on when it ran, and nothing of this second
    /// asking stays behind for a later call to find. Anything else the check throws comes out of here as it is,
    /// since it answers neither way: a failure of its reading, and a <see cref="ConcurrencyConflictException"/>
    /// too. A conflict says that what the request is about moved on from the version it named, the handler's own
    /// save among the ways that happens, and not whether the caller may: a check that holds a request to a version
    /// asks it after the caller's rights, as Membership's does.
    /// </remarks>
    /// <param name="cancellationToken">Stops the reading the check does.</param>
    public async ValueTask<bool> StillPassesAsync(CancellationToken cancellationToken = default)
    {
        AskingAgain.Value = true;
        try
        {
            await _check.RequireAsync(Requirement, Request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RefusalException)
        {
            return false;
        }
    }

    /// <summary>
    /// The request's type and that of its requirement, as their authors write them, <c>CloseInvoice
    /// (BillingAccess.OnInvoice)</c>: what a log line names the check by, so that whoever reads it sees what was
    /// asked again, a requirement that asks nothing of the caller among them. Nothing of their values, which may
    /// say more than a log should.
    /// </summary>
    public override string ToString() => $"{WrittenTypeNames.Of(Request.GetType())} ({WrittenTypeNames.Of(Requirement.GetType())})";

    /// <summary>
    /// Asks <paramref name="check"/> about <paramref name="request"/> and keeps what it passed for the flow that
    /// asked: set here, before anything is awaited, so that it is the flow of the caller of
    /// <see cref="AccessChecks{TRequests}.RequireAsync"/> it is set in, and from there the handler's. It counts as
    /// passed once the check returned.
    /// </summary>
    internal static ValueTask Ask(IAccessCheck check, AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        var passing = new PassedAccessCheck(check, requirement, request);
        Ambient.Value = passing;

        var asked = check.RequireAsync(requirement, request, cancellationToken);
        if (!asked.IsCompletedSuccessfully)
        {
            return PassedAsync(asked, passing);
        }

        asked.GetAwaiter().GetResult();
        passing._passed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Says that the flow of the caller passed no check: a request that requires nothing, sent from the handler of
    /// one that passed, is not that request, and a refusal of what it saves is not that request's to explain.
    /// </summary>
    internal static void None() => Ambient.Value = null;

    private static async ValueTask PassedAsync(ValueTask asked, PassedAccessCheck passing)
    {
        await asked.ConfigureAwait(false);
        passing._passed = true;
    }
}
