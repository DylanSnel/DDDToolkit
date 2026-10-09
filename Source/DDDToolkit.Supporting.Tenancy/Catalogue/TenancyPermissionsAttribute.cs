namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Marks a module's list of permission keys, the one place the module states them. Nothing else of the
/// module names them again: neither its registration nor the program that exports the database's policies.
/// <code>
/// public static class OrderingKeys
/// {
///     [TenancyPermissions]
///     public static IReadOnlyList&lt;Permission&gt; Permissions { get; } =
///     [
///         new("orders.view", "Ordering", "See the orders"),
///     ];
/// }
/// </code>
/// <para>
/// Tenancy's generator collects every marked list of the modules a project references into that project, in
/// <c>TenancyPermissionsOfModules</c>, in the namespace named after the project's assembly: <c>All</c>, every
/// module's keys, and <c>services.AddTenancyPermissionsOfModules()</c>, which adds them to the catalogue. It writes
/// them into every application, the program the modules are composed in, such as the host, which sees all of them,
/// and into every library that declares no module, with <c>DDD_Module</c> or <c>[assembly: Module]</c>, and into no
/// module's own libraries. The host registers the keys with that one call, and an export builds the same catalogue with
/// <c>TenancyCatalogue.Build(application, TenancyPermissionsOfModules.All)</c>, so a module that is added changes
/// neither. Both compile while no module marks a list yet: <c>All</c> is then empty.
/// </para>
/// <para>
/// The list is a static property or field, readable, declared in a class that is not generic (nor nested in one),
/// whose type is a sequence of <see cref="Permission"/>: <c>IReadOnlyList&lt;Permission&gt;</c>,
/// <c>IEnumerable&lt;Permission&gt;</c> or an array. In a library it is public, in public types, because the
/// project that composes the modules is another one, whether the library declares a module or not; only an
/// application may keep a list of its own internal. One the generator cannot read is DDD00063 where it is
/// declared. A module that marks its list adds it with
/// <see cref="TenancyServiceCollectionExtensions.AddTenancyPermissions"/> no more: the catalogue refuses a key
/// that is declared twice.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TenancyPermissionsAttribute : Attribute;
