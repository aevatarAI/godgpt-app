using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orleans.Streams;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests.Stream;

/// <summary>
/// Unit tests for OrleansStreamFactory
/// Tests the unified stream creation logic for Orleans and MassTransit
/// </summary>
public class OrleansStreamFactoryTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly ILogger<OrleansStreamFactory> _logger;

    public OrleansStreamFactoryTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _logger = NullLogger<OrleansStreamFactory>.Instance;
    }

    #region RequiresCategoryForStream Tests

    [Fact]
    public void RequiresCategoryForStream_Should_Return_False_When_Provider_Is_Default()
    {
        // Arrange
        var options = Options.Create(new MessageStreamProviderOptions { Provider = "Default" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = factory.RequiresCategoryForStream();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RequiresCategoryForStream_Should_Return_False_When_Provider_Is_Orleans()
    {
        // Arrange
        var options = Options.Create(new MessageStreamProviderOptions { Provider = "Orleans" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = factory.RequiresCategoryForStream();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RequiresCategoryForStream_Should_Return_True_When_Provider_Is_MassTransit()
    {
        // Arrange
        var options = Options.Create(new MessageStreamProviderOptions { Provider = "MassTransit" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = factory.RequiresCategoryForStream();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RequiresCategoryForStream_Should_Use_Runtime_Override_When_Present()
    {
        // Arrange - Default is Orleans, but Orleans runtime override is MassTransit
        var options = Options.Create(new MessageStreamProviderOptions
        {
            Provider = "Orleans",
            Runtime = new Dictionary<string, string> { { "Orleans", "MassTransit" } }
        });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = factory.RequiresCategoryForStream();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RequiresCategoryForStream_Should_Return_False_When_Options_Is_Null()
    {
        // Arrange
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            providerOptions: null);

        // Act
        var result = factory.RequiresCategoryForStream();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DetermineProviderType Tests

    [Fact]
    public void DetermineProviderType_Should_Return_Default_When_No_Options()
    {
        // Arrange
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            providerOptions: null);

        // Act
        var result = factory.DetermineProviderType();

        // Assert
        Assert.Equal("Default", result);
    }

    [Fact]
    public void DetermineProviderType_Should_Return_Configured_Provider()
    {
        // Arrange
        var options = Options.Create(new MessageStreamProviderOptions { Provider = "CustomProvider" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = factory.DetermineProviderType();

        // Assert
        Assert.Equal("CustomProvider", result);
    }

    [Fact]
    public void DetermineProviderType_Should_Prefer_Runtime_Override()
    {
        // Arrange
        var options = Options.Create(new MessageStreamProviderOptions
        {
            Provider = "DefaultProvider",
            Runtime = new Dictionary<string, string> { { "Orleans", "RuntimeProvider" } }
        });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = factory.DetermineProviderType();

        // Assert
        Assert.Equal("RuntimeProvider", result);
    }

    #endregion

    #region CreateStreamAsync Tests

    [Fact]
    public async Task CreateStreamAsync_Should_Create_MassTransit_Stream_When_Configured()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var agentCategory = "TestAgent";

        var mockStream = new Mock<IMessageStream>();
        mockStream.Setup(s => s.StreamId).Returns(agentId);

        var mockStreamProvider = new Mock<IMessageStreamProvider>();
        mockStreamProvider
            .Setup(p => p.GetStream(agentId, agentCategory))
            .Returns(mockStream.Object);

        var options = Options.Create(new MessageStreamProviderOptions { Provider = "MassTransit" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options,
            mockStreamProvider.Object);

        // Act
        var result = await factory.CreateStreamAsync(agentId, agentCategory, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(agentId, result.StreamId);
        mockStreamProvider.Verify(p => p.GetStream(agentId, agentCategory), Times.Once);
    }

    [Fact]
    public async Task CreateStreamAsync_Should_Throw_When_Orleans_And_No_StreamProvider()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var options = Options.Create(new MessageStreamProviderOptions { Provider = "Orleans" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateStreamAsync(agentId, null, null));
    }

    [Fact]
    public async Task CreateStreamAsync_Should_Create_Orleans_Stream_When_StreamProvider_Available()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();

        var mockOrleansStream = new Mock<IAsyncStream<byte[]>>();
        var mockOrleansStreamProvider = new Mock<IStreamProvider>();
        mockOrleansStreamProvider
            .Setup(p => p.GetStream<byte[]>(It.IsAny<StreamId>()))
            .Returns(mockOrleansStream.Object);

        var options = Options.Create(new MessageStreamProviderOptions { Provider = "Orleans" });
        var factory = new OrleansStreamFactory(
            _serviceProviderMock.Object,
            _logger,
            options);

        // Act
        var result = await factory.CreateStreamAsync(
            agentId,
            null,
            (name) => mockOrleansStreamProvider.Object);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<OrleansMessageStream>(result);
        Assert.Equal(agentId, result.StreamId);
    }

    #endregion
}

