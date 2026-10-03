using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// A table of an aggregate's entities, and the way from it up to the aggregate's own table, one link per
/// table in between: what <see cref="RowAccessModel.EntityTablesOf"/> answers with.
/// </summary>
/// <param name="Entity">The entity type whose rows the table holds.</param>
/// <param name="Table">The table, with its schema: <c>"desk"."TicketComment"</c>.</param>
/// <param name="Links">The steps up, the first to the table of the one it belongs to, the last to the root's.</param>
public sealed record EntityTableChain(IEntityType Entity, string Table, IReadOnlyList<EntityTableLink> Links);

/// <summary>One step up from an entity's table: the table above, and the columns that tie the two, in pairs.</summary>
/// <param name="Parent">The table above, with its schema: <c>"desk"."Tickets"</c>.</param>
/// <param name="ParentColumns">The key columns of the table above, quoted: <c>"Id"</c>.</param>
/// <param name="Columns">The columns of the table below that hold them, in the same order, quoted: <c>"TicketId"</c>.</param>
public sealed record EntityTableLink(string Parent, IReadOnlyList<string> ParentColumns, IReadOnlyList<string> Columns);
