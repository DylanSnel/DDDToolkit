using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The SQL that asks one of Tenancy's database functions (<see cref="TenancyFunctionNames"/>) through Entity
/// Framework, so whatever the context does to its commands, such as saying who is calling, is done to these too.
/// The function answers under column names of its own, so the SQL selects those, quoted as the provider quotes.
/// </summary>
internal static class TenancyFunctionSql
{
    /// <summary>
    /// The function that answers the calling seat, or nothing where the connection carries none: no parameters.
    /// A package that writes Tenancy's functions for a database that keeps the rights writes it under this name,
    /// in the schema of the context that maps Tenancy's tables.
    /// </summary>
    internal const string CallerSeat = "caller_seat";

    /// <summary>
    /// <c>SELECT f."SeatId", f."RoleId" FROM "tenancy"."tenant_administrators"({0}, {1}) AS f</c>: every row the
    /// function answers, with a placeholder for each of its <paramref name="parameters"/>.
    /// </summary>
    /// <param name="context">The context that will run it.</param>
    /// <param name="schema">The schema the function is in, or <see langword="null"/> for the model's default schema.</param>
    /// <param name="function">The function's name.</param>
    /// <param name="parameters">How many parameters it takes.</param>
    /// <param name="columns">The columns it answers.</param>
    public static string Select(DbContext context, string? schema, string function, int parameters, params string[] columns)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        return "SELECT " + string.Join(", ", columns.Select(column => "f." + sql.DelimitIdentifier(column)))
            + " FROM " + sql.DelimitIdentifier(function, schema ?? context.Model.GetDefaultSchema())
            + "(" + string.Join(", ", Enumerable.Range(0, parameters).Select(index => "{" + index + "}")) + ") AS f";
    }

    /// <summary>
    /// <c>SELECT f.v AS "TenantId" FROM "tenancy"."tenants_to_sweep"() AS f(v)</c>: every value of a function that
    /// answers one value a row rather than named columns, under the name <paramref name="column"/> for Entity
    /// Framework to read it by, with a placeholder for each of its <paramref name="parameters"/>.
    /// </summary>
    /// <param name="context">The context that will run it.</param>
    /// <param name="schema">The schema the function is in, or <see langword="null"/> for the model's default schema.</param>
    /// <param name="function">The function's name.</param>
    /// <param name="parameters">How many parameters it takes.</param>
    /// <param name="column">The name the values are read by.</param>
    public static string SelectValues(DbContext context, string? schema, string function, int parameters, string column)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        return "SELECT f.v AS " + sql.DelimitIdentifier(column)
            + " FROM " + sql.DelimitIdentifier(function, schema ?? context.Model.GetDefaultSchema())
            + "(" + string.Join(", ", Enumerable.Range(0, parameters).Select(index => "{" + index + "}")) + ") AS f(v)";
    }

    /// <summary>
    /// <c>SELECT "tenancy"."caller_seat"() IS NOT NULL AS "Value"</c>: whether a function without parameters
    /// answers anything, under the name Entity Framework reads a single value by.
    /// </summary>
    /// <param name="context">The context that will run it.</param>
    /// <param name="schema">The schema the function is in, or <see langword="null"/> for the model's default schema.</param>
    /// <param name="function">The function's name.</param>
    public static string Answers(DbContext context, string? schema, string function)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        return "SELECT " + sql.DelimitIdentifier(function, schema ?? context.Model.GetDefaultSchema()) + "() IS NOT NULL AS " + sql.DelimitIdentifier("Value");
    }

    /// <summary>
    /// <paramref name="id"/> as the database stores it, through the converter the context registers for its type:
    /// an id travels to a function as the value of its column, a <see cref="Guid"/> or a <see cref="long"/>.
    /// </summary>
    public static object Stored<TId>(DbContext context, TId id)
        where TId : struct
        => context.GetService<ITypeMappingSource>().FindMapping(typeof(TId), context.Model)?.Converter is { } converter
            ? converter.ConvertToProvider(id) ?? id
            : id;
}
