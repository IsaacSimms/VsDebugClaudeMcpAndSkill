using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using VsBridge;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);
builder.Services
    .AddSingleton<StaDispatcher>()                  // STA thread for COM calls
    .AddSingleton<VsConnection>()                   // DTE2 connection manager
    .AddMcpServer()
    .WithStdioServerTransport()                     // JSON-RPC over stdin/stdout
    .WithToolsFromAssembly();                       // Auto-register [McpServerTool] methods

await builder.Build().RunAsync();
