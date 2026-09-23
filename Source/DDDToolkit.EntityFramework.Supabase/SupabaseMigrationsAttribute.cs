namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// Marks a design-time factory whose context's migrations Supabase applies, so the build exports them.
/// <code>
/// [SupabaseMigrations]
/// public sealed class OrderingContextFactory : IDesignTimeDbContextFactory&lt;OrderingContext&gt;
/// {
///     public OrderingContext CreateDbContext(string[] args) => ...; // UseNpgsql("Host=unused")
/// }
/// </code>
/// <para>
/// Put it on the factory in the module. The project that turns the export on, with
/// <c>&lt;SupabaseMigrationsExport&gt;Write&lt;/SupabaseMigrationsExport&gt;</c> (the host of a modular
/// monolith, the presentation layer of a service), finds every marked factory it references at compile
/// time and exports them after it builds. Nothing lists the modules by hand, and nothing is found by
/// reflection at run time.
/// </para>
/// <para>
/// The factory has to be a public, non-abstract class with a public parameterless constructor that
/// implements <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>; the build reports DDD00031 otherwise.
/// The files are named after the module the assembly declares with <c>[assembly: Module("...")]</c>.
/// </para>
/// <para>
/// The marker is explicit on purpose. Not every context with migrations belongs to Supabase, and a
/// context on SQL Server exported as Postgres SQL would be worse than none.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SupabaseMigrationsAttribute : Attribute;
