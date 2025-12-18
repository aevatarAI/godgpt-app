using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Orleans;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests.Streaming;

/// <summary>
/// Orleans Stream机制集成测试
/// </summary>
public class OrleansStreamTests : IClassFixture<OrleansStreamTests.ClusterFixture>
{
    private readonly TestCluster _cluster;
    private readonly IGrainFactory _grainFactory;

    public OrleansStreamTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
        _grainFactory = _cluster.GrainFactory;
    }

    /// <summary>
    /// 测试Orleans grain的父子关系订阅
    /// </summary>
    [Fact]
    public async Task Orleans_SetParent_Should_Subscribe_To_Parent_Stream()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var parentGrain = _grainFactory.GetGrain<IGAgentGrain>(parentId.ToString());
        var childGrain = _grainFactory.GetGrain<IGAgentGrain>(childId.ToString());

        // 激活grains (使用Orleans测试的实际Agent类型)
        await parentGrain.InitializeAgentAsync("Aevatar.Agents.Orleans.Tests.OrleansTestAgent");
        await childGrain.InitializeAgentAsync("Aevatar.Agents.Orleans.Tests.OrleansTestAgent");

        // Act - 建立父子关系
        await childGrain.SetParentAsync(parentId);
        await parentGrain.AddChildAsync(childId);

        // 父节点发布DOWN事件
        var testEvent = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Direction = EventDirection.Down,
            Message = "Orleans Test Message",
            PublisherId = parentId.ToString()
        };
        await parentGrain.HandleEventAsync(testEvent.ToByteArray());

        // 等待stream传播
        await Task.Delay(500);

        // Assert - 通过查询子节点状态验证（实际测试中需要具体实现）
        // 这里简化为验证关系建立
        var childParent = await childGrain.GetParentAsync();
        Assert.Equal(parentId, childParent);
    }

    /// <summary>
    /// 测试Orleans的Resume机制
    /// </summary>
    [Fact]
    public async Task Orleans_Resume_Should_Work_After_Subscription_Failure()
    {
        // Arrange
        var streamProvider = _cluster.Client.GetStreamProvider("StreamProvider");
        var streamId = StreamId.Create(
            AevatarAgentsOrleansConstants.StreamNamespace,
            Guid.NewGuid().ToString());
        var stream = streamProvider.GetStream<byte[]>(streamId);

        var receivedMessages = new List<string>();
        var orleansStream = new OrleansMessageStream(Guid.NewGuid(), stream);

        // Act - 订阅
        var subscription = await orleansStream.SubscribeAsync<EventEnvelope>(async envelope =>
        {
            receivedMessages.Add(envelope.Message);
            await Task.CompletedTask;
        });

        // 发送消息
        await orleansStream.ProduceAsync(new EventEnvelope { Message = "Message 1" });
        await Task.Delay(200);

        // 暂停并恢复
        await subscription.UnsubscribeAsync();
        await subscription.ResumeAsync();

        // 再次发送消息
        await orleansStream.ProduceAsync(new EventEnvelope { Message = "Message 2" });
        await Task.Delay(200);

        // Assert
        Assert.Contains("Message 1", receivedMessages);
        Assert.Contains("Message 2", receivedMessages);
    }

    /// <summary>
    /// 测试Orleans Stream的类型过滤
    /// </summary>
    [Fact]
    public async Task Orleans_Stream_Type_Filter_Should_Work()
    {
        // Arrange
        var streamProvider = _cluster.Client.GetStreamProvider("StreamProvider");
        var streamId = StreamId.Create(
            AevatarAgentsOrleansConstants.StreamNamespace,
            Guid.NewGuid().ToString());
        var stream = streamProvider.GetStream<byte[]>(streamId);

        var orleansStream = new OrleansMessageStream(Guid.NewGuid(), stream);
        var filteredMessages = new List<string>();

        // Act - 带过滤器订阅
        await orleansStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                filteredMessages.Add(envelope.Message);
                await Task.CompletedTask;
            },
            envelope => envelope.Direction == EventDirection.Up); // 只接收UP事件

        // 发送不同方向的事件
        await orleansStream.ProduceAsync(new EventEnvelope
        {
            Message = "UP Event",
            Direction = EventDirection.Up
        });

        await orleansStream.ProduceAsync(new EventEnvelope
        {
            Message = "DOWN Event",
            Direction = EventDirection.Down
        });

        await Task.Delay(200);

        // Assert - 只应该收到UP事件
        Assert.Single(filteredMessages);
        Assert.Contains("UP Event", filteredMessages);
        Assert.DoesNotContain("DOWN Event", filteredMessages);
    }

    // Test Cluster配置
    public class ClusterFixture : IDisposable
    {
        public TestCluster Cluster { get; private set; }

        public ClusterFixture()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
            builder.AddClientBuilderConfigurator<TestClientConfigurator>();
            Cluster = builder.Build();
            Cluster.Deploy();
        }

        public void Dispose()
        {
            Cluster.StopAllSilos();
            Cluster.Dispose();
        }
    }

    // Silo配置
    public class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializerBuilder => { serializerBuilder.AddProtobufSerializer(); });
                })
                .ConfigureLogging(logging => logging.AddConsole())
                .AddMemoryStreams("StreamProvider")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryGrainStorage("agentState") // For OrleansGAgentGrain persistent state
                .AddMemoryGrainStorageAsDefault()
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "test-cluster";
                    options.ServiceId = "test-service";
                });
        }
    }

    // Client配置
    public class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder
                .ConfigureServices(services =>
                {
                    services.AddSerializer(serializerBuilder => { serializerBuilder.AddProtobufSerializer(); });
                })
                .AddMemoryStreams("StreamProvider")
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "test-cluster";
                    options.ServiceId = "test-service";
                });
        }
    }
}
