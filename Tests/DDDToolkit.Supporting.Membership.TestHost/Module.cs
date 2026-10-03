using DDDToolkit.Abstractions.Attributes;

// The application's one module, with two kinds of resource in it. The package declares no module of its own:
// once an application references it, it is part of whichever module declares the member classes.
[assembly: Module("Filing")]
