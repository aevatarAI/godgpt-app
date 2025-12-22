using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Demo.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("🌌 Aevatar Agent Framework - Simple Demo");
Console.WriteLine("=========================================\n");

// 设置依赖注入
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddAevatarAgentSystem(builder =>
{
    builder.UseLocalRuntime();
});

var serviceProvider = services.BuildServiceProvider();

// 获取工厂
var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();

Console.WriteLine("📊 创建 Calculator Agent...");
var calculatorId = Guid.NewGuid().ToString();
var calculatorActor = await factory.CreateGAgentActorAsync<CalculatorAgent>(calculatorId);
Console.WriteLine($"✅ Calculator Agent 创建成功! ID: {calculatorActor.Id}\n");

// 通过 Actor 获取 Agent 并执行操作
var calculator = (CalculatorAgent)calculatorActor.GetAgent();

Console.WriteLine("🔢 执行计算操作:");
Console.WriteLine("─────────────────");

// 加法
var sum = await calculator.AddAsync(10, 5);
Console.WriteLine($"  10 + 5 = {sum}");

// 减法
var difference = await calculator.SubtractAsync(20, 8);
Console.WriteLine($"  20 - 8 = {difference}");

// 乘法
var product = await calculator.MultiplyAsync(6, 7);
Console.WriteLine($"  6 × 7 = {product}");

// 除法
var quotient = await calculator.DivideAsync(100, 4);
Console.WriteLine($"  100 ÷ 4 = {quotient}");

Console.WriteLine($"\n📝 计算历史:");
var history = calculator.GetHistory();
foreach (var item in history)
{
    Console.WriteLine($"  {item}");
}

Console.WriteLine($"\n✨ 最后结果: {calculator.GetLastResult()}");
Console.WriteLine($"📈 操作次数: {calculator.GetState().OperationCount}");

// 测试Weather Agent
Console.WriteLine("\n\n🌤️  创建 Weather Agent...");
var weatherId = Guid.NewGuid().ToString();
var weatherActor = await factory.CreateGAgentActorAsync<WeatherAgent>(weatherId);
Console.WriteLine($"✅ Weather Agent 创建成功! ID: {weatherActor.Id}\n");

var weather = (WeatherAgent)weatherActor.GetAgent();

Console.WriteLine("🌍 查询天气:");
Console.WriteLine("─────────────────");

var cities = new[] { "北京", "上海", "广州", "深圳" };
foreach (var city in cities)
{
    var weatherInfo = await weather.GetWeatherAsync(city);
    Console.WriteLine($"  {city}: {weatherInfo}");
}

Console.WriteLine($"\n📊 查询次数: {weather.GetQueryCount()}");

// 清理
Console.WriteLine("\n\n🧹 清理资源...");
await calculatorActor.DeactivateAsync();
await weatherActor.DeactivateAsync();

Console.WriteLine("✅ Demo 完成!");
Console.WriteLine("\n示例运行成功！");

