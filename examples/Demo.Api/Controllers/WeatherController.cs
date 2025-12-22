using Aevatar.Agents.Abstractions;
using Demo.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;

    public WeatherController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 查询指定城市的天气
    /// </summary>
    [HttpGet("{city}")]
    public async Task<IActionResult> GetWeather(string city)
    {
        try
        {
            // 通过 ActorFactory 创建 Actor
            var factory = _serviceProvider.GetRequiredService<IGAgentActorFactory>();
            var agentActor = await factory.CreateGAgentActorAsync<WeatherAgent>(Guid.NewGuid().ToString());
            
            // 获取 Agent 并执行业务逻辑
            var weatherAgent = (WeatherAgent)agentActor.GetAgent();
            
            // 查询天气
            var weather = await weatherAgent.GetWeatherAsync(city);
            
            // 获取查询统计
            var queryCount = weatherAgent.GetQueryCount();

            // 清理
            await agentActor.DeactivateAsync();

            return Ok(new
            {
                City = city,
                Weather = weather,
                AgentId = agentActor.Id,
                QueryCount = queryCount
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 获取Agent统计信息
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        try
        {
            return Ok(new
            {
                Message = "Weather Agent API",
                Version = "1.0.0",
                Description = "查询城市天气信息"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}
