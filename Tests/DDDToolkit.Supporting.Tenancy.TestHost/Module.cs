using DDDToolkit.Abstractions.Attributes;

// The application's tenancy module. The package declares no module of its own: once an application
// references it, it is part of whichever module declares the classes, and that is this one.
[assembly: Module("Tenancy")]
