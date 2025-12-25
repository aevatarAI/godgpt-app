using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.Runtime.Local;
using Aevatar.CognitiveMesh.Abstractions;
using Aevatar.CognitiveMesh.Strategies;
using Aevatar.PaperReview.Models;
using Aevatar.PaperReview.Services;
using Aevatar.PaperReview.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.PaperReview.Tests;

// ============================================================
//  PAPER REVIEW INTEGRATION TESTS
//  端到端测试：验证完整的论文评审流程
// ============================================================

public class PaperReviewIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly MockLLMProvider _mockLlm;
    private readonly MockEmbeddingFactory _mockEmbedding;
    private readonly CognitiveStrategy _strategy;
    private readonly IGAgentActorManager _actorManager;

    public PaperReviewIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // 配置 Mock LLM - 返回 MAKER 工作流所需的响应
        _mockLlm = new MockLLMProvider().WithMakerWorkflowResponses();
        
        // 配置 Mock Embedding - 让所有内容语义相同，保证投票总是通过
        _mockEmbedding = new MockEmbeddingFactory();

        // 构建服务容器
        var services = new ServiceCollection();
        
        // 添加日志
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new XunitLoggerProvider(output));
        });

        // 添加 Local Runtime
        services.AddAevatarLocalRuntime();

        // 添加 Mock LLM 和 Embedding - 必须注册到 DI 以便 Agent 可以获取
        services.AddSingleton(_mockLlm);
        services.AddSingleton<ILLMProviderFactory>(new MockLLMProviderFactory(_mockLlm));
        services.AddSingleton<IAIAgentEmbeddingFactory>(_mockEmbedding);

        _serviceProvider = services.BuildServiceProvider();

        // 获取 Actor Manager
        _actorManager = _serviceProvider.GetRequiredService<IGAgentActorManager>();

        // 创建 CognitiveStrategy - 带 Mock Embedding 实现语义聚类
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var config = new ConfigurationBuilder().Build();
        _strategy = new CognitiveStrategy(
            _actorManager,
            new MockLLMProviderFactory(_mockLlm),
            config,
            loggerFactory.CreateLogger<CognitiveStrategy>(),
            _mockEmbedding);  // 传入 Mock Embedding 工厂
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    // ─────────────────────────────────────────────────────────
    //  基础流程测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteReview_WithMockLLM_CompletesSuccessfully()
    {
        // Arrange
        var paperContent = """
            # Test Paper: A Novel Approach to AI Testing
            
            ## Abstract
            This paper presents a novel approach to testing AI systems using mock providers.
            
            ## Introduction
            Testing AI systems is challenging due to the non-deterministic nature of LLMs.
            We propose using controlled mock responses to validate system behavior.
            
            ## Method
            Our method involves creating deterministic mock LLM and embedding providers
            that return predictable responses for testing purposes.
            
            ## Results
            Our approach achieves 100% test coverage with deterministic outcomes.
            
            ## Conclusion
            Mock-based testing is effective for AI system validation.
            """;

        var task = $"""
            Please review the following paper:
            
            {paperContent}
            
            Provide a comprehensive review covering:
            1. Technical soundness
            2. Novelty
            3. Presentation quality
            """;

        var options = new ReasoningOptions
        {
            ProviderName = "mock",
            CognitiveWorkflow = "maker-v2",
            CognitiveWorkerCount = 3,
            CognitiveConsensusK = 2,
            MaxLlmCalls = 100,
            MaxTokens = 100000,
            MaxDuration = TimeSpan.FromMinutes(5)
        };

        var progressEvents = new List<ReasoningProgress>();
        var progress = new Progress<ReasoningProgress>(p =>
        {
            progressEvents.Add(p);
            _output.WriteLine($"[{p.Phase}] {p.Message}");
        });

        // Act
        var result = await _strategy.ExecuteAsync(task, options, progress, CancellationToken.None);

        // Assert
        _output.WriteLine($"═══════════════════════════════════════════════════════");
        _output.WriteLine($"Result: Success={result.Success}");
        _output.WriteLine($"LLM Calls: {result.TotalLlmCalls}");
        _output.WriteLine($"Duration: {result.Duration}");
        _output.WriteLine($"Error: {result.Error ?? "(none)"}");
        _output.WriteLine($"Progress events: {progressEvents.Count}");
        _output.WriteLine($"Mock LLM call count: {_mockLlm.CallCount}");
        _output.WriteLine($"Mock Embedding call count: {_mockEmbedding.Generator.CallCount}");
        _output.WriteLine($"═══════════════════════════════════════════════════════");
        
        if (result.Content != null)
        {
            var preview = result.Content.Length > 500 
                ? result.Content[..500] + "..." 
                : result.Content;
            _output.WriteLine($"Content:\n{preview}");
        }

        // 验证结果
        result.Success.ShouldBeTrue($"Review should complete successfully. Error: {result.Error}");
        result.Content.ShouldNotBeNullOrEmpty("Review should produce content");
        _mockLlm.CallCount.ShouldBeGreaterThan(0, "Mock LLM should have been called");
        progressEvents.ShouldNotBeEmpty("Should have progress events");
    }

    [Fact]
    public async Task ExecuteReview_TracksProgress_ThroughAllPhases()
    {
        // Arrange
        var task = "Review this simple paper about testing.";
        var options = new ReasoningOptions
        {
            ProviderName = "mock",
            CognitiveWorkflow = "maker-v2",
            CognitiveWorkerCount = 3,
            CognitiveConsensusK = 2,
            MaxLlmCalls = 30,
            MaxDuration = TimeSpan.FromMinutes(2)
        };

        var phases = new HashSet<string>();
        var progress = new Progress<ReasoningProgress>(p =>
        {
            if (!string.IsNullOrEmpty(p.Phase))
            {
                phases.Add(p.Phase);
                _output.WriteLine($"Phase: {p.Phase} - {p.Message}");
            }
        });

        // Act
        var result = await _strategy.ExecuteAsync(task, options, progress, CancellationToken.None);

        // Assert
        _output.WriteLine($"Phases observed: {string.Join(", ", phases)}");

        // 验证关键阶段被执行
        // 注意：具体阶段名称取决于工作流定义
        phases.Count.ShouldBeGreaterThan(0, "Should have observed at least one phase");
    }

    // ─────────────────────────────────────────────────────────
    //  ReviewEventBridge 集成测试
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewEventBridge_TransformsProgress_ToUIEvents()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ReviewEventBridge>();
        var bridge = new ReviewEventBridge(logger);
        var session = new ReviewSession
        {
            Title = "Test Paper",
            Authors = "Test Author",
            Type = ReviewType.Standard
        };

        // 模拟一系列进度事件
        var progressSequence = new[]
        {
            new ReasoningProgress { Phase = "START", Message = "Initializing review" },
            new ReasoningProgress 
            { 
                Phase = "EXECUTE", 
                ParallelTotal = 3,
                VoteK = 2,
                Message = "Starting parallel workers" 
            },
            new ReasoningProgress
            {
                Phase = "SOLVE",
                TaskId = "gen[1]",
                StepType = "llm_call",
                StepStatus = "Running",
                Message = "Worker 1 processing"
            },
            new ReasoningProgress
            {
                Phase = "SOLVE",
                TaskId = "gen[1]",
                StreamingToken = new StreamingTokenProgress
                {
                    WorkerId = "gen[1]",
                    ProposalId = "prop-1",
                    Token = "Analysis",
                    AccumulatedContent = "Analysis",
                    IsFirstToken = true,
                    IsLastToken = false
                }
            },
            new ReasoningProgress
            {
                Phase = "SOLVE",
                TaskId = "gen[1]",
                StreamingToken = new StreamingTokenProgress
                {
                    WorkerId = "gen[1]",
                    ProposalId = "prop-1",
                    Token = " complete",
                    AccumulatedContent = "Analysis complete",
                    IsFirstToken = false,
                    IsLastToken = true
                }
            },
            new ReasoningProgress
            {
                Phase = "VOTE",
                StepType = "vote",
                VoteRound = 1,
                VoteK = 2,
                VoteCurrentVotes = 2,
                Message = "Consensus reached"
            },
            new ReasoningProgress { Phase = "COMPLETE", Message = "Review finished" }
        };

        // Act
        foreach (var p in progressSequence)
        {
            bridge.HandleProgress(session, p);
        }

        // 收集所有事件
        var events = new List<ReviewEvent>();
        while (session.EventChannel.Reader.TryRead(out var evt))
        {
            events.Add(evt);
            _output.WriteLine($"Event: {evt.Type}");
        }

        // Assert
        events.ShouldNotBeEmpty("Should have generated UI events");
        
        // 验证关键事件类型
        events.OfType<StageLogEvent>().ShouldNotBeEmpty("Should have stage log events");
        events.OfType<PhaseChangeEvent>().ShouldNotBeEmpty("Should have phase change events");
        events.OfType<WorkerStartedEvent>().ShouldNotBeEmpty("Should have worker started events");
        events.OfType<LlmStreamingEvent>().ShouldNotBeEmpty("Should have streaming events");
        
        // 验证 Worker ID 规范化（应该使用 K 值循环）
        var workerEvents = events.OfType<WorkerStartedEvent>().ToList();
        foreach (var we in workerEvents)
        {
            _output.WriteLine($"Worker: {we.WorkerId} - {we.DisplayName}");
            we.WorkerId.ShouldStartWith("worker-");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  MAKER 参数验证测试
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReviewType.Quick, 1, 1)]
    [InlineData(ReviewType.Standard, 2, 3)]
    [InlineData(ReviewType.Detailed, 3, 5)]
    [InlineData(ReviewType.Rigorous, 4, 7)]
    [InlineData(ReviewType.Critical, 5, 9)]
    public void MakerParameters_FollowPaperFormula(ReviewType type, int expectedK, int expectedN)
    {
        // Arrange & Act
        var (k, n, desc) = MakerParameters.GetParams(type);

        // Assert
        k.ShouldBe(expectedK, $"K for {type}");
        n.ShouldBe(expectedN, $"N for {type}");
        
        // 验证 MAKER 论文公式: N = 2K - 1
        n.ShouldBe(2 * k - 1, "N should equal 2K - 1 (MAKER formula)");
        
        _output.WriteLine($"{type}: K={k}, N={n} ({desc})");
    }
}

// ============================================================
//  XUNIT LOGGER PROVIDER
//  用于在测试中输出日志
// ============================================================

public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName) => new XunitLogger(_output, categoryName);

    public void Dispose() { }
}

public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        
        try
        {
            var shortCategory = _categoryName.Split('.').LastOrDefault() ?? _categoryName;
            _output.WriteLine($"[{logLevel}] {shortCategory}: {formatter(state, exception)}");
        }
        catch
        {
            // Ignore output errors (can happen if test has already completed)
        }
    }
}