global using DDDToolkit.Supporting.Tenancy.Access;
global using DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;
global using DDDToolkit.Supporting.Tenancy.TestHost;
global using DDDToolkit.Supporting.Tenancy.TestHost.Contracts;
global using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
global using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
global using DDDToolkit.Supporting.Tenancy.Tests.Support;
global using FluentAssertions;
global using Microsoft.EntityFrameworkCore;
global using Xunit;

// The caller closed over the host's ids, once, as an application does. The use cases are HostTenancy, which the
// toolkit's generator writes into the test host that declares the classes, under the name its module gives it.
global using HostCaller = DDDToolkit.Supporting.Tenancy.Access.TenancyCaller<
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.TenantId,
    DDDToolkit.Supporting.Tenancy.TestHost.Contracts.SeatId>;
