using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// LumenFeedback State converter
/// </summary>
public class LumenFeedbackStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new LumenFeedbackState();

        var newState = new LumenFeedbackState();

        if (oldState.TryGetValue("FeedbackId", out var feedbackIdObj))
            newState.FeedbackId = ConvertToString(feedbackIdObj);

        if (oldState.TryGetValue("UserId", out var userIdObj))
            newState.UserId = ConvertToString(userIdObj);

        if (oldState.TryGetValue("PredictionId", out var predictionIdObj))
            newState.PredictionId = ConvertToString(predictionIdObj);

        // Method feedbacks map
        if (oldState.TryGetValue("MethodFeedbacks", out var methodFeedbacksObj))
        {
            if (methodFeedbacksObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                {
                    var feedbackDetail = ConvertToFeedbackDetail(prop.Value);
                    if (feedbackDetail != null)
                        newState.MethodFeedbacks[prop.Name] = feedbackDetail;
                }
            }
        }

        return newState;
    }

    private static FeedbackDetail? ConvertToFeedbackDetail(object? obj)
    {
        if (obj == null) return null;
        
        var detail = new FeedbackDetail();
        
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("Rating", out var ratingEl) || je.TryGetProperty("rating", out ratingEl))
                detail.Rating = ConvertToInt32(ratingEl);
            
            if (je.TryGetProperty("Comment", out var commentEl) || je.TryGetProperty("comment", out commentEl))
                detail.Comment = ConvertToString(commentEl);
        }
        
        return detail;
    }

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private static int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }
}
