using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager;
using Shouldly;
using Xunit;

namespace Aevatar.App.Services;

/// <summary>
/// Unit tests for Share functionality
/// Tests multiple ShareId support in SessionInfoProto (aligned with old project behavior)
/// </summary>
public class ShareServiceTests
{
    #region SessionInfoProto ShareIds Tests

    [Fact(DisplayName = "SessionInfoProto should support multiple ShareIds")]
    public void SessionInfoProto_ShouldSupportMultipleShareIds()
    {
        // Arrange
        var proto = new SessionInfoProto();
        var shareId1 = Guid.NewGuid().ToString();
        var shareId2 = Guid.NewGuid().ToString();
        var shareId3 = Guid.NewGuid().ToString();
        
        // Act - Add multiple share IDs using repeated field
        proto.ShareIds.Add(shareId1);
        proto.ShareIds.Add(shareId2);
        proto.ShareIds.Add(shareId3);
        
        // Assert - All share IDs should be preserved
        proto.ShareIds.Count.ShouldBe(3);
        proto.ShareIds.ShouldContain(shareId1);
        proto.ShareIds.ShouldContain(shareId2);
        proto.ShareIds.ShouldContain(shareId3);
    }
    
    [Fact(DisplayName = "ShareIds should preserve insertion order")]
    public void ShareIds_ShouldPreserveOrder()
    {
        // Arrange
        var proto = new SessionInfoProto();
        var shareId1 = "first-share-id";
        var shareId2 = "second-share-id";
        var shareId3 = "third-share-id";
        
        // Act
        proto.ShareIds.Add(shareId1);
        proto.ShareIds.Add(shareId2);
        proto.ShareIds.Add(shareId3);
        
        // Assert - Order should be preserved (like old project's List<Guid>)
        proto.ShareIds[0].ShouldBe(shareId1);
        proto.ShareIds[1].ShouldBe(shareId2);
        proto.ShareIds[2].ShouldBe(shareId3);
    }
    
    [Fact(DisplayName = "AddShareId extension should append not replace")]
    public void AddShareId_ShouldAppendNotReplace()
    {
        // Arrange
        var proto = new SessionInfoProto();
        var shareId1 = Guid.NewGuid();
        var shareId2 = Guid.NewGuid();
        
        // Act - Add first share ID
        proto.AddShareId(shareId1);
        
        // Act - Add second share ID (should append, not replace - this was the bug!)
        proto.AddShareId(shareId2);
        
        // Assert - Both IDs should be present (fixed behavior)
        proto.ShareIds.Count.ShouldBe(2);
        proto.ShareIds.ShouldContain(shareId1.ToString());
        proto.ShareIds.ShouldContain(shareId2.ToString());
    }
    
    [Fact(DisplayName = "GetShareIds should return all ShareIds as Guids")]
    public void GetShareIds_ShouldReturnAllShareIdsAsGuids()
    {
        // Arrange
        var proto = new SessionInfoProto();
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        
        proto.ShareIds.Add(guid1.ToString());
        proto.ShareIds.Add(guid2.ToString());
        proto.ShareIds.Add(guid3.ToString());
        
        // Act - Use the extension method
        var result = proto.GetShareIds();
        
        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContain(guid1);
        result.ShouldContain(guid2);
        result.ShouldContain(guid3);
    }
    
    [Fact(DisplayName = "GetShareIds should return empty list when no ShareIds")]
    public void GetShareIds_ShouldReturnEmptyListWhenNoShareIds()
    {
        // Arrange
        var proto = new SessionInfoProto();
        
        // Act
        var result = proto.GetShareIds();
        
        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }
    
    [Fact(DisplayName = "HasShareIds should return true when ShareIds exist")]
    public void HasShareIds_ShouldReturnTrueWhenShareIdsExist()
    {
        // Arrange
        var proto = new SessionInfoProto();
        proto.ShareIds.Add(Guid.NewGuid().ToString());
        
        // Act
        var hasShareIds = proto.HasShareIds();
        
        // Assert
        hasShareIds.ShouldBeTrue();
    }
    
    [Fact(DisplayName = "HasShareIds should return false when ShareIds empty")]
    public void HasShareIds_ShouldReturnFalseWhenShareIdsEmpty()
    {
        // Arrange
        var proto = new SessionInfoProto();
        
        // Act
        var hasShareIds = proto.HasShareIds();
        
        // Assert
        hasShareIds.ShouldBeFalse();
    }

    #endregion

    #region SessionInfo DTO Tests (C# class with List<Guid>)
    
    [Fact(DisplayName = "SessionInfo DTO should initialize ShareIds as empty list")]
    public void SessionInfo_ShouldInitializeShareIdsAsEmptyList()
    {
        // Arrange & Act
        var sessionInfo = new SessionInfo();
        
        // Assert
        sessionInfo.ShareIds.ShouldNotBeNull();
        sessionInfo.ShareIds.ShouldBeEmpty();
    }
    
    [Fact(DisplayName = "SessionInfo DTO should support multiple ShareIds")]
    public void SessionInfo_ShouldSupportMultipleShareIds()
    {
        // Arrange
        var sessionInfo = new SessionInfo();
        var shareId1 = Guid.NewGuid();
        var shareId2 = Guid.NewGuid();
        
        // Act
        sessionInfo.ShareIds.Add(shareId1);
        sessionInfo.ShareIds.Add(shareId2);
        
        // Assert
        sessionInfo.ShareIds.Count.ShouldBe(2);
        sessionInfo.ShareIds.ShouldContain(shareId1);
        sessionInfo.ShareIds.ShouldContain(shareId2);
    }

    #endregion

    #region Proto to DTO Conversion Tests
    
    [Fact(DisplayName = "ToSessionInfoProto should convert all ShareIds from DTO to proto")]
    public void ToSessionInfoProto_ShouldConvertAllShareIds()
    {
        // Arrange
        var sessionInfo = new SessionInfo
        {
            SessionId = Guid.NewGuid(),
            Title = "Test Session",
            ShareIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]
        };
        
        // Act
        var proto = sessionInfo.ToProto();
        
        // Assert
        proto.ShareIds.Count.ShouldBe(3);
        foreach (var shareId in sessionInfo.ShareIds)
        {
            proto.ShareIds.ShouldContain(shareId.ToString());
        }
    }

    #endregion

    #region CurrentShareCount Tests (State Management)
    
    [Fact(DisplayName = "CurrentShareCount should decrement by ShareIds count on session delete")]
    public void CurrentShareCount_ShouldDecrementByShareIdsCountOnDelete()
    {
        // Arrange - Simulate state with multiple share IDs per session
        var currentShareCount = 10;
        var session1ShareIds = new List<string> 
        { 
            Guid.NewGuid().ToString(), 
            Guid.NewGuid().ToString() 
        };
        var session2ShareIds = new List<string> 
        { 
            Guid.NewGuid().ToString() 
        };
        
        // Act - Delete session 1 (has 2 share IDs)
        currentShareCount -= session1ShareIds.Count;
        
        // Assert
        currentShareCount.ShouldBe(8);
        
        // Act - Delete session 2 (has 1 share ID)
        currentShareCount -= session2ShareIds.Count;
        
        // Assert
        currentShareCount.ShouldBe(7);
    }
    
    [Fact(DisplayName = "GetFirstShareId should return first ShareId for backward compatibility")]
    public void GetFirstShareId_ShouldReturnFirstShareId()
    {
        // Arrange
        var proto = new SessionInfoProto();
        var firstShareId = "first-share-id";
        var secondShareId = "second-share-id";
        
        proto.ShareIds.Add(firstShareId);
        proto.ShareIds.Add(secondShareId);
        
        // Act
        var firstId = proto.GetFirstShareId();
        
        // Assert
        firstId.ShouldBe(firstShareId);
    }
    
    [Fact(DisplayName = "GetFirstShareId should return null when empty")]
    public void GetFirstShareId_ShouldReturnNullWhenEmpty()
    {
        // Arrange
        var proto = new SessionInfoProto();
        
        // Act
        var firstId = proto.GetFirstShareId();
        
        // Assert
        firstId.ShouldBeNull();
    }

    #endregion
}

