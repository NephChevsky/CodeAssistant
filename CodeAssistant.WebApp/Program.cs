using System.Text.Json;
using System.Text.Json.Serialization;
using CodeAssistant.Lib;
using CodeAssistant.WebApp.Hubs;
using CodeAssistant.WebApp.Services;
using CopilotLib;

namespace CodeAssistant.WebApp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.AddCopilotLib();
            builder.Services.AddCodeAssistantLib();
            builder.Services.AddSingleton<AssistantConnections>();
            builder.Services.AddSignalR(options => options.EnableDetailedErrors = true)
                .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

            WebApplication app = builder.Build();

            // Local-only app without authentication: reject requests made by other sites, including WebSocket upgrades that CORS does not cover.
            app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                string? origin = context.Request.Headers.Origin;
                if (!string.IsNullOrEmpty(origin) && !(Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && uri.IsLoopback))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                await next(context);
            });

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapHub<AssistantHub>("/hubs/assistant");
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}
