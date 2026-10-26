namespace Examples.Tenancy.Inspections.Application.Recording;

/// <summary>
/// Where Inspections' commands add and save inspections: one unit of work per request. The application declares
/// it; the infrastructure project implements it over the module's context and registers it, scoped.
/// </summary>
/// <remarks>
/// The write side only. An inspection is recorded once and never changed, so there is nothing to load: a command
/// makes the inspection, adds it and saves. Nothing here lists inspections, which is
/// <see cref="IInspectionReads"/>'s. The two never share storage: a request's commands run one at a time on this
/// unit of work, while its queries may run side by side, each on a context of its own.
/// <para>
/// The inspections keep to the current caller's tenant, as the storage's save check does: an inspection of another
/// tenant is not saved.
/// </para>
/// <para>
/// Public, because the infrastructure project implements it and a handler's constructor names it. No route
/// names it: a route only sends a request.
/// </para>
/// </remarks>
public interface IInspectionStore
{
    /// <summary>Adds a new inspection to the unit of work.</summary>
    /// <param name="inspection">The inspection, just recorded.</param>
    void Add(Inspection inspection);

    /// <summary>
    /// Saves every inspection added in this unit of work, with the domain events they raised, in one transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    Task SaveAsync(CancellationToken cancellationToken);
}
