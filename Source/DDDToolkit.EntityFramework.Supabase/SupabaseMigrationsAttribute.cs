namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// Marks a context whose migrations Supabase applies, so the build exports them, writes the design-time factory
/// <c>dotnet ef</c> and the export make it with, and lists it for the host's start-up check.
/// <code>
/// [SupabaseMigrations]
/// public sealed class OrderingContext(DbContextOptions&lt;OrderingContext&gt; options) : DbContext(options) { ... }
///
/// // in the host, once: every marked context the host references, for the check that Supabase applied them all,
/// // written into the namespace named after the host's assembly (using Shop.Host; in a top-level Program.cs)
/// builder.Services.AddSupabaseMigrations();
/// </code>
/// <para>
/// Put it on the context in the module. Beside it the build writes <c>OrderingContextDesignTimeFactory</c>, an
/// <c>IDesignTimeDbContextFactory&lt;OrderingContext&gt;</c> that builds the context on Npgsql with a connection string
/// that points nowhere, since neither <c>dotnet ef</c> nor the export opens a connection, and with
/// <c>UseDDDToolkitDesignTime()</c> where the project references DDDToolkit.EntityFramework, which keeps the migration
/// history in the context's default schema, where the host's <c>UseDDDToolkit</c> reads it. <c>dotnet ef</c> finds it
/// in the context's assembly, as it finds a factory written by hand. A factory of the module's own for the context, in
/// the same project, wins: the build then writes none and uses that one, which, for an export or a check in another
/// project, is public with a public parameterless constructor. A factory of the host's own for the context wins in the
/// host: <c>dotnet ef</c> takes it first with the host as its startup project, and so do the export and the check
/// there. The context needs a constructor that takes its options alone, and the project
/// Npgsql.EntityFrameworkCore.PostgreSQL; the build reports DDD00031 otherwise.
/// </para>
/// <para>
/// The project that turns the export on, with <c>&lt;SupabaseMigrationsExport&gt;Write&lt;/SupabaseMigrationsExport&gt;</c>
/// (the host of a modular monolith, the presentation layer of a service), finds every marked context it references
/// at compile time and exports them after it builds; every application that is not a test project gets
/// <c>services.AddSupabaseMigrations()</c>, which registers them all. Nothing lists the modules by hand, and nothing
/// is found by reflection at run time. The files are named after the module the assembly declares with
/// <c>[assembly: Module("...")]</c> or <c>DDD_Module</c>.
/// </para>
/// <para>
/// It may go on a design-time factory instead, as it did before it could go on a context: for a factory in another
/// project than its context, one that says where the migrations are, say. The factory has to be a public,
/// non-abstract class with a public parameterless constructor that implements
/// <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>, and calls <c>UseDDDToolkitDesignTime()</c> where the host wires
/// the context with <c>UseDDDToolkit</c>, which DDD00071 reports a factory without. A marked factory wins over the
/// factory of its context, marked too or not.
/// </para>
/// <para>
/// The marker is explicit on purpose. Not every context with migrations belongs to Supabase, and a
/// context on SQL Server exported as Postgres SQL would be worse than none.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SupabaseMigrationsAttribute : Attribute;
