using DDDToolkit.Abstractions.Attributes;

// The ids in this folder are Tenancy's, published here so Projects and Inspections can store them without
// referencing any other project of Tenancy. This project declares the same module as the rest of Tenancy (see
// Module.cs), so they are one module.
namespace Examples.Tenancy.Tenants.Contracts.ValueObjects;

/// <summary>
/// A tenant's id, which is also its organization's.
/// </summary>
/// <remarks>
/// The ids are the application's, not the Tenancy package's: the package is generic over them, and the
/// application picks the key type and the prefix. Every one is a struct, because the package's parents hold
/// optional ids, such as a unit's parent, as <c>Nullable&lt;T&gt;</c>.
/// </remarks>
[ModuleContract]
[EntityId<Guid>("TEN")]
public readonly partial record struct TenantId;
