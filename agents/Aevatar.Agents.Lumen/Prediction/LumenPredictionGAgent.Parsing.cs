using Microsoft.Extensions.Logging;
using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// LumenPredictionGAgent - Parsing methods for enum conversion
/// Uses Protobuf-generated enum types from lumen_common.proto
/// </summary>
public partial class LumenPredictionGAgent
{
    #region Tarot Card Parsing

    /// <summary>
    /// Parse tarot card name to enum
    /// Supports English, Chinese Simplified, and Chinese Traditional
    /// </summary>
    private TarotCardEnum ParseTarotCard(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return TarotCardEnum.TarotUnknown;

        // Try Chinese mapping first (including common aliases)
        var chineseMapping = new Dictionary<string, TarotCardEnum>(StringComparer.OrdinalIgnoreCase)
        {
            // Major Arcana
            { "愚者", TarotCardEnum.TarotTheFool },
            { "魔术师", TarotCardEnum.TarotTheMagician },
            { "女祭司", TarotCardEnum.TarotTheHighPriestess },
            { "皇后", TarotCardEnum.TarotTheEmpress },
            { "皇帝", TarotCardEnum.TarotTheEmperor },
            { "教皇", TarotCardEnum.TarotTheHierophant },
            { "恋人", TarotCardEnum.TarotTheLovers },
            { "战车", TarotCardEnum.TarotTheChariot },
            { "力量", TarotCardEnum.TarotStrength },
            { "隐士", TarotCardEnum.TarotTheHermit },
            { "命运之轮", TarotCardEnum.TarotWheelOfFortune },
            { "正义", TarotCardEnum.TarotJustice },
            { "倒吊人", TarotCardEnum.TarotTheHangedMan },
            { "倒吊者", TarotCardEnum.TarotTheHangedMan },
            { "倒悬者", TarotCardEnum.TarotTheHangedMan },
            { "死神", TarotCardEnum.TarotDeath },
            { "死亡", TarotCardEnum.TarotDeath },
            { "节制", TarotCardEnum.TarotTemperance },
            { "恶魔", TarotCardEnum.TarotTheDevil },
            { "魔鬼", TarotCardEnum.TarotTheDevil },
            { "高塔", TarotCardEnum.TarotTheTower },
            { "塔", TarotCardEnum.TarotTheTower },
            { "星星", TarotCardEnum.TarotTheStar },
            { "星辰", TarotCardEnum.TarotTheStar },
            { "月亮", TarotCardEnum.TarotTheMoon },
            { "月", TarotCardEnum.TarotTheMoon },
            { "太阳", TarotCardEnum.TarotTheSun },
            { "日", TarotCardEnum.TarotTheSun },
            { "审判", TarotCardEnum.TarotJudgement },
            { "审讯", TarotCardEnum.TarotJudgement },
            { "世界", TarotCardEnum.TarotTheWorld },

            // Wands
            { "权杖王牌", TarotCardEnum.TarotAceOfWands },
            { "权杖二", TarotCardEnum.TarotTwoOfWands },
            { "权杖三", TarotCardEnum.TarotThreeOfWands },
            { "权杖四", TarotCardEnum.TarotFourOfWands },
            { "权杖五", TarotCardEnum.TarotFiveOfWands },
            { "权杖六", TarotCardEnum.TarotSixOfWands },
            { "权杖七", TarotCardEnum.TarotSevenOfWands },
            { "权杖八", TarotCardEnum.TarotEightOfWands },
            { "权杖九", TarotCardEnum.TarotNineOfWands },
            { "权杖十", TarotCardEnum.TarotTenOfWands },
            { "权杖侍从", TarotCardEnum.TarotPageOfWands },
            { "权杖骑士", TarotCardEnum.TarotKnightOfWands },
            { "权杖王后", TarotCardEnum.TarotQueenOfWands },
            { "权杖国王", TarotCardEnum.TarotKingOfWands },

            // Cups
            { "圣杯王牌", TarotCardEnum.TarotAceOfCups },
            { "圣杯二", TarotCardEnum.TarotTwoOfCups },
            { "圣杯三", TarotCardEnum.TarotThreeOfCups },
            { "圣杯四", TarotCardEnum.TarotFourOfCups },
            { "圣杯五", TarotCardEnum.TarotFiveOfCups },
            { "圣杯六", TarotCardEnum.TarotSixOfCups },
            { "圣杯七", TarotCardEnum.TarotSevenOfCups },
            { "圣杯八", TarotCardEnum.TarotEightOfCups },
            { "圣杯九", TarotCardEnum.TarotNineOfCups },
            { "圣杯十", TarotCardEnum.TarotTenOfCups },
            { "圣杯侍从", TarotCardEnum.TarotPageOfCups },
            { "圣杯骑士", TarotCardEnum.TarotKnightOfCups },
            { "圣杯王后", TarotCardEnum.TarotQueenOfCups },
            { "圣杯国王", TarotCardEnum.TarotKingOfCups },

            // Swords
            { "宝剑王牌", TarotCardEnum.TarotAceOfSwords },
            { "宝剑二", TarotCardEnum.TarotTwoOfSwords },
            { "宝剑三", TarotCardEnum.TarotThreeOfSwords },
            { "宝剑四", TarotCardEnum.TarotFourOfSwords },
            { "宝剑五", TarotCardEnum.TarotFiveOfSwords },
            { "宝剑六", TarotCardEnum.TarotSixOfSwords },
            { "宝剑七", TarotCardEnum.TarotSevenOfSwords },
            { "宝剑八", TarotCardEnum.TarotEightOfSwords },
            { "宝剑九", TarotCardEnum.TarotNineOfSwords },
            { "宝剑十", TarotCardEnum.TarotTenOfSwords },
            { "宝剑侍从", TarotCardEnum.TarotPageOfSwords },
            { "宝剑骑士", TarotCardEnum.TarotKnightOfSwords },
            { "宝剑王后", TarotCardEnum.TarotQueenOfSwords },
            { "宝剑国王", TarotCardEnum.TarotKingOfSwords },

            // Pentacles (Chinese uses 星币)
            { "星币王牌", TarotCardEnum.TarotAceOfPentacles },
            { "星币二", TarotCardEnum.TarotTwoOfPentacles },
            { "星币三", TarotCardEnum.TarotThreeOfPentacles },
            { "星币四", TarotCardEnum.TarotFourOfPentacles },
            { "星币五", TarotCardEnum.TarotFiveOfPentacles },
            { "星币六", TarotCardEnum.TarotSixOfPentacles },
            { "星币七", TarotCardEnum.TarotSevenOfPentacles },
            { "星币八", TarotCardEnum.TarotEightOfPentacles },
            { "星币九", TarotCardEnum.TarotNineOfPentacles },
            { "星币十", TarotCardEnum.TarotTenOfPentacles },
            { "星币侍从", TarotCardEnum.TarotPageOfPentacles },
            { "星币骑士", TarotCardEnum.TarotKnightOfPentacles },
            { "星币王后", TarotCardEnum.TarotQueenOfPentacles },
            { "星币国王", TarotCardEnum.TarotKingOfPentacles }
        };

        var trimmedName = cardName.Trim();

        if (chineseMapping.TryGetValue(trimmedName, out var chineseResult))
        {
            return chineseResult;
        }

        // Try common Chinese aliases with normalization (for court cards)
        var normalizedChinese = trimmedName;

        if (trimmedName.Contains("权杖") || trimmedName.Contains("圣杯") ||
            trimmedName.Contains("宝剑") || trimmedName.Contains("星币"))
        {
            normalizedChinese = trimmedName
                .Replace("侍者", "侍从")
                .Replace("随从", "侍从")
                .Replace("武士", "骑士")
                .Replace("女王", "王后");
        }

        if (normalizedChinese != trimmedName &&
            chineseMapping.TryGetValue(normalizedChinese, out var normalizedResult))
        {
            Logger.LogDebug(
                "[LumenPrediction][ParseTarotCard] Normalized '{Original}' to '{Normalized}'",
                trimmedName, normalizedChinese);
            return normalizedResult;
        }

        // Try English parsing with normalization
        // Protobuf enum values are like "TarotTheFool" so we need to add "Tarot" prefix
        var normalized = "Tarot" + cardName.Trim()
            .Replace(" of ", "Of") // "Ace of Wands" → "AceOfWands"
            .Replace(" ", ""); // Remove all spaces: "The Fool" → "TheFool"

        if (Enum.TryParse<TarotCardEnum>(normalized, true, out var result))
        {
            return result;
        }

        Logger.LogWarning(
            "[LumenPrediction][ParseTarotCard] Unknown tarot card: {CardName}, normalized: {Normalized}",
            cardName, normalized);
        return TarotCardEnum.TarotUnknown;
    }

    #endregion

    #region Tarot Orientation Parsing

    /// <summary>
    /// Parse tarot card orientation to enum
    /// </summary>
    private TarotOrientationEnum ParseTarotOrientation(string orientation)
    {
        if (string.IsNullOrWhiteSpace(orientation)) return TarotOrientationEnum.OrientationUnknown;

        var normalized = orientation.Trim().ToLowerInvariant();

        return normalized switch
        {
            // English
            "upright" => TarotOrientationEnum.OrientationUpright,
            "reversed" => TarotOrientationEnum.OrientationReversed,
            // Chinese
            "正位" => TarotOrientationEnum.OrientationUpright,
            "逆位" => TarotOrientationEnum.OrientationReversed,
            // Spanish
            "derecha" => TarotOrientationEnum.OrientationUpright,
            "invertida" => TarotOrientationEnum.OrientationReversed,
            _ => TarotOrientationEnum.OrientationUnknown
        };
    }

    #endregion

    #region Zodiac Sign Parsing

    /// <summary>
    /// Parse zodiac sign name to enum
    /// </summary>
    private ZodiacSignEnum ParseZodiacSign(string signName)
    {
        if (string.IsNullOrWhiteSpace(signName)) return ZodiacSignEnum.ZodiacUnknown;

        // Protobuf enum values are prefixed with "Zodiac"
        var normalized = "Zodiac" + signName.Trim().Replace(" ", "");

        if (Enum.TryParse<ZodiacSignEnum>(normalized, true, out var result))
        {
            return result;
        }

        Logger.LogWarning("[LumenPrediction][ParseZodiacSign] Unknown zodiac sign: {SignName}", signName);
        return ZodiacSignEnum.ZodiacUnknown;
    }

    /// <summary>
    /// Parse chinese zodiac animal to enum
    /// </summary>
    private ChineseZodiacEnum ParseChineseZodiac(string animalName)
    {
        if (string.IsNullOrWhiteSpace(animalName)) return ChineseZodiacEnum.ChineseZodiacUnknown;

        // Remove "The " prefix for matching
        var normalized = "ChineseZodiac" + animalName.Replace("The ", "").Trim().Replace(" ", "");

        if (Enum.TryParse<ChineseZodiacEnum>(normalized, true, out var result))
        {
            return result;
        }

        Logger.LogWarning(
            "[LumenPrediction][ParseChineseZodiac] Unknown chinese zodiac: {AnimalName}",
            animalName);
        return ChineseZodiacEnum.ChineseZodiacUnknown;
    }

    #endregion

    #region Crystal Stone Parsing

    /// <summary>
    /// Parse crystal stone name to enum
    /// </summary>
    private CrystalStoneEnum ParseCrystalStone(string stoneName)
    {
        if (string.IsNullOrWhiteSpace(stoneName)) return CrystalStoneEnum.CrystalUnknown;

        // Remove spaces and special chars for matching
        var normalized = stoneName.Replace(" ", "").Replace("'", "").Replace("-", "").Trim();

        // Handle special cases including multilingual support
        var specialCases = new Dictionary<string, CrystalStoneEnum>(StringComparer.OrdinalIgnoreCase)
        {
            // English special cases
            { "RoseQuartz", CrystalStoneEnum.CrystalRoseQuartz },
            { "ClearQuartz", CrystalStoneEnum.CrystalClearQuartz },
            { "SmokyQuartz", CrystalStoneEnum.CrystalSmokyQuartz },
            { "BlackTourmaline", CrystalStoneEnum.CrystalBlackTourmaline },
            { "TigersEye", CrystalStoneEnum.CrystalTigersEye },
            { "Tiger'sEye", CrystalStoneEnum.CrystalTigersEye },
            { "TigerEye", CrystalStoneEnum.CrystalTigersEye },
            { "LapisLazuli", CrystalStoneEnum.CrystalLapisLazuli },
            { "Lapis", CrystalStoneEnum.CrystalLapisLazuli },

            // Chinese mappings
            { "紫水晶", CrystalStoneEnum.CrystalAmethyst },
            { "粉晶", CrystalStoneEnum.CrystalRoseQuartz },
            { "芙蓉石", CrystalStoneEnum.CrystalRoseQuartz },
            { "白水晶", CrystalStoneEnum.CrystalClearQuartz },
            { "黄水晶", CrystalStoneEnum.CrystalCitrine },
            { "茶晶", CrystalStoneEnum.CrystalSmokyQuartz },
            { "黑碧玺", CrystalStoneEnum.CrystalBlackTourmaline },
            { "透石膏", CrystalStoneEnum.CrystalSelenite },
            { "拉长石", CrystalStoneEnum.CrystalLabradorite },
            { "月光石", CrystalStoneEnum.CrystalMoonstone },
            { "红玛瑙", CrystalStoneEnum.CrystalCarnelian },
            { "虎眼石", CrystalStoneEnum.CrystalTigersEye },
            { "玉", CrystalStoneEnum.CrystalJade },
            { "绿松石", CrystalStoneEnum.CrystalTurquoise },
            { "青金石", CrystalStoneEnum.CrystalLapisLazuli },
            { "海蓝宝", CrystalStoneEnum.CrystalAquamarine },
            { "祖母绿", CrystalStoneEnum.CrystalEmerald },
            { "红宝石", CrystalStoneEnum.CrystalRuby },
            { "蓝宝石", CrystalStoneEnum.CrystalSapphire },
            { "石榴石", CrystalStoneEnum.CrystalGarnet },
            { "蛋白石", CrystalStoneEnum.CrystalOpal },
            { "黄玉", CrystalStoneEnum.CrystalTopaz },
            { "橄榄石", CrystalStoneEnum.CrystalPeridot },
            { "黑曜石", CrystalStoneEnum.CrystalObsidian },
            { "孔雀石", CrystalStoneEnum.CrystalMalachite },
            { "赤铁矿", CrystalStoneEnum.CrystalHematite },
            { "黄铁矿", CrystalStoneEnum.CrystalPyrite },
            { "萤石", CrystalStoneEnum.CrystalFluorite },
            { "东陵玉", CrystalStoneEnum.CrystalAventurine },
            { "碧玉", CrystalStoneEnum.CrystalJasper },
            { "玛瑙", CrystalStoneEnum.CrystalAgate },
            { "血石", CrystalStoneEnum.CrystalBloodstone },
            { "黑玛瑙", CrystalStoneEnum.CrystalOnyx },
            { "菱镁矿", CrystalStoneEnum.CrystalHowlite },
            { "天河石", CrystalStoneEnum.CrystalAmazonite }
        };

        if (specialCases.TryGetValue(normalized, out var specialResult))
        {
            return specialResult;
        }

        // Try with "Crystal" prefix for Protobuf enum values
        var prefixedName = "Crystal" + normalized;
        if (Enum.TryParse<CrystalStoneEnum>(prefixedName, true, out var result))
        {
            return result;
        }

        // Fallback: Try contains-based matching for composite stone names
        var enumNames = Enum.GetNames<CrystalStoneEnum>()
            .Where(name => name != "CrystalUnknown")
            .OrderByDescending(name => name.Length)
            .ToArray();

        foreach (var enumName in enumNames)
        {
            // Strip "Crystal" prefix for matching
            var baseName = enumName.StartsWith("Crystal") ? enumName.Substring(7) : enumName;
            if (normalized.Contains(baseName, StringComparison.OrdinalIgnoreCase))
            {
                var containsResult = Enum.Parse<CrystalStoneEnum>(enumName, true);
                Logger.LogInformation(
                    "[LumenPrediction][ParseCrystalStone] Matched '{StoneName}' to '{EnumName}' via contains",
                    stoneName, enumName);
                return containsResult;
            }
        }

        Logger.LogWarning(
            "[LumenPrediction][ParseCrystalStone] Unknown crystal stone: {StoneName}",
            stoneName);
        return CrystalStoneEnum.CrystalUnknown;
    }

    #endregion
}

