using System;
using System.Collections.Generic;
using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.App.Lumen.Dtos;

/// <summary>
/// API result for getting prediction history
/// </summary>
public class GetPredictionHistoryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<HistoryPredictionResultDto> Predictions { get; set; } = new();
}

/// <summary>
/// API result for getting calculated values (astrology, etc.)
/// </summary>
public class GetCalculatedValuesResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    
    // Western Astrology
    public string? SunSign { get; set; }
    public string? MoonSign { get; set; }
    public string? RisingSign { get; set; }
    public ZodiacSignEnum SunSignEnum { get; set; }
    public ZodiacSignEnum? MoonSignEnum { get; set; }
    public ZodiacSignEnum? RisingSignEnum { get; set; }
    
    // Chinese Astrology
    public string? ChineseZodiac { get; set; }
    public ChineseZodiacEnum ChineseZodiacEnum { get; set; }
    public string? Element { get; set; }
    public string? YinYang { get; set; }
    
    // Four Pillars
    public string? YearPillar { get; set; }
    public string? MonthPillar { get; set; }
    public string? DayPillar { get; set; }
    public string? HourPillar { get; set; }
    
    // Lucky values
    public int? LuckyNumber { get; set; }
    public string? LuckyColor { get; set; }
    public string? LuckyDirection { get; set; }
}

/// <summary>
/// API result for getting prediction generation status
/// </summary>
public class GetPredictionStatusResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    
    public PredictionStatusInfo? Daily { get; set; }
    public PredictionStatusInfo? Yearly { get; set; }
    public PredictionStatusInfo? Lifetime { get; set; }
    
    public bool IsGenerating { get; set; }
    public List<string> GeneratingTypes { get; set; } = new();
}

/// <summary>
/// Status info for a single prediction type
/// </summary>
public class PredictionStatusInfo
{
    public bool Exists { get; set; }
    public bool IsGenerating { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public string? SourceLanguage { get; set; }
    public List<string> AvailableLanguages { get; set; } = new();
    public bool IsTranslating { get; set; }
    public List<string> TranslatingLanguages { get; set; } = new();
}

