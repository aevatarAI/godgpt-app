using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Local.Subscription;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;

namespace Aevatar.Agents.Local.Tests;

public class LocalSubscriptionManagerTests
{
    private readonly LocalMessageStreamRegistry _streamRegistry;
    private readonly LocalSubscriptionManager _subscriptionManager;
    private readonly ILogger<LocalSubscriptionManager> _logger;

    public LocalSubscriptionManagerTests()
    {
        _streamRegistry = new LocalMessageStreamRegistry();
        _logger = Mock.Of<ILogger<LocalSubscriptionManager>>();
        _subscriptionManager = new LocalSubscriptionManager(_streamRegistry, _logger);
    }

    [Fact]
    public async Task SubscribeWithRetry_ShouldCreateSubscription_WhenStreamExists()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var eventReceived = false;
        
        // 创建父节点的stream
        var parentStream = _streamRegistry.GetOrCreateStream(parentId);

        // Act
        var subscription = await _subscriptionManager.SubscribeWithRetryAsync(
            parentId,
            childId,
            async envelope =>
            {
                eventReceived = true;
                await Task.CompletedTask;
            });

        // 发送测试事件
        var testEvent = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down,
            Timestamp = TimestampHelper.GetUtcNow()
        };
        await parentStream.ProduceAsync(testEvent);

        // 等待事件处理
        await Task.Delay(100);

        // Assert
        Assert.NotNull(subscription);
        Assert.Equal(parentId, subscription.ParentId);
        Assert.Equal(childId, subscription.ChildId);
        Assert.True(subscription.IsHealthy);
        Assert.True(eventReceived);
    }

    [Fact]
    public async Task SubscribeWithRetry_ShouldRetry_WhenStreamNotInitiallyAvailable()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var retryCount = 0;
        
        // Use more retries and shorter delay to give time for stream creation
        var retryPolicy = new TestRetryPolicy(5, () => retryCount++);

        // Delay stream creation to trigger retry (create after ~100ms, within retry window)
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            _streamRegistry.GetOrCreateStream(parentId);
        });

        // Act
        var subscription = await _subscriptionManager.SubscribeWithRetryAsync(
            parentId,
            childId,
            async envelope => await Task.CompletedTask,
            retryPolicy);

        // Assert
        Assert.NotNull(subscription);
        Assert.True(retryCount > 0); // Should have retried at least once
        Assert.True(subscription.IsHealthy);
    }

    [Fact]
    public async Task IsSubscriptionHealthy_ShouldReturnTrue_WhenSubscriptionActive()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        _streamRegistry.GetOrCreateStream(parentId);
        
        var subscription = await _subscriptionManager.SubscribeWithRetryAsync(
            parentId, childId, async _ => await Task.CompletedTask);

        // Act
        var isHealthy = await _subscriptionManager.IsSubscriptionHealthyAsync(subscription);

        // Assert
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task IsSubscriptionHealthy_ShouldReturnFalse_WhenStreamRemoved()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var stream = _streamRegistry.GetOrCreateStream(parentId);
        
        var subscription = await _subscriptionManager.SubscribeWithRetryAsync(
            parentId, childId, async _ => await Task.CompletedTask);

        // 移除stream
        _streamRegistry.RemoveStream(parentId);

        // Act
        var isHealthy = await _subscriptionManager.IsSubscriptionHealthyAsync(subscription);

        // Assert
        Assert.False(isHealthy);
    }

    [Fact]
    public async Task ReconnectSubscription_ShouldRestoreConnection_AfterDisconnection()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var eventCount = 0;
        
        var stream = _streamRegistry.GetOrCreateStream(parentId);
        
        var subscription = await _subscriptionManager.SubscribeWithRetryAsync(
            parentId, childId, async _ => { eventCount++; await Task.CompletedTask; });

        // 模拟断开连接 - 通过移除stream
        _streamRegistry.RemoveStream(parentId);
        // 重新创建stream
        stream = _streamRegistry.GetOrCreateStream(parentId);

        // Act - 重新连接
        await _subscriptionManager.ReconnectSubscriptionAsync(subscription);

        // 发送测试事件
        var testEvent = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test event" }),
            Direction = EventDirection.Down,
            Timestamp = TimestampHelper.GetUtcNow()
        };
        await stream.ProduceAsync(testEvent);
        await Task.Delay(100);

        // Assert
        Assert.True(subscription.IsHealthy);
        Assert.Equal(1, eventCount); // 应该接收到事件
    }

    [Fact]
    public async Task UnsubscribeAsync_ShouldRemoveSubscription()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        _streamRegistry.GetOrCreateStream(parentId);
        
        var subscription = await _subscriptionManager.SubscribeWithRetryAsync(
            parentId, childId, async _ => await Task.CompletedTask);

        // Act
        await _subscriptionManager.UnsubscribeAsync(subscription);

        // Assert
        var activeSubscriptions = await _subscriptionManager.GetActiveSubscriptionsAsync();
        Assert.DoesNotContain(activeSubscriptions, s => s.SubscriptionId == subscription.SubscriptionId);
    }

    [Fact]
    public async Task GetActiveSubscriptions_ShouldReturnAllSubscriptions()
    {
        // Arrange
        var parent1 = Guid.NewGuid().ToString();
        var parent2 = Guid.NewGuid().ToString();
        var child1 = Guid.NewGuid().ToString();
        var child2 = Guid.NewGuid().ToString();
        
        _streamRegistry.GetOrCreateStream(parent1);
        _streamRegistry.GetOrCreateStream(parent2);

        await _subscriptionManager.SubscribeWithRetryAsync(
            parent1, child1, async _ => await Task.CompletedTask);
        await _subscriptionManager.SubscribeWithRetryAsync(
            parent2, child2, async _ => await Task.CompletedTask);

        // Act
        var subscriptions = await _subscriptionManager.GetActiveSubscriptionsAsync();

        // Assert
        Assert.Equal(2, subscriptions.Count);
    }

    [Fact]
    public async Task EventHandler_ShouldReceiveCorrectEvents()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        EventEnvelope? receivedEnvelope = null;
        
        var stream = _streamRegistry.GetOrCreateStream(parentId);
        
        await _subscriptionManager.SubscribeWithRetryAsync(
            parentId, childId, 
            async envelope =>
            {
                receivedEnvelope = envelope;
                await Task.CompletedTask;
            });

        // Act
        var testEvent = new EventEnvelope
        {
            Id = "test-123",
            // Payload包含事件信息
            Payload = Any.Pack(new StringValue { Value = "test data" }),
            Direction = EventDirection.Down,
            Timestamp = TimestampHelper.GetUtcNow()
        };
        await stream.ProduceAsync(testEvent);
        await Task.Delay(100);

        // Assert
        Assert.NotNull(receivedEnvelope);
        Assert.Equal("test-123", receivedEnvelope?.Id);
        // EventEnvelope不有EventType，验证通过Id和Payload即可
    }

    [Fact]
    public async Task RetryPolicy_ShouldRespectMaxRetries()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var attempts = 0;
        
        var retryPolicy = new TestRetryPolicy(2, () => attempts++);
        
        // 不创建stream，强制失败

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _subscriptionManager.SubscribeWithRetryAsync(
                parentId, childId,
                async _ => await Task.CompletedTask,
                retryPolicy,
                new CancellationTokenSource(500).Token); // 短超时
        });

        Assert.Equal(2, attempts); // 最多重试2次
    }

    // 测试用的重试策略
    private class TestRetryPolicy : IRetryPolicy
    {
        private readonly int _maxRetries;
        private readonly Action _onRetry;
        private int _currentAttempt = 0;

        public TestRetryPolicy(int maxRetries, Action onRetry)
        {
            _maxRetries = maxRetries;
            _onRetry = onRetry;
        }

        public int MaxRetries => _maxRetries;

        public bool ShouldRetry(Exception exception, int attempt)
        {
            _currentAttempt = attempt;
            if (attempt <= _maxRetries)
            {
                _onRetry();
                return true;
            }
            return false;
        }

        public TimeSpan GetDelay(int attempt)
        {
            return TimeSpan.FromMilliseconds(50);
        }
    }
}
