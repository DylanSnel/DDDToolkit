using DDDToolkit.Abstractions.Attributes;

// Two projects, one module: this one and DDDToolkit.HotChocolate.Tests.Harbor.Api declare the same module name, so
// to the generators they are one module, and the API project binds the ids declared here.
[assembly: Module("Harbor")]
