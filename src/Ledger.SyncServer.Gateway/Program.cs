var builder = WebApplication.CreateBuilder(args);

// YARP itself carries no logic of this app's own — routing and cluster
// membership live entirely in configuration (appsettings.json / compose
// environment), so there's nothing here that needs a project reference
// to Domain or Infrastructure. Round-robins across whatever destinations
// are configured, and actively polls each one's own /health/ready (see
// appsettings.json's Clusters:api-cluster:HealthCheck:Active section) —
// the same endpoint the previous checkpoint built for exactly this
// purpose. Active health checking is entirely configuration-driven; no
// extra builder call is needed to turn it on.
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();

app.Run();
