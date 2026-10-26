// This module's ids: every feature's routes read them. The commands and queries a feature's routes send are named
// in that feature's own file, with a using of the application project's feature of the same name. The
// infrastructure project's namespace is not among these: only the entry names it, in its own file.
global using Examples.Tenancy.Tenants.Contracts.ValueObjects;

// The package's shapes and statuses: the bodies and the answers name them. The package's records the routes and the
// GraphQL types describe, TenancyUseCases.KeyReach and the rest, need nothing here: TenancyUseCases is a class of the
// domain project, which the toolkit's generator wrote there, and HotChocolate's generator reads it as any type.
global using DDDToolkit.Supporting.Tenancy;
