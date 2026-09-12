using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ledger-sync-server-gateway"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            // Traces the outgoing call YARP makes to whichever replica it
            // picked. Combined with .NET's automatic W3C traceparent
            // propagation over HttpClient (no extra wiring needed for
            // that part), this is what turns "gateway request" and
            // "replica request" into one connected trace in Jaeger,
            // instead of two traces that happen to be about the same
            // call.
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    });

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

app.UseSerilogRequestLogging();

app.MapReverseProxy();

app.Run();
