using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Garnet (Redis-compatible cache) — ephemeral provider response cache
var cache = builder.AddGarnet("garnet");

// MongoDB — durable storage for saved locations, provider config, audit logs
var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume()
    .WithMongoExpress();
var mongoDb = mongo.AddDatabase("veder");

var api = builder.AddProject<Projects.WebApi>("api")
    .WithReference(cache)
    .WithReference(mongoDb);

var webapp = builder.AddProject<Projects.WebApp>("webapp")
    .WithReference(api);

builder.Build().Run();