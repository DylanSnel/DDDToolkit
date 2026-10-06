using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Marks the application's part of the catalogue, the one it hands Tenancy as <c>TenancyOptions.Catalogue</c>, so
/// the program that exports the database's policies writes them from the same catalogue:
/// <code>
/// public static class ShopCatalogue
/// {
///     [TenancyCatalogue]
///     public static ApplicationCatalogue Application { get; } = new(
///         Packs: [new RolePack("viewer", "Viewer", "Looks at the orders", [ShopKeys.OrdersView])]);
/// }
/// </code>
/// <para>
/// Tenancy on Postgres writes its functions, policies and triggers into every application that references it,
/// from the catalogue the application runs with: its marks decide which roles manage access, and so which grants
/// the policies contain, and its packs which roles a settings manager may add. The export runs before the
/// application starts, so it builds that catalogue itself, as the host's registration does: this part, with the
/// keys of every module the exporting project references, every list marked <see cref="TenancyPermissionsAttribute"/>.
/// An application that marks nothing gets what an application that leaves <c>TenancyOptions.Catalogue</c> unset gets:
/// <c>new ApplicationCatalogue()</c>, Tenancy's keys and the modules' and the default administrators' pack.
/// </para>
/// <para>
/// The member is a static property or field of type <see cref="ApplicationCatalogue"/>, readable from the project
/// that runs the export, which references the project that declares it: in a library it is public, in public
/// types, which that library's own build checks (DDD00070), since the exporting project does not see it otherwise.
/// One member is marked in all of them: the export cannot choose between two, and reports them, as it does one of
/// another type (DDD00066).
/// </para>
/// </summary>
[ApplicationMark]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TenancyCatalogueAttribute : Attribute;
