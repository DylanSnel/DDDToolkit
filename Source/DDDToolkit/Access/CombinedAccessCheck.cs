using DDDToolkit.Exceptions;

namespace DDDToolkit.Access;

/// <summary>
/// Decides a requirement that combines cases, <see cref="AccessRequirement.AllOf"/> or <see cref="AccessRequirement.AnyOf"/>,
/// by asking each case of the check that decides it alone. <see cref="AccessChecks{TRequests}"/> makes one for each
/// request that declares a combination, with the checks of all its cases found first, and the request in hand keeps it,
/// so asking it again asks the same cases of the same checks.
/// </summary>
/// <remarks>
/// It reads nothing itself, and what a case's check keeps for the handler (<see cref="Checked{T}"/>) it holds back until
/// the combination is decided: kept once the caller was let through, of <see cref="AccessRequirement.All"/> every case's
/// and of <see cref="AccessRequirement.Any"/> that of the case that let it through, and dropped when the caller is
/// refused. So a handler of a request let through finds what each of the cases it passed by kept, and a refused request
/// has nothing kept for it, whichever of its cases had passed before the one that refused.
/// </remarks>
/// <param name="checks">The check of every case the combination holds, its nested combinations' included.</param>
internal sealed class CombinedAccessCheck(IReadOnlyDictionary<AccessRequirement, IAccessCheck> checks) : IAccessCheck
{
    /// <inheritdoc />
    public bool Decides(AccessRequirement requirement) => requirement is AccessRequirement.Combined;

    /// <inheritdoc />
    /// <exception cref="RefusalException">
    /// The first failing case's own refusal, of all of them; or <see cref="ToolkitRefusals.Refused"/>, with each case's reason,
    /// where none of several let the caller through.
    /// </exception>
    public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(request);

        return RequireWholeAsync(requirement, request, cancellationToken);
    }

    /// <summary>
    /// The whole combination, with what its cases keep held back until it let the caller through. Asynchronous, so the
    /// holding back stays in here and is never the flow's that asked.
    /// </summary>
    private async ValueTask RequireWholeAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        var held = RequestInHand.HoldBack();
        try
        {
            await Require(requirement, request, cancellationToken).ConfigureAwait(false);
            held.Commit();
        }
        finally
        {
            RequestInHand.EndHoldingBack(held);
        }
    }

    private async ValueTask Require(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
    {
        switch (requirement)
        {
            case AccessRequirement.AllOf all:
                // In their order, and the first refusal is the request's: what follows it is not asked, and what the cases
                // before it kept is dropped with the combination around them.
                foreach (var each in all.Cases)
                {
                    await Require(each, request, cancellationToken).ConfigureAwait(false);
                }

                return;

            case AccessRequirement.AnyOf any:
                // The first that lets the caller through ends it, and what follows it is not asked. Only a refusal moves
                // on to the next case: anything else a check throws, a lost race or a failure to read, is the answer.
                // Each case keeps on its own: what a case that refused kept, the passing half of an All, is dropped.
                var reasons = new List<RefusalReason>(any.Cases.Count);
                foreach (var each in any.Cases)
                {
                    var held = RequestInHand.HoldBack();
                    try
                    {
                        await Require(each, request, cancellationToken).ConfigureAwait(false);
                        held.Commit();
                        return;
                    }
                    catch (RefusalException refused)
                    {
                        reasons.Add(RefusalReason.From(refused));
                    }
                    finally
                    {
                        RequestInHand.EndHoldingBack(held);
                    }
                }

                throw ToolkitRefusals.NoneMet(reasons);

            default:
                await checks[requirement].RequireAsync(requirement, request, cancellationToken).ConfigureAwait(false);
                return;
        }
    }
}
