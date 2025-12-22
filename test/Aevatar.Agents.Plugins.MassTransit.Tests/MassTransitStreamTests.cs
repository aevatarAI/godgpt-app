using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Plugins.MassTransit;
using Google.Protobuf;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Plugins.MassTransit.Tests;

/// <summary>
/// Test implementation of IMassTransitEventHandler that routes events back to local streams.
/// </summary>
internal class TestMassTransitEventHandler : IMassTransitEventHandler
{
    private readonly MassTransitMessageStreamProvider _streamProvider;

    public TestMassTransitEventHandler(MassTransitMessageStreamProvider streamProvider)
    {
        _streamProvider = streamProvider;
    }

    public async Task<bool> HandleEventAsync(string agentId, EventEnvelope envelope)
    {
        // Get the stream for this agent/stream ID
        var stream = _streamProvider.GetStreamInternal(agentId);
        if (stream == null)
        {
            return false;
        }

        // Dispatch to local subscribers
        using var ms = new MemoryStream();
        envelope.WriteTo(ms);
        await stream.DispatchAsync(ms.ToArray());
        return true;
    }
}

public class MassTransitStreamTests
{
    private static ServiceProvider BuildTestProvider()
    {
        return new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<StreamMessageDispatcher>();
            })
            .AddSingleton<MassTransitMessageStreamProvider>()
            .AddSingleton<IMassTransitEventHandler, TestMassTransitEventHandler>()
            .Configure<MassTransitStreamOptions>(options =>
            {
                options.TransportType = MassTransitTransportType.InMemory;
            })
            .BuildServiceProvider(true);
    }

    [Fact]
    public async Task Should_Receive_Message_In_Same_Stream()
    {
        // Arrange
        await using var provider = BuildTestProvider();

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var streamProvider = provider.GetRequiredService<MassTransitMessageStreamProvider>();
        var streamId = Guid.NewGuid().ToString();
        var stream = streamProvider.GetStream(streamId);

        var receivedTcs = new TaskCompletionSource<EventEnvelope>();

        // Act
        await stream.SubscribeAsync<EventEnvelope>(async env =>
        {
            receivedTcs.TrySetResult(env);
            await Task.CompletedTask;
        });

        var testEvent = new EventEnvelope 
        { 
            Id = "test-event-1",
            PublisherId = streamId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await stream.ProduceAsync(testEvent);

        // Assert
        // Wait for message to be consumed and dispatched
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        
        received.ShouldNotBeNull();
        received.Id.ShouldBe(testEvent.Id);
        
        // Verify MassTransit consumption
        (await harness.Consumed.Any<ByteArrayMessage>()).ShouldBeTrue();
    }
    
    [Fact]
    public async Task Should_Not_Receive_Message_From_Different_Stream()
    {
        // Arrange
        await using var provider = BuildTestProvider();

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var streamProvider = provider.GetRequiredService<MassTransitMessageStreamProvider>();
        
        var stream1Id = Guid.NewGuid().ToString();
        var stream2Id = Guid.NewGuid().ToString();
        var stream1 = streamProvider.GetStream(stream1Id);
        var stream2 = streamProvider.GetStream(stream2Id);

        var received1Tcs = new TaskCompletionSource<bool>();
        var received2 = false;

        await stream1.SubscribeAsync<EventEnvelope>(async _ => 
        {
            received1Tcs.TrySetResult(true);
            await Task.CompletedTask;
        });

        await stream2.SubscribeAsync<EventEnvelope>(async _ => 
        {
            received2 = true;
            await Task.CompletedTask;
        });

        var testEvent = new EventEnvelope 
        { 
            Id = "test-event",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        // Act - Produce to Stream 1
        await stream1.ProduceAsync(testEvent);

        // Wait for stream1 to receive message
        var received1 = await received1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Give some time to ensure stream2 didn't receive anything
        await Task.Delay(200);

        // Assert
        received1.ShouldBeTrue();
        received2.ShouldBeFalse();
        
        // Verify MassTransit consumption
        (await harness.Consumed.Any<ByteArrayMessage>()).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Respect_Filter()
    {
        // Arrange
        await using var provider = BuildTestProvider();

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var streamProvider = provider.GetRequiredService<MassTransitMessageStreamProvider>();
        var streamId = Guid.NewGuid().ToString();
        var stream = streamProvider.GetStream(streamId);

        var receivedCount = 0;
        var allProcessedTcs = new TaskCompletionSource<bool>();
        var processedCount = 0;

        await stream.SubscribeAsync<EventEnvelope>(
            async env => 
            {
                Interlocked.Increment(ref receivedCount);
                if (Interlocked.Increment(ref processedCount) >= 1)
                {
                    // We expect only 1 valid message to pass the filter
                    allProcessedTcs.TrySetResult(true);
                }
                await Task.CompletedTask;
            },
            filter: env => env.Id.StartsWith("valid") // Filter logic
        );

        // Act
        await stream.ProduceAsync(new EventEnvelope { Id = "valid-1", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) });
        await stream.ProduceAsync(new EventEnvelope { Id = "invalid-1", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) });

        // Wait for the valid message to be processed
        await allProcessedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        
        // Small delay to ensure invalid message had chance to be processed (it shouldn't be)
        await Task.Delay(200);

        // Assert
        receivedCount.ShouldBe(1);
    }
}
