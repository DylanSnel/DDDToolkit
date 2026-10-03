namespace Examples.Tenancy.Inspections.Infrastructure.Persistence;

/// <summary>
/// The application project's <see cref="IInspectionStore"/>, over the request's context: the unit of work a
/// command adds to and saves.
/// </summary>
/// <remarks>
/// It takes the scope's context, and only commands use it. A request's commands run one at a time, so one context
/// serves them all, and what one added is what <see cref="SaveAsync"/> writes, with the domain events it raised,
/// in one transaction. The save check <c>UseTenancy</c> puts on the context keeps every write to the caller's
/// tenant. Nothing here reads for a query: those take a context of their own, through
/// <see cref="EfInspectionReads"/>.
/// <para>
/// Internal: the module's registration, in this project, is the only code that names it. Everything else asks for
/// the port.
/// </para>
/// </remarks>
/// <param name="db">The module's context, per request.</param>
internal sealed class EfInspectionStore(InspectionsContext db) : IInspectionStore
{
    /// <inheritdoc />
    public void Add(Inspection inspection) => db.Inspections.Add(inspection);

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
