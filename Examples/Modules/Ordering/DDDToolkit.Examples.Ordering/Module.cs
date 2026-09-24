using DDDToolkit.Abstractions.Attributes;

// One assembly, one module. Everything this project declares belongs to Ordering and is Ordering's own
// business unless it is marked [ModuleContract] or is an integration event, both of which live next
// door in DDDToolkit.Examples.Ordering.Contracts.
//
// Nothing happens until a second assembly says it is a module too. Shipping does, so from here on the
// boundary analyzer reports anything Shipping names that Ordering did not publish (DDD00022) and any
// entity of one module stored by the other (DDD00023). See docs/modules.md.
[assembly: Module("Ordering")]
