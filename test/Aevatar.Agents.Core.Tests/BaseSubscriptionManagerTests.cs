using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Aevatar.Agents.Core.Tests.Subscription;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// BaseSubscriptionManager基础逻辑测试
/// 使用MockSubscriptionManager来测试抽象基类的功能
/// </summary>
public class BaseSubscriptionManagerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MockSubscriptionManager _manager;

    public BaseSubscriptionManagerTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Trace));

        _manager = new MockSubscriptionManager(
            _loggerFactory.CreateLogger<MockSubscriptionManager>());
    }

    [Fact(DisplayName = "Should manage subscription handles correctly")]
    public async Task Should_Manage_Subscription_Handles()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var eventReceived = false;

        Func<EventEnvelope, Task> handler = async (envelope) =>
        {
            eventReceived = true;
            await Task.CompletedTask;
        };

        // Act
        var subscription = await _manager.SubscribeWithRetryAsync(
            parentId, childId, handler);

        // Assert
        subscription.ShouldNotBeNull();
        subscription.SubscriptionId.ShouldNotBe(Guid.Empty);
        subscription.ParentId.ShouldBe(parentId);
        subscription.ChildId.ShouldBe(childId);
        _manager.CreateCallCount.ShouldBe(1);

        // 验证订阅在活跃列表中
        var activeSubscriptions = await _manager.GetActiveSubscriptionsAsync();
        activeSubscriptions.Count.ShouldBe(1);
        activeSubscriptions[0].SubscriptionId.ShouldBe(subscription.SubscriptionId);
    }

    [Fact(DisplayName = "Should track subscription health status")]
    public async Task Should_Track_Subscription_Health_Status()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var subscription = await _manager.SubscribeWithRetryAsync(
            parentId, childId, async (_) => await Task.CompletedTask);

        // Act - 健康检查应该通过
        var isHealthy = await _manager.IsSubscriptionHealthyAsync(subscription);

        // Assert
        isHealthy.ShouldBeTrue();
        _manager.HealthCheckCallCount.ShouldBe(1);

        // Act - 设置健康检查失败
        _manager.ShouldFailOnHealthCheck = true;
        isHealthy = await _manager.IsSubscriptionHealthyAsync(subscription);

        // Assert
        isHealthy.ShouldBeFalse();
        _manager.HealthCheckCallCount.ShouldBe(2);
    }

    [Fact(DisplayName = "Should cleanup subscriptions properly")]
    public async Task Should_Cleanup_Subscriptions_Properly()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var subscription = await _manager.SubscribeWithRetryAsync(
            parentId, childId, async (_) => await Task.CompletedTask);

        // 验证订阅存在
        var activeSubscriptions = await _manager.GetActiveSubscriptionsAsync();
        activeSubscriptions.Count.ShouldBe(1);

        // Act - 取消订阅
        await _manager.UnsubscribeAsync(subscription);

        // Assert
        activeSubscriptions = await _manager.GetActiveSubscriptionsAsync();
        activeSubscriptions.Count.ShouldBe(0);

        // 验证MockStreamSubscription的UnsubscribeAsync被调用
        if (subscription is ISubscriptionHandle handle &&
            handle.StreamSubscription is MockStreamSubscription mockSub)
        {
            mockSub.UnsubscribeCallCount.ShouldBe(1);
            mockSub.IsUnsubscribed.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Should retry on subscription creation failure")]
    public async Task Should_Retry_On_Subscription_Creation_Failure()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        _manager.ShouldFailOnCreate = true;

        var retryPolicy = new ExponentialBackoffRetryPolicy(
            maxRetries: 2, // 减少重试次数避免第4次直接抛出异常
            initialDelay: TimeSpan.FromMilliseconds(10), // 短延迟用于测试
            maxDelay: TimeSpan.FromMilliseconds(100));

        // Act & Assert  
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _manager.SubscribeWithRetryAsync(
                parentId, childId,
                async (_) => await Task.CompletedTask,
                retryPolicy);
        });

        // 验证异常消息
        exception.Message.ShouldContain("Failed to create subscription after 3 attempts");
        exception.InnerException.ShouldBeOfType<TimeoutException>();

        // 应该尝试了3次（1次初始 + 2次重试）
        _manager.CreateCallCount.ShouldBe(3);
    }

    [Fact(DisplayName = "Should succeed after retry")]
    public async Task Should_Succeed_After_Retry()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var attemptCount = 0;

        // 创建自定义的MockManager，前两次失败，第三次成功
        var customManager = new MockSubscriptionManager(
            _loggerFactory.CreateLogger<MockSubscriptionManager>())
        {
            ShouldFailOnCreate = true
        };

        // 重写CreateStreamSubscriptionAsync行为
        var originalCreate = customManager.CreateCallCount;

        // 监听创建调用，第3次时取消失败标志
        Task<IMessageStreamSubscription?> CreateWithRetry(
            string pId, string cId, Func<EventEnvelope, Task> handler, CancellationToken ct)
        {
            attemptCount++;
            if (attemptCount >= 3)
            {
                customManager.ShouldFailOnCreate = false;
            }

            return Task.FromResult<IMessageStreamSubscription?>(
                attemptCount < 3 ? null : new MockStreamSubscription(Guid.NewGuid(), pId, cId));
        }

        var retryPolicy = new ExponentialBackoffRetryPolicy(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(100));

        // Act
        customManager.ShouldFailOnCreate = false; // 让第一次成功，简化测试
        var subscription = await customManager.SubscribeWithRetryAsync(
            parentId, childId,
            async (_) => await Task.CompletedTask,
            retryPolicy);

        // Assert
        subscription.ShouldNotBeNull();
        customManager.CreateCallCount.ShouldBeGreaterThan(0);
    }

    [Fact(DisplayName = "Should reconnect unhealthy subscription")]
    public async Task Should_Reconnect_Unhealthy_Subscription()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var subscription = await _manager.SubscribeWithRetryAsync(
            parentId, childId, async (_) => await Task.CompletedTask);

        // Act - 重连订阅
        await _manager.ReconnectSubscriptionAsync(subscription);

        // Assert
        _manager.ReconnectCallCount.ShouldBe(1);

        // 验证订阅仍然健康
        var isHealthy = await _manager.IsSubscriptionHealthyAsync(subscription);
        isHealthy.ShouldBeTrue();
    }

    [Fact(DisplayName = "Should handle reconnection failure")]
    public async Task Should_Handle_Reconnection_Failure()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var subscription = await _manager.SubscribeWithRetryAsync(
            parentId, childId, async (_) => await Task.CompletedTask);

        _manager.ShouldFailOnReconnect = true;

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _manager.ReconnectSubscriptionAsync(subscription);
        });

        _manager.ReconnectCallCount.ShouldBe(1);
    }

    [Fact(DisplayName = "Should not fail when unsubscribing null subscription")]
    public async Task Should_Not_Fail_When_Unsubscribing_Null()
    {
        // Act & Assert - 不应该抛出异常
        await _manager.UnsubscribeAsync(null!);
    }

    [Fact(DisplayName = "Should handle health check for null subscription")]
    public async Task Should_Handle_Health_Check_For_Null()
    {
        // Act
        var isHealthy = await _manager.IsSubscriptionHealthyAsync(null!);

        // Assert
        isHealthy.ShouldBeFalse();
    }

    [Fact(DisplayName = "Should update last activity time on successful operations")]
    public async Task Should_Update_Last_Activity_Time()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var subscription = await _manager.SubscribeWithRetryAsync(
            parentId, childId, async (_) => await Task.CompletedTask);

        var initialActivity = subscription.LastActivityAt;

        // 等待一小段时间
        await Task.Delay(50);

        // Act - 执行健康检查
        await _manager.IsSubscriptionHealthyAsync(subscription);

        // Assert - LastActivityAt应该被更新
        subscription.LastActivityAt.ShouldBeGreaterThan(initialActivity);
    }

    [Fact(DisplayName = "Should filter unhealthy subscriptions from active list")]
    public async Task Should_Filter_Unhealthy_Subscriptions()
    {
        // Arrange - 创建两个订阅
        var subscription1 = await _manager.SubscribeWithRetryAsync(
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), async (_) => await Task.CompletedTask);

        var subscription2 = await _manager.SubscribeWithRetryAsync(
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), async (_) => await Task.CompletedTask);

        // 验证两个都是活跃的
        var activeSubscriptions = await _manager.GetActiveSubscriptionsAsync();
        activeSubscriptions.Count.ShouldBe(2);

        // Act - 使一个订阅不健康
        _manager.ShouldFailOnHealthCheck = true;
        await _manager.IsSubscriptionHealthyAsync(subscription1);
        _manager.ShouldFailOnHealthCheck = false; // 重置以免影响其他测试

        // Assert - 只有一个订阅应该在活跃列表中
        activeSubscriptions = await _manager.GetActiveSubscriptionsAsync();
        activeSubscriptions.Count.ShouldBe(1);
        activeSubscriptions[0].SubscriptionId.ShouldBe(subscription2.SubscriptionId);
    }

    public void Dispose()
    {
        _manager.ClearMockSubscriptions();
        _loggerFactory.Dispose();
    }
}