using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using VsBridge;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);
builder.Services
    .AddSingleton<StaDispatcher>()
    .AddSingleton<VsConnection>()
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
