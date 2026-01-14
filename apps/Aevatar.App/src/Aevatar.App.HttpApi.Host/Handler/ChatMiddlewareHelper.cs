using System;
using System.Linq;
using Aevatar.Agents.GodGPT.Protos.GodChatStream;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;

namespace Aevatar.App.HttpApi.Host.Handler;

/// <summary>
/// Constants and helper methods for ChatMiddleware.
/// </summary>
public static class ChatMiddlewareHelper
{
    public const int MaxImageCount = 10;
    public const string CNDefaultRegion = "CN";
    public const string DefaultRegion = "DEFAULT";
    public const string CNConsoleRegion = "CNCONSOLE";
    public const string ConsoleRegion = "CONSOLE";

    /// <summary>
    /// Resolves region based on CN status and input region.
    /// </summary>
    public static string ResolveRegion(string? inputRegion, bool isCN)
    {
        if (string.IsNullOrWhiteSpace(inputRegion))
        {
            return isCN ? CNDefaultRegion : DefaultRegion;
        }
        
        if (inputRegion.Equals(ConsoleRegion, StringComparison.OrdinalIgnoreCase) && isCN)
        {
            return CNConsoleRegion;
        }
        
        return inputRegion;
    }

    /// <summary>
    /// Maps GodChatStreamEnvelopeProto to HTTP response DTO.
    /// </summary>
    public static ResponseStreamGodChatForHttp MapEnvelopeToHttpResponse(GodChatStreamEnvelopeProto envelope)
    {
        var sessionId = Guid.TryParse(envelope.StreamId, out var parsed) ? parsed : Guid.Empty;

        var http = new ResponseStreamGodChatForHttp
        {
            ResponseType = ResponseType.ChatResponse,
            ChatId = envelope.ChatId ?? string.Empty,
            SessionId = sessionId,
            SerialNumber = (int)Math.Min(int.MaxValue, Math.Max(0, envelope.Seq)),
            SerialChunk = 0,
            ErrorCode = ChatErrorCode.Success,
            VoiceContentType = VoiceContentType.VoiceResponse
        };

        switch (envelope.PayloadCase)
        {
            case GodChatStreamEnvelopeProto.PayloadOneofCase.Text:
                http.Response = envelope.Text?.Content ?? string.Empty;
                http.IsLastChunk = envelope.Text?.IsLast ?? false;
                http.AudioData = null;
                http.AudioMetadata = null;
                // Read VoiceContentType from proto: 0=VoiceToText, 1=VoiceResponse
                // This is critical for voice chat to distinguish STT result from AI response
                if (envelope.Text != null && Enum.IsDefined(typeof(VoiceContentType), envelope.Text.VoiceContentType))
                {
                    http.VoiceContentType = (VoiceContentType)envelope.Text.VoiceContentType;
                }
                // Map suggested items from proto to HTTP response
                if (envelope.Text?.SuggestedItems != null && envelope.Text.SuggestedItems.Count > 0)
                {
                    http.SuggestedItems = envelope.Text.SuggestedItems.ToList();
                }
                return http;

            case GodChatStreamEnvelopeProto.PayloadOneofCase.Audio:
                http.Response = string.Empty;
                http.IsLastChunk = envelope.Audio?.IsLast ?? false;
                http.AudioData = envelope.Audio?.AudioData?.ToByteArray();
                http.AudioMetadata = null;
                http.VoiceContentType = VoiceContentType.VoiceResponse;
                return http;

            case GodChatStreamEnvelopeProto.PayloadOneofCase.Control:
                http.Response = string.Empty;
                http.IsLastChunk = envelope.Control?.Type == ControlProto.Types.ControlType.AllCompleted
                                  || envelope.Control?.Type == ControlProto.Types.ControlType.Error;
                if (envelope.Control?.Type == ControlProto.Types.ControlType.Error)
                {
                    if (Enum.IsDefined(typeof(ChatErrorCode), envelope.Control.ErrorCode))
                    {
                        http.ErrorCode = (ChatErrorCode)envelope.Control.ErrorCode;
                    }
                    else
                    {
                        http.ErrorCode = ChatErrorCode.ParamInvalid;
                    }
                    http.Response = envelope.Control?.Message ?? string.Empty;
                }
                return http;

            default:
                http.Response = string.Empty;
                http.IsLastChunk = false;
                return http;
        }
    }
}

