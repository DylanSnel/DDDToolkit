using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Turns what the database refuses in a save into the refusal a caller can be answered with, where the database
/// keeps a rule the application could not, or did not, ask about first.
/// <list type="bullet">
/// <item>
/// <b>A unique index that says what it refuses with</b>
/// (<see cref="IndexBuilderRefusalExtensions.RefusesAs(Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder, string, string, RefusalKind)"/>):
/// a save that breaks it throws that <see cref="RefusalException"/>, filled from the row that broke it. Two
/// commands that both passed the check in C# and saved at the same moment get the answer the check gives.
/// </item>
/// <item>
/// <b>A row a policy denies.</b> When row level security refuses the row an insert or an update would write,
/// the save throws <see cref="ToolkitRefusals.Refused"/>, a <see cref="RefusalKind.NotPermitted"/>, and a
/// warning is logged through the context's logger factory: the application allowed what the policies do not,
/// which somebody should look at, while the caller needs no more than "you may not".
/// </item>
/// </list>
/// The failure the database gave is the refusal's inner exception. Anything else goes on as it was: an index
/// that declares nothing, a missing privilege, a foreign key, a check constraint.
/// <para>
/// An update or a delete a policy hides the row from fails differently: the statement finds no row, exactly as
/// when somebody else changed it first. <see cref="AggregateVersionInterceptor"/> tells the two apart, and
/// answers the denial with the same refusal and the same warning.
/// </para>
/// <para>
/// The provider's exception is read by the names of its own type, so this needs no provider package; see
/// <see cref="DatabaseRefusal.From"/> for what is read on Npgsql, SQLite and SQL Server. Register it after
/// <see cref="AggregateVersionInterceptor"/>; <c>UseDDDToolkit</c> does this for you.
/// </para>
/// </summary>
public sealed class DatabaseRefusalInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    /// <remarks>Throwing from here replaces the outgoing exception, so callers can <c>catch (RefusalException)</c> directly.</remarks>
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context && DatabaseRefusals.Translate(context, eventData.Exception) is { } refusal)
        {
            throw refusal;
        }
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }
}
