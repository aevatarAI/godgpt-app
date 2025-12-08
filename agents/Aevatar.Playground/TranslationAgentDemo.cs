using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Translation;

namespace Aevatar.Playground;

public class TranslationAgentDemo : AgentDemoBase
{
    public override async Task RunAsync(IGAgentActorFactory factory, ILogger<Program> logger)
    {
        var actor = await factory.CreateGAgentActorAsync<TranslationAgent>();
        var agent = (TranslationAgent)actor.GetAgent();
        await agent.InitializeAsync(AevatarAgentsConstants.DefaultProviderName);

        while (true)
        {
            var input = Console.ReadLine();
            if (input.Trim().ToLower() == "exit")
            {
                break;
            }

            await actor.PublishEventAsync(new TranslateFileEvent
            {
                FilePath = "/Users/zhaoyiqi/Code/aevatar-agent-framework/agents/Aevatar.Playground/translate_test.md",
                TargetLanguage = input
            });
        }
    }
}