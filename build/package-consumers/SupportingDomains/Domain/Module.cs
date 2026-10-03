using DDDToolkit.Abstractions.Attributes;

// The module the domain and the infrastructure project are both part of, so the generators write the
// registrations and the converters into the infrastructure project over the classes declared here.
[assembly: Module("Press")]
