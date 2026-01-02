using System.Text.RegularExpressions;
using Aevatar.Agents.Lumen.Prediction.Dictionaries;

namespace Aevatar.Agents.Lumen.Prediction.Services;

/// <summary>
/// Helper methods for translating prediction content
/// Extracted from LumenPredictionGAgent for better maintainability
/// </summary>
public static class TranslationHelpers
{
    #region Yin Yang and Elements

    /// <summary>
    /// Translate Yin/Yang to target language
    /// </summary>
    public static string TranslateYinYang(string yinYang, string language) => language switch
    {
        "zh-tw" or "zh" => yinYang == "Yang" ? "陽" : "陰",
        "es" => yinYang == "Yang" ? "Yang" : "Yin",
        _ => yinYang // English default
    };

    /// <summary>
    /// Translate Five Elements to target language
    /// </summary>
    public static string TranslateElement(string element, string language) => (element, language) switch
    {
        ("Wood", "zh-tw" or "zh") => "木",
        ("Fire", "zh-tw" or "zh") => "火",
        ("Earth", "zh-tw" or "zh") => "土",
        ("Metal", "zh-tw" or "zh") => "金",
        ("Water", "zh-tw" or "zh") => "水",
        ("Wood", "es") => "Madera",
        ("Fire", "es") => "Fuego",
        ("Earth", "es") => "Tierra",
        ("Metal", "es") => "Metal",
        ("Water", "es") => "Agua",
        _ => element // English default
    };

    /// <summary>
    /// Translate Direction to target language
    /// </summary>
    public static string TranslateDirection(string direction, string language) => (direction, language) switch
    {
        ("East 1", "zh-tw" or "zh") => "東一",
        ("East 2", "zh-tw" or "zh") => "東二",
        ("South 1", "zh-tw" or "zh") => "南一",
        ("South 2", "zh-tw" or "zh") => "南二",
        ("West 1", "zh-tw" or "zh") => "西一",
        ("West 2", "zh-tw" or "zh") => "西二",
        ("North 1", "zh-tw" or "zh") => "北一",
        ("North 2", "zh-tw" or "zh") => "北二",
        ("Centre", "zh-tw" or "zh") => "中",
        ("East 1", "es") => "Este 1",
        ("East 2", "es") => "Este 2",
        ("South 1", "es") => "Sur 1",
        ("South 2", "es") => "Sur 2",
        ("West 1", "es") => "Oeste 1",
        ("West 2", "es") => "Oeste 2",
        ("North 1", "es") => "Norte 1",
        ("North 2", "es") => "Norte 2",
        ("Centre", "es") => "Centro",
        _ => direction // English default
    };

    #endregion

    #region Chinese Zodiac

    /// <summary>
    /// Translate Chinese Zodiac animal to target language
    /// </summary>
    public static string TranslateZodiac(string zodiac, string language) => (zodiac, language) switch
    {
        ("Rat", "zh-tw" or "zh") => "鼠",
        ("Ox", "zh-tw" or "zh") => "牛",
        ("Tiger", "zh-tw" or "zh") => "虎",
        ("Rabbit", "zh-tw" or "zh") => "兔",
        ("Dragon", "zh-tw" or "zh") => "龍",
        ("Snake", "zh-tw" or "zh") => "蛇",
        ("Horse", "zh-tw" or "zh") => "馬",
        ("Goat", "zh-tw" or "zh") => "羊",
        ("Monkey", "zh-tw" or "zh") => "猴",
        ("Rooster", "zh-tw" or "zh") => "雞",
        ("Dog", "zh-tw" or "zh") => "狗",
        ("Pig", "zh-tw" or "zh") => "豬",
        ("Rat", "es") => "Rata",
        ("Ox", "es") => "Buey",
        ("Tiger", "es") => "Tigre",
        ("Rabbit", "es") => "Conejo",
        ("Dragon", "es") => "Dragón",
        ("Snake", "es") => "Serpiente",
        ("Horse", "es") => "Caballo",
        ("Goat", "es") => "Cabra",
        ("Monkey", "es") => "Mono",
        ("Rooster", "es") => "Gallo",
        ("Dog", "es") => "Perro",
        ("Pig", "es") => "Cerdo",
        _ => zodiac // English default
    };

    /// <summary>
    /// Translate "Element + Zodiac" format to Chinese
    /// Example: "Wood Snake" → "木蛇"
    /// </summary>
    public static string TranslateZodiacWithElementToChinese(string zodiacWithElement)
    {
        var parts = zodiacWithElement.Split(' ', 2);
        if (parts.Length != 2) return zodiacWithElement;

        var element = parts[0];
        var zodiac = parts[1];

        var elementChinese = element switch
        {
            "Wood" => "木",
            "Fire" => "火",
            "Earth" => "土",
            "Metal" => "金",
            "Water" => "水",
            _ => element
        };

        var zodiacChinese = zodiac switch
        {
            "Rat" => "鼠",
            "Ox" => "牛",
            "Tiger" => "虎",
            "Rabbit" => "兔",
            "Dragon" => "龙",
            "Snake" => "蛇",
            "Horse" => "马",
            "Goat" => "羊",
            "Monkey" => "猴",
            "Rooster" => "鸡",
            "Dog" => "狗",
            "Pig" => "猪",
            _ => zodiac
        };

        return $"{elementChinese}{zodiacChinese}";
    }

    /// <summary>
    /// Translate "Element + Zodiac" format to Spanish
    /// Example: "Wood Snake" → "Serpiente de Madera"
    /// </summary>
    public static string TranslateZodiacWithElementToSpanish(string zodiacWithElement)
    {
        var parts = zodiacWithElement.Split(' ', 2);
        if (parts.Length != 2) return zodiacWithElement;

        var element = parts[0];
        var zodiac = parts[1];

        var elementSpanish = element switch
        {
            "Wood" => "Madera",
            "Fire" => "Fuego",
            "Earth" => "Tierra",
            "Metal" => "Metal",
            "Water" => "Agua",
            _ => element
        };

        var zodiacSpanish = zodiac switch
        {
            "Rat" => "Rata",
            "Ox" => "Buey",
            "Tiger" => "Tigre",
            "Rabbit" => "Conejo",
            "Dragon" => "Dragón",
            "Snake" => "Serpiente",
            "Horse" => "Caballo",
            "Goat" => "Cabra",
            "Monkey" => "Mono",
            "Rooster" => "Gallo",
            "Dog" => "Perro",
            "Pig" => "Cerdo",
            _ => zodiac
        };

        return $"{zodiacSpanish} de {elementSpanish}";
    }

    /// <summary>
    /// Translate Chinese zodiac with element based on language
    /// Input: "Wood Pig"
    /// </summary>
    public static string TranslateChineseZodiacAnimal(string zodiacWithElement, string language)
    {
        if (language == "zh" || language == "zh-tw")
        {
            return TranslateZodiacWithElementToChinese(zodiacWithElement);
        }
        else if (language == "es")
        {
            return TranslateZodiacWithElementToSpanish(zodiacWithElement);
        }
        else
        {
            return zodiacWithElement; // English
        }
    }

    /// <summary>
    /// Translate Chinese Zodiac title (e.g., "The Pig") to different languages
    /// </summary>
    public static string TranslateZodiacTitle(string birthYearAnimal, string language)
    {
        // Extract animal name (e.g., "Wood Pig" -> "Pig")
        var animalName = birthYearAnimal.Split(' ').Last();

        return language switch
        {
            "zh-tw" or "zh" => animalName switch
            {
                "Rat" => "鼠",
                "Ox" => "牛",
                "Tiger" => "虎",
                "Rabbit" => "兔",
                "Dragon" => "龍",
                "Snake" => "蛇",
                "Horse" => "馬",
                "Goat" => "羊",
                "Monkey" => "猴",
                "Rooster" => "雞",
                "Dog" => "狗",
                "Pig" => "豬",
                _ => animalName
            },
            "es" => animalName switch
            {
                "Rat" => "La Rata",
                "Ox" => "El Buey",
                "Tiger" => "El Tigre",
                "Rabbit" => "El Conejo",
                "Dragon" => "El Dragón",
                "Snake" => "La Serpiente",
                "Horse" => "El Caballo",
                "Goat" => "La Cabra",
                "Monkey" => "El Mono",
                "Rooster" => "El Gallo",
                "Dog" => "El Perro",
                "Pig" => "El Cerdo",
                _ => $"El {animalName}"
            },
            _ => $"The {animalName}" // English default
        };
    }

    #endregion

    #region Sun Signs

    /// <summary>
    /// Translate sun sign based on language
    /// </summary>
    public static string TranslateSunSign(string sunSign, string language) => (sunSign, language) switch
    {
        // Chinese translations
        ("Aries", "zh" or "zh-tw") => "白羊座",
        ("Taurus", "zh" or "zh-tw") => "金牛座",
        ("Gemini", "zh" or "zh-tw") => "双子座",
        ("Cancer", "zh" or "zh-tw") => "巨蟹座",
        ("Leo", "zh" or "zh-tw") => "狮子座",
        ("Virgo", "zh" or "zh-tw") => "处女座",
        ("Libra", "zh" or "zh-tw") => "天秤座",
        ("Scorpio", "zh" or "zh-tw") => "天蝎座",
        ("Sagittarius", "zh" or "zh-tw") => "射手座",
        ("Capricorn", "zh" or "zh-tw") => "摩羯座",
        ("Aquarius", "zh" or "zh-tw") => "水瓶座",
        ("Pisces", "zh" or "zh-tw") => "双鱼座",

        // Spanish translations
        ("Aries", "es") => "Aries",
        ("Taurus", "es") => "Tauro",
        ("Gemini", "es") => "Géminis",
        ("Cancer", "es") => "Cáncer",
        ("Leo", "es") => "Leo",
        ("Virgo", "es") => "Virgo",
        ("Libra", "es") => "Libra",
        ("Scorpio", "es") => "Escorpio",
        ("Sagittarius", "es") => "Sagitario",
        ("Capricorn", "es") => "Capricornio",
        ("Aquarius", "es") => "Acuario",
        ("Pisces", "es") => "Piscis",

        // English default
        _ => sunSign
    };

    #endregion

    #region Taishui Relationship

    /// <summary>
    /// Parse taishui relationship string to extract Chinese, Pinyin, and English
    /// Example: "相害 (Xiang Hai - Harm)" → (chinese: "相害", pinyin: "Xiang Hai", english: "Harm", spanish: "Daño")
    /// </summary>
    public static (string Chinese, string Pinyin, string English, string Spanish) ParseTaishuiRelationship(
        string taishui)
    {
        var match = Regex.Match(taishui, @"^(.*?)\s*\((.*?)\s*-\s*(.*?)\)$");
        if (match.Success)
        {
            var chinese = match.Groups[1].Value.Trim();
            var pinyin = match.Groups[2].Value.Trim();
            var english = match.Groups[3].Value.Trim();
            var spanish = TranslateTaishuiToSpanish(english);
            return (chinese, pinyin, english, spanish);
        }

        // Fallback if parsing fails
        return (taishui, "", taishui, taishui);
    }

    /// <summary>
    /// Translate Taishui relationship English term to Spanish
    /// </summary>
    public static string TranslateTaishuiToSpanish(string english) => english switch
    {
        "Birth Year" => "Año de Nacimiento",
        "Harm" => "Daño",
        "Neutral" => "Neutral",
        "Triple Harmony" => "Triple Armonía",
        "Six Harmony" => "Seis Armonía",
        "Clash" => "Choque",
        "Break" => "Ruptura",
        _ => english
    };

    /// <summary>
    /// Translate taishui relationship based on language
    /// Input format: "相害 (Xiang Hai - Harm)"
    /// </summary>
    public static string TranslateTaishuiRelationship(string taishui, string language)
    {
        var parts = ParseTaishuiRelationship(taishui);

        if (language == "zh" || language == "zh-tw")
        {
            // Chinese: only Chinese text
            return parts.Chinese;
        }
        else if (language == "es")
        {
            // Spanish: "Daño (相害 Xiang Hai)"
            return $"{parts.Spanish} ({parts.Chinese} {parts.Pinyin})";
        }
        else
        {
            // English: "Harm (相害 Xiang Hai)"
            return $"{parts.English} ({parts.Chinese} {parts.Pinyin})";
        }
    }

    #endregion

    #region Template Builders

    /// <summary>
    /// Build zodiacInfluence string based on language
    /// Format: "{birthYearZodiac} native in {yearlyYearZodiac} year → {taishuiRelationship}"
    /// </summary>
    public static string BuildZodiacInfluence(
        string birthYearZodiac,
        string yearlyYearZodiac,
        string taishuiRelationship,
        string language)
    {
        // Parse taishui to extract Chinese, Pinyin, and English parts
        var taishuiParts = ParseTaishuiRelationship(taishuiRelationship);

        if (language == "zh" || language == "zh-tw")
        {
            // Chinese: "木蛇生人遇木龙年 → 相害"
            var birthZodiacChinese = TranslateZodiacWithElementToChinese(birthYearZodiac);
            var yearlyZodiacChinese = TranslateZodiacWithElementToChinese(yearlyYearZodiac);
            return $"{birthZodiacChinese}生人遇{yearlyZodiacChinese}年 → {taishuiParts.Chinese}";
        }
        else if (language == "es")
        {
            // Spanish: "Serpiente de Madera nativo en año del Dragón de Madera → Daño (相害 Xiang Hai)"
            var birthZodiacSpanish = TranslateZodiacWithElementToSpanish(birthYearZodiac);
            var yearlyZodiacSpanish = TranslateZodiacWithElementToSpanish(yearlyYearZodiac);
            return
                $"{birthZodiacSpanish} nativo en año del {yearlyZodiacSpanish} → {taishuiParts.Spanish} ({taishuiParts.Chinese} {taishuiParts.Pinyin})";
        }
        else
        {
            // English: "Wood Snake native in Wood Dragon year → Harm (相害 Xiang Hai)"
            return
                $"{birthYearZodiac} native in {yearlyYearZodiac} year → {taishuiParts.English} ({taishuiParts.Chinese} {taishuiParts.Pinyin})";
        }
    }

    /// <summary>
    /// Build path title for daily predictions with localized template
    /// Example: "John's Path Today - A Courageous Path" (en) or "王凯文今日之路 - 勇敢之路" (zh)
    /// </summary>
    public static string BuildPathTitle(string displayName, string pathType, string language)
    {
        if (language == "zh" || language == "zh-tw")
        {
            return $"{displayName}今日之路 - {pathType}之路";
        }
        else if (language == "es")
        {
            return $"Camino de {displayName} Hoy - Un Camino {pathType}";
        }
        else
        {
            return $"{displayName}'s Path Today - A {pathType} Path";
        }
    }

    /// <summary>
    /// Build archetype string for lifetime predictions with localized template
    /// Example: "Sun in Cancer - The Nurturing Protector" (en) or "巨蟹座太阳 - 心灵守护者" (zh)
    /// Removes duplicate articles ("The", "El", etc.) if LLM already included them
    /// </summary>
    public static string BuildArchetypeString(
        string celestialBody,
        string zodiacSign,
        string archetypeName,
        string language)
    {
        // Clean archetype name: remove leading articles if LLM already added them
        var cleanArchName = archetypeName.Trim();

        if (language == "zh" || language == "zh-tw")
        {
            var bodyName = celestialBody switch
            {
                "Sun" => "太阳",
                "Moon" => "月亮",
                "Rising" => "上升",
                _ => celestialBody
            };
            return $"{zodiacSign}{bodyName} - {cleanArchName}";
        }
        else if (language == "es")
        {
            var bodyName = celestialBody switch
            {
                "Sun" => "Sol",
                "Moon" => "Luna",
                "Rising" => "Ascendente",
                _ => celestialBody
            };
            // Remove "El " or "el " if already present
            if (cleanArchName.StartsWith("El ", StringComparison.OrdinalIgnoreCase))
            {
                cleanArchName = cleanArchName.Substring(3).Trim();
            }

            return $"{bodyName} en {zodiacSign} - El {cleanArchName}";
        }
        else
        {
            // Remove "The " or "the " if already present
            if (cleanArchName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                cleanArchName = cleanArchName.Substring(4).Trim();
            }

            return $"{celestialBody} in {zodiacSign} - The {cleanArchName}";
        }
    }

    /// <summary>
    /// Translate cycle age range based on language
    /// Input: "Age 20-29 (1990-1999)" or "Age -10--1 (2015-2024)"
    /// </summary>
    public static string TranslateCycleAgeRange(string ageRange, string language)
    {
        // Extract numbers using regex: support negative ages like "Age -10--1 (2015-2024)"
        var match = Regex.Match(ageRange, @"Age (-?\d+)-(-?\d+) \((\d+)-(\d+)\)");
        if (!match.Success) return ageRange;

        var startAge = match.Groups[1].Value;
        var endAge = match.Groups[2].Value;
        var startYear = match.Groups[3].Value;
        var endYear = match.Groups[4].Value;

        if (language == "zh" || language == "zh-tw")
        {
            return $"{startAge}-{endAge}岁 ({startYear}-{endYear})";
        }
        else if (language == "es")
        {
            return $"Edad {startAge}-{endAge} ({startYear}-{endYear})";
        }
        else
        {
            return ageRange; // English
        }
    }

    /// <summary>
    /// Translate cycle period based on language
    /// Input: "甲子 (Jiǎzǐ) · Wood Rat"
    /// </summary>
    public static string TranslateCyclePeriod(string period, string language)
    {
        // Extract parts: "甲子 (Jiǎzǐ) · Wood Rat"
        var parts = period.Split(" · ", 2);
        if (parts.Length != 2) return period;

        var stemsBranch = parts[0]; // "甲子 (Jiǎzǐ)"
        var zodiacWithElement = parts[1]; // "Wood Rat"

        // For Chinese languages, remove pinyin; keep it for other languages
        if (language == "zh" || language == "zh-tw")
        {
            // Remove pinyin from stems-branch: "甲子 (Jiǎzǐ)" -> "甲子"
            var pinyinStartIndex = stemsBranch.IndexOf(" (");
            if (pinyinStartIndex > 0)
            {
                stemsBranch = stemsBranch.Substring(0, pinyinStartIndex);
            }
        }

        var translatedZodiac = TranslateChineseZodiacAnimal(zodiacWithElement, language);

        return $"{stemsBranch} · {translatedZodiac}";
    }

    #endregion

    #region Chinese Translation Injection

    /// <summary>
    /// Add Chinese translations for English-only fields (tarot card, stone, orientation)
    /// This is called after parsing to replace English values with Chinese translations
    /// </summary>
    public static void AddChineseTranslations(
        Dictionary<string, string> parsedResults,
        string targetLanguage)
    {
        if (targetLanguage != "zh" && targetLanguage != "zh-tw")
        {
            return; // Only add translations for Chinese users
        }

        // Translate tarot card name - REPLACE the original field value
        if (parsedResults.TryGetValue("todaysReading_tarotCard_name", out var cardName) &&
            !string.IsNullOrWhiteSpace(cardName))
        {
            var translatedName = TranslationDictionaries.TranslateTarotCard(cardName.Trim(), targetLanguage);
            if (translatedName != cardName.Trim())
            {
                parsedResults["todaysReading_tarotCard_name"] = translatedName;
            }
        }

        // Translate tarot card orientation - REPLACE the original field value
        if (parsedResults.TryGetValue("todaysReading_tarotCard_orientation", out var orientation) &&
            !string.IsNullOrWhiteSpace(orientation))
        {
            var translatedOrientation =
                TranslationDictionaries.TranslateOrientation(orientation.Trim(), targetLanguage);
            if (translatedOrientation != orientation.Trim())
            {
                parsedResults["todaysReading_tarotCard_orientation"] = translatedOrientation;
            }
        }

        // Translate lucky stone - REPLACE the original field value
        if (parsedResults.TryGetValue("luckyAlignments_luckyStone", out var stone) &&
            !string.IsNullOrWhiteSpace(stone))
        {
            var translatedStone = TranslationDictionaries.TranslateStone(stone.Trim(), targetLanguage);
            if (translatedStone != stone.Trim())
            {
                parsedResults["luckyAlignments_luckyStone"] = translatedStone;
            }
        }
    }

    #endregion
}

