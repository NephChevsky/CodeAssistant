using CodeAssistant.Lib.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace CodeAssistant.Lib
{
    public static class CodeAssistantServiceCollectionExtensions
    {
        /// <summary>Registers <see cref="IFeaturePlanner"/>. An <c>LLMLib.ILLMService</c> must be registered separately.</summary>
        public static IServiceCollection AddCodeAssistantLib(this IServiceCollection services, Action<PlannerOptions>? configure = null)
        {
            PlannerOptions options = new();
            configure?.Invoke(options);
            services.AddSingleton(options);
            services.AddSingleton<IFeaturePlanner, FeaturePlanner>();
            return services;
        }
    }
}
