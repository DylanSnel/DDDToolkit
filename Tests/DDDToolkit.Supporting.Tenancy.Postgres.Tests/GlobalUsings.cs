global using DDDToolkit.EntityFramework.Tests.Infrastructure;
global using DDDToolkit.Supporting.Tenancy.Access;
global using DDDToolkit.Supporting.Tenancy.Managers;
global using DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;
global using DDDToolkit.Supporting.Tenancy.Catalogue;
global using DDDToolkit.Supporting.Tenancy.TestHost;
global using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
global using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
global using FluentAssertions;
global using Xunit;

// The caller closed over the host's ids, once, as an application does. The managers are the classes the toolkit's
// generator writes into the test host that declares the classes: SeatManager and the others, and HostRoleManager, under
// the name the host gives it.
global using HostCaller = DDDToolkit.Supporting.Tenancy.Access.TenancyCaller<
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId>;
