using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Aevatar.Application.Grains.UserFeedback.Options;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;

namespace Aevatar.Application.Grains;

[DependsOn(
    typeof(AbpAutoMapperModule))]
public class GodGPTGAgentModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options => { options.AddMaps<GodGPTGAgentModule>(); });
        
        var configuration = context.Services.GetConfiguration();
        Configure<CreditsOptions>(configuration.GetSection("Credits"));
        Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));
        Configure<RolePromptOptions>(configuration.GetSection("RolePrompts"));
        Configure<AnonymousGodGPTOptions>(configuration.GetSection("AnonymousGodGPT"));
        Configure<TwitterAuthOptions>(configuration.GetSection("TwitterAuth"));
        Configure<TwitterRewardOptions>(configuration.GetSection("TwitterReward"));
        Configure<LLMRegionOptions>(configuration.GetSection("LLMRegion"));
        Configure<AwakeningOptions>(configuration.GetSection("Awakening"));
        Configure<UserStatisticsOptions>(configuration.GetSection("UserStatistics"));
        Configure<UserFeedbackOptions>(configuration.GetSection("UserFeedback"));
        Configure<GoogleAuthOptions>(configuration.GetSection("GoogleAuth"));
        Configure<SpeechOptions>(configuration.GetSection("Speech"));
        
        // Register speech services
        context.Services.AddSingleton<ISpeechService, SpeechService>();
        context.Services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // Register HttpClient factory
        context.Services.AddHttpClient();
    }
}