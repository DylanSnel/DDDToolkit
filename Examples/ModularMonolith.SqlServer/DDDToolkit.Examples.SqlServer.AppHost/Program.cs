// The modular monolith on SQL Server: one SQL Server in Docker, one database in it, and the host that
// runs all five modules against it, each module in a schema of its own. Run this project, not the host;
// the Aspire dashboard shows the host's logs and traces, and the host's own URL serves the shop.

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sqlserver");
var shop = sqlServer.AddDatabase("shop");

builder.AddProject<Projects.DDDToolkit_Examples_SqlServer_Host>("monolith")
    .WithReference(shop, connectionName: "SqlServer")
    .WaitFor(shop)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
