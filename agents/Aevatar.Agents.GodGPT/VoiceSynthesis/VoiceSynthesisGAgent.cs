using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.GodChatStream;
using Aevatar.Agents.GodGPT.Protos.GodChatVoice;
using Aevatar.Application.Grains.Agents.ChatManager;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Agents.ChatManager.VoiceSynthesis;

/// <summary>
/// Dedicated TTS agent.
/// - Consumes VoiceSynthesisJobProto (internal agent-to-agent event).
/// - Produces GodChatStreamEnvelopeProto{audio/control} to the SAME client stream (StreamId + "GodChat").
/// </summary>
public interface IVoiceSynthesisGAgent : IGAgent
{
}

public class VoiceSynthesisGAgent : GAgentBase<VoiceSynthesisStateProto>, IVoiceSynthesisGAgent
{
    // Injected by OrleansGAgentGrain via reflection
    public IServiceProvider ServiceProvider { get; set; } = null!;

    public VoiceSynthesisGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Voice synthesis agent (TTS) for GodChat streaming");
    }

    [EventHandler]
    public async Task HandleVoiceSynthesisJobAsync(VoiceSynthesisJobProto job)
    {
        Logger.LogInformation("[VoiceSynthesisGAgent] Received job - StreamId={StreamId}, ChatId={ChatId}, TextDelta={TextDeltaLen}, TextIsLast={TextIsLast}, VoiceLanguage={VoiceLanguage}",
            job.StreamId, job.ChatId, job.TextDelta?.Length ?? 0, job.TextIsLast, job.VoiceLanguage);
        
        var speechService = ServiceProvider.GetService<ISpeechService>();
        if (speechService == null)
        {
            Logger.LogWarning("[VoiceSynthesisGAgent] ISpeechService not available, skip TTS - StreamId={StreamId}", job.StreamId);
            return;
        }

        if (string.IsNullOrWhiteSpace(job.StreamId))
        {
            Logger.LogWarning("[VoiceSynthesisGAgent] Empty StreamId, skip TTS");
            return;
        }

        var streamState = GetOrCreateStreamState(job.StreamId);
        streamState.LastTextSeq = job.TextSeq;

        // Append delta
        if (!string.IsNullOrEmpty(job.TextDelta))
        {
            streamState.PendingText += job.TextDelta;
        }

        // Extract & synthesize complete sentences
        var emittedAny = false;
        var pending = streamState.PendingText ?? string.Empty;
        while (TryDequeueSentence(ref pending, out var sentence))
        {
            emittedAny = true;
            await SynthesizeAndPublishSentenceAsync(speechService, job, streamState, sentence, isLastAudioChunk: false);
            streamState.NextSentenceIndex++;
        }
        streamState.PendingText = pending;

        // If text is completed, flush remaining text as last sentence (even without punctuation)
        if (job.TextIsLast)
        {
            if (!string.IsNullOrWhiteSpace(streamState.PendingText))
            {
                var tail = streamState.PendingText.Trim();
                streamState.PendingText = string.Empty;
                await SynthesizeAndPublishSentenceAsync(speechService, job, streamState, tail, isLastAudioChunk: true);
                streamState.NextSentenceIndex++;
                emittedAny = true;
            }

            // Send AllCompleted after all audio chunks are done
            // AIAgentStatusProxy sends TextCompleted for voice chat; we own the final AllCompleted
            Logger.LogInformation("[VoiceSynthesisGAgent] All audio done, sending AllCompleted - StreamId={StreamId}, ChatId={ChatId}, SentencesEmitted={SentencesEmitted}",
                job.StreamId, job.ChatId, streamState.NextSentenceIndex);
            await PublishControlAsync(job, ControlProto.Types.ControlType.AllCompleted, "all", "", 0);

            // Cleanup state to avoid unbounded growth
            State.Streams.Remove(job.StreamId);
        }
        else
        {
            // Persist updated state for long streams; small cost, but keeps robustness across activations.
            // (Optional; can be removed later if state persistence becomes too chatty.)
            if (emittedAny)
            {
                State.Streams[job.StreamId] = streamState;
            }
        }
    }

    private VoiceSynthesisStreamStateProto GetOrCreateStreamState(string streamId)
    {
        if (State.Streams.TryGetValue(streamId, out var existing))
        {
            return existing;
        }

        var created = new VoiceSynthesisStreamStateProto
        {
            PendingText = string.Empty,
            NextSentenceIndex = 0,
            LastTextSeq = 0
        };
        State.Streams[streamId] = created;
        return created;
    }

    private static bool TryDequeueSentence(ref string pending, out string sentence)
    {
        sentence = string.Empty;
        if (string.IsNullOrEmpty(pending))
        {
            return false;
        }

        // Very simple sentence boundary detection.
        // We can improve later (CJK punctuation, abbreviations, etc.).
        var idx = pending.IndexOfAny(['.', '!', '?', '。', '！', '？', '\n']);
        if (idx < 0)
        {
            return false;
        }

        // Include the delimiter
        idx += 1;
        sentence = pending[..idx].Trim();
        pending = pending[idx..];
        return !string.IsNullOrWhiteSpace(sentence);
    }

    private async Task SynthesizeAndPublishSentenceAsync(
        ISpeechService speechService,
        VoiceSynthesisJobProto job,
        VoiceSynthesisStreamStateProto streamState,
        string text,
        bool isLastAudioChunk)
    {
        var voiceLanguage = VoiceLanguageEnum.English;
        if (System.Enum.IsDefined(typeof(VoiceLanguageEnum), job.VoiceLanguage))
        {
            voiceLanguage = (VoiceLanguageEnum)job.VoiceLanguage;
        }

        try
        {
            Logger.LogInformation(
                "[VoiceSynthesisGAgent] TTS start - StreamId={StreamId}, ChatId={ChatId}, SentenceIndex={SentenceIndex}, TextLen={Len}",
                job.StreamId, job.ChatId, streamState.NextSentenceIndex, text.Length);

            var (audioData, metadata) = await speechService.TextToSpeechWithMetadataAsync(text, voiceLanguage);

            var audioChunk = new AudioChunkProto
            {
                AudioData = ByteString.CopyFrom(audioData),
                AudioMetadataJson = JsonConvert.SerializeObject(metadata),
                TextSeqRef = streamState.LastTextSeq,
                SentenceIndex = (int)streamState.NextSentenceIndex,
                AudioChunkId = Guid.NewGuid().ToString("N"),
                IsLast = isLastAudioChunk
            };

            await PublishAudioAsync(job, audioChunk);
        }
        catch (Exception ex)
        {
            // Per spec: audio failure should not break text streaming.
            Logger.LogError(
                ex,
                "[VoiceSynthesisGAgent] TTS failed - StreamId={StreamId}, ChatId={ChatId}, SentenceIndex={SentenceIndex}",
                job.StreamId, job.ChatId, streamState.NextSentenceIndex);

            // Do NOT emit Control(ERROR) here (it would close SSE).
            // We can add a non-terminal "metadata" signal later if needed.
        }
    }

    private async Task PublishAudioAsync(VoiceSynthesisJobProto job, AudioChunkProto audio)
    {
        Logger.LogInformation("[VoiceSynthesisGAgent] PublishAudio - StreamId={StreamId}, ChatId={ChatId}, AudioDataLen={AudioDataLen}, AudioChunkId={AudioChunkId}, IsLast={IsLast}",
            job.StreamId, job.ChatId, audio.AudioData?.Length ?? 0, audio.AudioChunkId, audio.IsLast);
        
        var envelope = new GodChatStreamEnvelopeProto
        {
            StreamId = job.StreamId,
            ChatId = job.ChatId,
            RequestId = job.RequestId,
            // NOTE: seq is not a global monotonic across multi-producer yet.
            // We keep it aligned to the last text seq to preserve compatibility with existing clients.
            Seq = job.TextSeq,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Audio = audio
        };

        await ProduceToClientStreamAsync(job.StreamId, envelope);
    }

    private async Task PublishControlAsync(
        VoiceSynthesisJobProto job,
        ControlProto.Types.ControlType type,
        string scope,
        string message,
        int errorCode)
    {
        var envelope = new GodChatStreamEnvelopeProto
        {
            StreamId = job.StreamId,
            ChatId = job.ChatId,
            RequestId = job.RequestId,
            Seq = job.TextSeq,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Control = new ControlProto
            {
                Type = type,
                Scope = scope,
                Message = message,
                ErrorCode = errorCode
            }
        };

        await ProduceToClientStreamAsync(job.StreamId, envelope);
    }

    private async Task ProduceToClientStreamAsync(string streamId, GodChatStreamEnvelopeProto payload)
    {
        var messageStreamProvider = ServiceProvider.GetService<IMessageStreamProvider>();
        if (messageStreamProvider == null)
        {
            Logger.LogWarning("[VoiceSynthesisGAgent] IMessageStreamProvider not available, cannot publish audio - StreamId={StreamId}", streamId);
            return;
        }

        var stream = messageStreamProvider.GetStream(streamId, "GodChat");
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Version = 0,
            Payload = Any.Pack(payload)
        };

        await stream.ProduceAsync(envelope, CancellationToken.None);
    }
}


