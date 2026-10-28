using DDDToolkit.Abstractions.Access;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// What a row access contribution answers about the caller in the database, for the functions of another package to
/// ask: who the caller is as an id of a kind it keeps, and where the caller holds a key, as places of a kind it keeps.
/// Tenancy's contribution answers both for the application's seat id and unit id, so a resource whose members are
/// seats, and which hangs at a unit, has its functions written with Tenancy's without its rules naming one.
/// </summary>
/// <remarks>
/// It is asked about a context, the one that maps what asks, by the type of the id or of the place, and answers
/// <see langword="null"/> for a type it keeps nothing of in that context. A package that asks finds it among the
/// contributions of the export (<see cref="RowAccessExport.Contributions"/>), and refuses where none answers.
/// </remarks>
public interface ICurrentUserFunctions
{
    /// <summary>
    /// The function that answers who the caller is as an <paramref name="id"/>, without parameters, or
    /// <see langword="null"/> where this keeps no such id in the model of <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The context whose functions ask it.</param>
    /// <param name="id">The type of the id, such as the application's seat id.</param>
    DatabaseFunction? CurrentUserAs(DbContext context, Type id);

    /// <summary>
    /// The function that answers, for a key given as text, the places of type <paramref name="place"/> the caller's
    /// hold of it reaches, or <see langword="null"/> where this keeps no such place in the model of
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The context whose functions ask it.</param>
    /// <param name="place">The type of the place, such as the application's unit id.</param>
    DatabaseFunction? PlacesWhereCurrentUserHolds(DbContext context, Type place);
}
