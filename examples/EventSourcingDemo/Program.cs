using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Helpers;
using EventSourcingDemo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("🌌 Aevatar Agent Framework - EventSourcing Demo");
Console.WriteLine("==================================================\n");
Console.WriteLine("展示新的 EventSourcing API:");
Console.WriteLine("  ✅ 批量事件提交 (RaiseEvent + ConfirmEventsAsync)");
Console.WriteLine("  ✅ 纯函数式状态转换 (TransitionState)");
Console.WriteLine("  ✅ 自动事件重放 (OnActivateAsync)");
Console.WriteLine("  ✅ 快照优化 (Snapshot Strategy)\n");

// 配置日志
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// 配置服务
var services = new ServiceCollection();
services.AddSingleton<InMemoryEventStore>();  // 注册为具体类型
services.AddSingleton<IEventStore>(provider => provider.GetRequiredService<InMemoryEventStore>());  // 同时注册为接口
services.AddSingleton<Aevatar.Agents.Abstractions.EventSourcing.IEventStore>(provider => provider.GetRequiredService<InMemoryEventStore>());  // 确保注册为框架接口
services.AddSingleton(loggerFactory);
services.AddSingleton<Aevatar.Agents.Abstractions.IGAgentFactory, Aevatar.Agents.AI.Core.AIGAgentFactory>();  // 注册工厂
var serviceProvider = services.BuildServiceProvider();

// 获取共享的 EventStore
var eventStore = serviceProvider.GetRequiredService<InMemoryEventStore>();
var logger = loggerFactory.CreateLogger<BankAccountAgent>();

// ============================================================
// Part 1: 创建账户并执行交易
// ============================================================
Console.WriteLine("📍 Part 1: Creating Account and Transactions");
Console.WriteLine("══════════════════════════════════════════════\n");

// 使用 AIGAgentFactory 创建 Agent（自动注入 EventStore）
var factory = serviceProvider.GetRequiredService<Aevatar.Agents.Abstractions.IGAgentFactory>();
var agentId = Guid.NewGuid().ToString();
var agent = factory.CreateGAgent<BankAccountAgent>(agentId);

if (agent == null)
{
    Console.WriteLine("❌ Failed to create BankAccountAgent");
    return;
}

await agent.ActivateAsync();

Console.WriteLine($"📊 Agent Created (with auto-injected EventStore)");
Console.WriteLine($"   ID: {agentId:N}\n");

// 创建账户
await agent.CreateAccountAsync("Alice Smith", 100);

Console.WriteLine($"✅ Account created");
Console.WriteLine($"   Holder: {agent.GetState().AccountHolder}");
Console.WriteLine($"   Balance: ${agent.GetState().Balance:F2}");
Console.WriteLine($"   Version: v{agent.GetCurrentVersion()}\n");

// 执行单个交易
Console.WriteLine("💰 Individual Transactions:");
Console.WriteLine("────────────────────────────");

await agent.DepositAsync(1000, "Salary");
Console.WriteLine($"  ✓ Deposited $1000 (Salary) - Balance: ${agent.GetState().Balance:F2}");

await agent.DepositAsync(500, "Bonus");
Console.WriteLine($"  ✓ Deposited $500 (Bonus) - Balance: ${agent.GetState().Balance:F2}");

await agent.WithdrawAsync(300, "Rent");
Console.WriteLine($"  ✓ Withdrew $300 (Rent) - Balance: ${agent.GetState().Balance:F2}");

Console.WriteLine($"\n💵 Current Balance: ${agent.GetState().Balance:F2}");
Console.WriteLine($"📈 Current Version: v{agent.GetCurrentVersion()}");

// ============================================================
// Part 2: 批量交易演示（新 API 优势）
// ============================================================
Console.WriteLine("\n\n📍 Part 2: Batch Transactions (New API Feature)");
Console.WriteLine("══════════════════════════════════════════════════\n");

Console.WriteLine("⚡ Submitting 3 transactions in one batch:");
Console.WriteLine("────────────────────────────────────────────");

var batchTransactions = new[]
{
    ("deposit", 200m, "Freelance"),
    ("deposit", 150m, "Investment Return"),
    ("withdraw", 100m, "Groceries")
};

await agent.BatchTransactionsAsync(batchTransactions);

Console.WriteLine($"  ✓ Batch completed (3 transactions)");
Console.WriteLine($"\n💵 New Balance: ${agent.GetState().Balance:F2}");
Console.WriteLine($"📈 New Version: v{agent.GetCurrentVersion()}");

// ============================================================
// Part 3: 查看事件历史
// ============================================================
Console.WriteLine("\n\n📍 Part 3: Event History");
Console.WriteLine("══════════════════════════════════════════\n");

var events = await eventStore.GetEventsAsync(agentId);
Console.WriteLine($"📝 Stored Events ({events.Count} total):");
Console.WriteLine("────────────────────────────────────────────");
foreach (var evt in events)
{
    var eventName = evt.EventType.Split('.').Last();
    var metadata = evt.Metadata.Any() 
        ? $" [{string.Join(", ", evt.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}]" 
        : "";
    Console.WriteLine($"  v{evt.Version}: {eventName,-20} at {evt.Timestamp.ToDateTime().ToLocalTime():HH:mm:ss.fff}{metadata}");
}

// 显示交易历史
Console.WriteLine($"\n📜 Transaction History:");
Console.WriteLine("────────────────────────────────────────────");
foreach (var history in agent.GetState().History)
{
    Console.WriteLine($"  {history}");
}

// ============================================================
// Part 4: 崩溃恢复演示
// ============================================================
Console.WriteLine("\n\n📍 Part 4: Crash Recovery Simulation");
Console.WriteLine("══════════════════════════════════════════════\n");

Console.WriteLine("💥 Simulating system crash...");
Console.WriteLine("────────────────────────────────────────────");

// 使用同样的工厂和相同的ID创建新 Agent（模拟重启恢复）
// EventStore会自动注入，OnActivateAsync会自动重放事件
var recoveredAgent = factory.CreateGAgent<BankAccountAgent>(agentId) as BankAccountAgent;

if (recoveredAgent == null)
{
    Console.WriteLine("❌ Failed to create recovered BankAccountAgent");
    return;
}

Console.WriteLine($"   Initial state (before recovery):");
Console.WriteLine($"   - Balance: ${recoveredAgent.GetState().Balance:F2}");
Console.WriteLine($"   - Version: v{recoveredAgent.GetCurrentVersion()}");
Console.WriteLine($"   - Transactions: {recoveredAgent.GetState().TransactionCount}");

Console.WriteLine($"\n🔄 Replaying events from EventStore...");
Console.WriteLine($"   Agent ID: {recoveredAgent.Id:N}");
Console.WriteLine($"   EventStore has events: {(await eventStore.GetEventsAsync(agentId)).Count}");

// 激活 Agent（自动重放事件）
await recoveredAgent.ActivateAsync();

Console.WriteLine($"\n✅ State recovered successfully!");
Console.WriteLine($"   - Balance: ${recoveredAgent.GetState().Balance:F2}");
Console.WriteLine($"   - Version: v{recoveredAgent.GetCurrentVersion()}");
Console.WriteLine($"   - Transactions: {recoveredAgent.GetState().TransactionCount}");
Console.WriteLine($"   - Holder: {recoveredAgent.GetState().AccountHolder}");

// 验证恢复的状态
if (recoveredAgent.GetState().Balance == agent.GetState().Balance &&
    recoveredAgent.GetCurrentVersion() == agent.GetCurrentVersion() &&
    recoveredAgent.GetState().AccountHolder == agent.GetState().AccountHolder)
{
    Console.WriteLine($"\n🎉 Verification: ✅ State perfectly recovered from events!");
    
    // 继续操作恢复的 Agent
    Console.WriteLine($"\n💳 Continuing with recovered agent:");
    Console.WriteLine("────────────────────────────────────────────");
    
    await recoveredAgent.WithdrawAsync(100, "Coffee");
    Console.WriteLine($"  ✓ Withdrew $100 (Coffee)");
    
    Console.WriteLine($"\n💵 Final Balance: ${recoveredAgent.GetState().Balance:F2}");
    Console.WriteLine($"📈 Final Version: v{recoveredAgent.GetCurrentVersion()}");
}
else
{
    Console.WriteLine($"\n❌ Verification failed! State mismatch!");
}

// ============================================================
// Part 5: 快照演示（可选）
// ============================================================
Console.WriteLine("\n\n📍 Part 5: Snapshot Support (Optional)");
Console.WriteLine("══════════════════════════════════════════════\n");

var currentVersion = recoveredAgent.GetCurrentVersion();
Console.WriteLine($"Current version: v{currentVersion}");
Console.WriteLine($"Snapshot strategy: Every 5 events (default: IntervalSnapshotStrategy(5))");
Console.WriteLine($"\n💡 Snapshots are automatically created during ConfirmEventsAsync()");
Console.WriteLine($"   when the snapshot strategy condition is met.");

// ============================================================
// Summary
// ============================================================
Console.WriteLine("\n\n✅ EventSourcing Demo completed successfully!");
Console.WriteLine("══════════════════════════════════════════════════\n");

Console.WriteLine("🌟 Key Features Demonstrated:");
Console.WriteLine("  ✓ Batch Event Submission (RaiseEvent + ConfirmEventsAsync)");
Console.WriteLine("  ✓ Pure Functional State Transition (TransitionState)");
Console.WriteLine("  ✓ Automatic Event Replay (OnActivateAsync)");
Console.WriteLine("  ✓ Metadata Support for Events");
Console.WriteLine("  ✓ Crash Recovery with Perfect State Restoration");
Console.WriteLine("  ✓ Optimistic Concurrency Control");
Console.WriteLine("  ✓ Snapshot Strategy Support");

Console.WriteLine("\n🚀 Next: Run MultiRuntimeEventSourcingDemo to see cross-runtime support!");
Console.WriteLine("────────────────────────────────────────────────────────────────────────");

// 运行多运行时演示
await MultiRuntimeEventSourcingDemo.RunAsync();
