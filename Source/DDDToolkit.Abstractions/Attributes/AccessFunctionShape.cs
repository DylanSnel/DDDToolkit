namespace DDDToolkit.Abstractions.Attributes;

/// <summary>What an access function answers: about one row, or with the set of rows it allows.</summary>
public enum AccessFunctionShape
{
    /// <summary>
    /// Whether the function's question is true of one row, which the function is given by its key:
    /// <c>projects.is_member(uuid) RETURNS boolean</c>, called from a policy with the row's key.
    /// </summary>
    Row,

    /// <summary>
    /// The keys of every row the function's question is true of: <c>projects.project_ids_where_i_am_a_member()
    /// RETURNS SETOF uuid</c>, which a policy asks once per statement, as
    /// <c>"Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_am_a_member()))</c>. The aggregate's key is a
    /// single column.
    /// </summary>
    Set,
}
