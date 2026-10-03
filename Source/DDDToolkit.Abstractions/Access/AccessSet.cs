namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// The answer to a set-shaped question only the database can answer, such as the ids of the tickets the
/// caller watches. A row access rule asks whether a value is in it:
/// <code>
/// public static bool Allows(Ticket ticket, Caller caller) =&gt; TicketsIWatch.Ids().Contains(ticket.Id);
/// </code>
/// The generator writes that as <c>"Id" = ANY (ARRAY(SELECT desk.tickets_i_watch()))</c>, which Postgres
/// answers once per statement, before it reads the table, and with the column's index.
/// </summary>
/// <remarks>
/// A set is never made in C#: the question that would answer it, an <c>[AccessSet]</c> method or the
/// <c>Ids</c> of a set-shaped access function, throws <see cref="DatabaseOnlyException"/> when it is called.
/// Ask the database instead, by loading the rows through a context that runs as the caller.
/// </remarks>
/// <typeparam name="T">What the set holds, usually an aggregate's id.</typeparam>
public readonly struct AccessSet<T>
{
    /// <summary>Whether the set holds <paramref name="value"/>, which only the database answers.</summary>
    /// <param name="value">The value to look for, typically a column of the row the rule is about.</param>
    /// <returns>Never: only the database can answer it.</returns>
    /// <exception cref="DatabaseOnlyException">Always.</exception>
    public bool Contains(T value) => throw new DatabaseOnlyException("AccessSet<" + typeof(T).Name + ">.Contains(" + value + ")");
}
