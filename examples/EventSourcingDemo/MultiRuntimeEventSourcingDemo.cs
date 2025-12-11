using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;

namespace EventSourcingDemo;

/// <summary>
/// 多运行时 EventSourcing 演示
/// 使用新的 WithEventSourcingAsync 扩展方法
/// </summary>
public static class MultiRuntimeEventSourcingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n\n");
        Console.WriteLine("🌌 ═══════════════════════════════════════════════════");
        Console.WriteLine("   Multi-Runtime EventSourcing Demo");
        Console.WriteLine("   展示 EventSourcing 在不同运行时下的统一API");
        Console.WriteLine("═══════════════════════════════════════════════════\n");
        
        // 配置依赖注入
        var services = ConfigureServices();
        var serviceProvider = services.BuildServiceProvider();
        
        // 创建共享的 EventStore（所有运行时共享同一个存储）
        var sharedEventStore = serviceProvider.GetRequiredService<InMemoryEventStore>();
        
        // 1. Local 运行时演示
        await DemoLocalRuntime(sharedEventStore, serviceProvider);
        
        // 2. Orleans 运行时演示
        await DemoOrleansRuntime();
        
        Console.WriteLine("\n✅ Multi-Runtime EventSourcing Demo 完成！");
        Console.WriteLine("🌟 所有运行时都使用统一的 EventSourcing API！");
    }
    
    /// <summary>
    /// Local 运行时演示（使用新 API）
    /// </summary>
    private static async Task DemoLocalRuntime(InMemoryEventStore eventStore, IServiceProvider serviceProvider)
    {
        Console.WriteLine("📍 Local Runtime EventSourcing");
        Console.WriteLine("════════════════════════════════════════");
        
        var agentId = Guid.NewGuid();
        Console.WriteLine($"Agent ID: {agentId:N}");
        
        // 创建工厂
        var logger = serviceProvider.GetRequiredService<ILogger<LocalGAgentActorFactory>>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var factory = new LocalGAgentActorFactory(serviceProvider, logger, loggerFactory);
        
        // ✅ 场景1：自动 EventSourcing 注入
        Console.WriteLine("\n⚡ 场景1：AIGAgentFactory 自动注入 EventStore");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 创建 Actor（EventStore 已通过 DI 自动注入）
        var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
        
        var agent = actor.GetAgent() as BankAccountAgent;
        if (agent == null)
        {
            Console.WriteLine("  ❌ 无法获取 Agent 实例");
            return;
        }
        
        Console.WriteLine("  ✓ Actor 创建成功");
        Console.WriteLine("  ✓ EventSourcing 自动启用");
        
        // 执行交易
        await agent.CreateAccountAsync("Local User", 1000);
        await agent.DepositAsync(500, "Salary");
        await agent.WithdrawAsync(200, "Shopping");
        
        Console.WriteLine($"\n  💵 Balance: ${agent.GetState().Balance:F2}");
        Console.WriteLine($"  📈 Version: v{agent.GetCurrentVersion()}");
        Console.WriteLine($"  🔢 Transactions: {agent.GetState().TransactionCount}");
        
        // ✅ 场景1.5：RPC 接口调用测试
        Console.WriteLine("\n⚡ 场景1.5：RPC 接口调用测试");
        Console.WriteLine("───────────────────────────────────────────────");
        
        try
        {
            // Test RPC calls via interface (using extension method)
            var balance = await actor.InvokeRpcAsync<double>("GetBalanceAsync");
            var holder = await actor.InvokeRpcAsync<string>("GetAccountHolderAsync");
            var txCount = await actor.InvokeRpcAsync<int>("GetTransactionCountAsync");
            var version = await actor.InvokeRpcAsync<long>("GetCurrentVersionAsync");
            var summary = await actor.InvokeRpcAsync<Events.AccountSummary>("GetAccountSummaryAsync");
            
            Console.WriteLine($"  ✅ RPC GetBalanceAsync: ${balance:F2}");
            Console.WriteLine($"  ✅ RPC GetAccountHolderAsync: {holder}");
            Console.WriteLine($"  ✅ RPC GetTransactionCountAsync: {txCount}");
            Console.WriteLine($"  ✅ RPC GetCurrentVersionAsync: v{version}");
            Console.WriteLine($"  ✅ RPC GetAccountSummaryAsync:");
            Console.WriteLine($"     - Holder: {summary.AccountHolder}");
            Console.WriteLine($"     - Balance: ${summary.Balance:F2}");
            Console.WriteLine($"     - Transactions: {summary.TransactionCount}");
            Console.WriteLine($"     - Version: v{summary.Version}");
            
            // Verify RPC results match direct access
            if (Math.Abs(balance - agent.GetState().Balance) < 0.01 &&
                holder == agent.GetState().AccountHolder &&
                txCount == agent.GetState().TransactionCount &&
                version == agent.GetCurrentVersion())
            {
                Console.WriteLine($"\n  🎉 RPC 调用验证成功！所有接口调用结果正确！");
            }
            else
            {
                Console.WriteLine($"\n  ⚠️ RPC 调用结果与直接访问不匹配");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ RPC 调用失败: {ex.Message}");
            Console.WriteLine($"     {ex.GetType().Name}: {ex.StackTrace}");
        }
        
        // ✅ 场景2：批量交易演示
        Console.WriteLine("\n⚡ 场景2：批量交易（展示批量提交优势）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        var batchTransactions = new[]
        {
            ("deposit", 300m, "Bonus"),
            ("deposit", 100m, "Refund"),
            ("withdraw", 50m, "Coffee")
        };
        
        await agent.BatchTransactionsAsync(batchTransactions);
        
        Console.WriteLine($"  ✓ Batch completed (3 transactions in 1 commit)");
        Console.WriteLine($"  💵 New Balance: ${agent.GetState().Balance:F2}");
        Console.WriteLine($"  📈 New Version: v{agent.GetCurrentVersion()}");
        
        // ✅ 场景3：崩溃恢复
        Console.WriteLine("\n⚡ 场景3：崩溃恢复（自动事件重放）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 停止原 Actor
        await actor.DeactivateAsync();
        Console.WriteLine("  ✓ 原 Actor 已停止");
        
        // 检查事件
        var events = await eventStore.GetEventsAsync(agentId);
        Console.WriteLine($"  📝 EventStore 中的事件数: {events.Count}");
        
        // 创建新 Actor（EventStore 会自动注入并重放事件）
        var newActor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
        
        var recoveredAgent = newActor.GetAgent() as BankAccountAgent;
        if (recoveredAgent != null)
        {
            Console.WriteLine($"\n  ✅ 状态完美恢复:");
            Console.WriteLine($"     Balance: ${recoveredAgent.GetState().Balance:F2}");
            Console.WriteLine($"     Version: v{recoveredAgent.GetCurrentVersion()}");
            Console.WriteLine($"     Holder: {recoveredAgent.GetState().AccountHolder}");
            Console.WriteLine($"     Transactions: {recoveredAgent.GetState().TransactionCount}");
            
            // 验证
            if (recoveredAgent.GetState().Balance == 1650.0 && 
                recoveredAgent.GetCurrentVersion() == 7)  // 1 create + 2 individual + 3 batch + 0 (batch is 1 commit)
            {
                Console.WriteLine($"\n  🎉 Local Runtime EventSourcing 验证成功!");
            }
            
            // ✅ 场景3.5：恢复后 RPC 调用测试
            Console.WriteLine("\n⚡ 场景3.5：恢复后 RPC 调用测试");
            Console.WriteLine("───────────────────────────────────────────────");
            
            try
            {
                var recoveredBalance = await newActor.InvokeRpcAsync<double>("GetBalanceAsync");
                var recoveredSummary = await newActor.InvokeRpcAsync<Events.AccountSummary>("GetAccountSummaryAsync");
                
                Console.WriteLine($"  ✅ 恢复后 RPC GetBalanceAsync: ${recoveredBalance:F2}");
                Console.WriteLine($"  ✅ 恢复后 RPC GetAccountSummaryAsync:");
                Console.WriteLine($"     - Balance: ${recoveredSummary.Balance:F2}");
                Console.WriteLine($"     - Version: v{recoveredSummary.Version}");
                
                if (Math.Abs(recoveredBalance - recoveredAgent.GetState().Balance) < 0.01 &&
                    recoveredSummary.Version == recoveredAgent.GetCurrentVersion())
                {
                    Console.WriteLine($"\n  🎉 恢复后 RPC 调用验证成功！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ 恢复后 RPC 调用失败: {ex.Message}");
            }
        }
        
        // ✅ 场景4：展示事件元数据
        Console.WriteLine("\n⚡ 场景4：事件元数据（用于审计和调试）");
        Console.WriteLine("───────────────────────────────────────────────");
        
        // 获取最近的几个事件
        var recentEvents = await eventStore.GetEventsAsync(agentId, fromVersion: 1, maxCount: 5);
        Console.WriteLine($"  📝 最近 {recentEvents.Count} 个事件:");
        
        foreach (var evt in recentEvents)
        {
            var eventName = evt.EventType.Split('.').Last();
            var metadataStr = evt.Metadata.Any() 
                ? $" | Metadata: {string.Join(", ", evt.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}"
                : "";
            Console.WriteLine($"     v{evt.Version}: {eventName,-20}{metadataStr}");
        }
        
        Console.WriteLine($"\n  ✅ Local Runtime 演示完成!");
    }
    
    /// <summary>
    /// Orleans 运行时演示
    /// </summary>
    private static async Task DemoOrleansRuntime()
    {
        Console.WriteLine("\n\n📍 Orleans Runtime EventSourcing + RPC");
        Console.WriteLine("════════════════════════════════════════════");
        
        // 启动内嵌 Orleans Silo
        Console.WriteLine("🚀 启动 Orleans Silo...");
        
        var host = Host.CreateDefaultBuilder()
            .UseOrleans((context, siloBuilder) =>
            {
                siloBuilder.UseLocalhostClustering();
                siloBuilder.AddMemoryGrainStorage("Default");
                siloBuilder.AddMemoryStreams("Default");
                siloBuilder.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "eventsourcing-demo";
                    options.ServiceId = "bank-demo";
                });
            })
            .ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
                // EventStore - required for state updates
                services.AddSingleton<InMemoryEventStore>();
                services.AddSingleton<IEventStore>(p => p.GetRequiredService<InMemoryEventStore>());
                services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
                services.AddGAgentActorFactoryProvider();
            })
            .Build();
        
        await host.StartAsync();
        Console.WriteLine("✅ Orleans Silo 已启动\n");
        
        try
        {
            // 获取工厂
            var clusterClient = host.Services.GetRequiredService<IClusterClient>();
            var logger = host.Services.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
            var factory = new OrleansGAgentActorFactory(host.Services, clusterClient, logger);
            
            var agentId = Guid.NewGuid();
            Console.WriteLine($"Agent ID: {agentId:N}");
            
            // 创建 Orleans Actor
            Console.WriteLine("\n⚡ 场景5：Orleans Runtime RPC 调用");
            Console.WriteLine("───────────────────────────────────────────────");
            
            var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
            Console.WriteLine("  ✓ Orleans Actor 创建成功");
            
            // 通过 RPC 创建账户 (decimal 转成 double 传输)
            Console.Write("  RPC CreateAccountAsync... ");
            await actor.InvokeRpcAsync("CreateAccountAsync", "Orleans User", 2000m);
            Console.WriteLine("✅");
            
            // 通过 RPC 存款
            Console.Write("  RPC DepositAsync(800)... ");
            await actor.InvokeRpcAsync("DepositAsync", 800m, "Orleans Deposit");
            Console.WriteLine("✅");
            
            // 通过 RPC 取款
            Console.Write("  RPC WithdrawAsync(300)... ");
            await actor.InvokeRpcAsync("WithdrawAsync", 300m, "Orleans Withdraw");
            Console.WriteLine("✅");
            
            // 通过 RPC 获取状态
            Console.Write("  RPC GetBalanceAsync... ");
            var balance = await actor.InvokeRpcAsync<double>("GetBalanceAsync");
            Console.WriteLine($"✅ 返回: ${balance:F2}");
            
            Console.Write("  RPC GetAccountHolderAsync... ");
            var holder = await actor.InvokeRpcAsync<string>("GetAccountHolderAsync");
            Console.WriteLine($"✅ 返回: {holder}");
            
            Console.Write("  RPC GetTransactionCountAsync... ");
            var txCount = await actor.InvokeRpcAsync<int>("GetTransactionCountAsync");
            Console.WriteLine($"✅ 返回: {txCount}");
            
            Console.Write("  RPC GetAccountSummaryAsync... ");
            var summary = await actor.InvokeRpcAsync<Events.AccountSummary>("GetAccountSummaryAsync");
            Console.WriteLine("✅");
            Console.WriteLine($"     - Holder: {summary.AccountHolder}");
            Console.WriteLine($"     - Balance: ${summary.Balance:F2}");
            Console.WriteLine($"     - Transactions: {summary.TransactionCount}");
            Console.WriteLine($"     - Version: v{summary.Version}");
            
            // 验证 (TransactionCount = 2: Deposit + Withdraw, CreateAccount sets it to 0)
            var expectedBalance = 2000.0 + 800.0 - 300.0; // 2500
            if (Math.Abs(balance - expectedBalance) < 0.01 && 
                holder == "Orleans User" && 
                txCount == 2)
            {
                Console.WriteLine($"\n  🎉 Orleans Runtime RPC 验证成功！");
                Console.WriteLine($"     预期余额: ${expectedBalance:F2}, 实际: ${balance:F2}");
                Console.WriteLine($"     交易数: {txCount}");
            }
            else
            {
                Console.WriteLine($"\n  ❌ Orleans Runtime RPC 验证失败");
                Console.WriteLine($"     预期余额: ${expectedBalance:F2}, 实际: ${balance:F2}");
                Console.WriteLine($"     预期交易数: 2, 实际: {txCount}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  ❌ Orleans RPC 调用失败: {ex.Message}");
            Console.WriteLine($"     {ex.StackTrace}");
        }
        finally
        {
            Console.WriteLine("\n🛑 停止 Orleans Silo...");
            await host.StopAsync();
            Console.WriteLine("✅ Orleans Silo 已停止");
        }
    }
    
    /// <summary>
    /// 配置服务
    /// </summary>
    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        
        // 日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        // Local Agent Runtime - 注册工厂提供者
        services.AddAevatarLocalRuntime();
        
        // EventStore - 注册为单例（所有运行时共享）
        services.AddSingleton<InMemoryEventStore>();
        services.AddSingleton<IEventStore>(
            provider => provider.GetRequiredService<InMemoryEventStore>());

        // 注册 AIGAgentFactory（会自动注入 EventStore）
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
        
        return services;
    }
}
