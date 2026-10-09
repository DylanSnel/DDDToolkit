using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DDDToolkit.EntityFramework.EventLog;

/// <summary>
/// Fills the columns a module added to its event log, with the <c>configure</c> argument of
/// <c>AddEventLog</c>, for every kept event before its row is saved: the branch a sale was made at, say.
/// <code>
/// public sealed class BranchOfTheEvent(IBranchAccessor branch) : IEventLogFields
/// {
///     public void Fill(IDomainEvent domainEvent, EntityEntry&lt;EventLogEntry&gt; entry)
///     {
///         if (entry.Metadata.FindProperty("Branch") is not null)
///         {
///             entry.Property("Branch").CurrentValue = branch.Current;
///         }
///     }
/// }
///
/// services.AddSingleton&lt;IEventLogFields, BranchOfTheEvent&gt;();
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a singleton that reads what it fills in each time it is asked</b>, from what follows the flow of
/// work, such as an ambient caller or the current request, or from the event itself. It keeps nothing between
/// two events and takes no scoped service. The interceptor takes every registered one from the provider its
/// context's options were built with, once per save, and with a context pool that provider is the
/// application's root one, where a scoped service cannot be resolved.
/// </para>
/// <para>
/// Every registered implementation is asked, in registration order, for every kept event of every context, so
/// one that fills a column only some logs have looks for the property first. It runs inside
/// <c>SaveChanges</c>: it fills the row from what it already knows and does not query. Throwing aborts the
/// save. Who acted is not its to fill: that comes from <c>IActedByAccessor</c>.
/// </para>
/// </remarks>
public interface IEventLogFields
{
    /// <summary>Fills <paramref name="entry"/>, the row being added for <paramref name="domainEvent"/>.</summary>
    /// <param name="domainEvent">The event being kept.</param>
    /// <param name="entry">Its row, tracked as added on the saving context; a column of the module's own is set through it.</param>
    void Fill(IDomainEvent domainEvent, EntityEntry<EventLogEntry> entry);
}
