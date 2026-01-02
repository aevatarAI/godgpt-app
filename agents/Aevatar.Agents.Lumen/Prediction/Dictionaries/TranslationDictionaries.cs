namespace Aevatar.Agents.Lumen.Prediction.Dictionaries;

/// <summary>
/// Translation dictionaries for Lumen prediction content
/// Extracted from original LumenPredictionGAgent for better maintainability
/// </summary>
public static class TranslationDictionaries
{
    /// <summary>
    /// Tarot card name translations: English -> (Simplified Chinese, Traditional Chinese)
    /// </summary>
    public static readonly Dictionary<string, (string zh, string zhTw)> TarotCardTranslations = new()
    {
        // Major Arcana
        ["The Fool"] = ("愚者", "愚者"),
        ["The Magician"] = ("魔术师", "魔術師"),
        ["The High Priestess"] = ("女祭司", "女祭司"),
        ["The Empress"] = ("女皇", "女皇"),
        ["The Emperor"] = ("皇帝", "皇帝"),
        ["The Hierophant"] = ("教皇", "教皇"),
        ["The Lovers"] = ("恋人", "戀人"),
        ["The Chariot"] = ("战车", "戰車"),
        ["Strength"] = ("力量", "力量"),
        ["The Hermit"] = ("隐士", "隱士"),
        ["Wheel of Fortune"] = ("命运之轮", "命運之輪"),
        ["Justice"] = ("正义", "正義"),
        ["The Hanged Man"] = ("倒吊者", "倒吊者"),
        ["Death"] = ("死亡", "死亡"),
        ["Temperance"] = ("节制", "節制"),
        ["The Devil"] = ("恶魔", "惡魔"),
        ["The Tower"] = ("塔", "塔"),
        ["The Star"] = ("星星", "星星"),
        ["The Moon"] = ("月亮", "月亮"),
        ["The Sun"] = ("太阳", "太陽"),
        ["Judgement"] = ("审判", "審判"),
        ["The World"] = ("世界", "世界"),
        
        // Minor Arcana - Wands
        ["Ace of Wands"] = ("权杖王牌", "權杖王牌"),
        ["Two of Wands"] = ("权杖二", "權杖二"),
        ["Three of Wands"] = ("权杖三", "權杖三"),
        ["Four of Wands"] = ("权杖四", "權杖四"),
        ["Five of Wands"] = ("权杖五", "權杖五"),
        ["Six of Wands"] = ("权杖六", "權杖六"),
        ["Seven of Wands"] = ("权杖七", "權杖七"),
        ["Eight of Wands"] = ("权杖八", "權杖八"),
        ["Nine of Wands"] = ("权杖九", "權杖九"),
        ["Ten of Wands"] = ("权杖十", "權杖十"),
        ["Page of Wands"] = ("权杖侍从", "權杖侍從"),
        ["Knight of Wands"] = ("权杖骑士", "權杖騎士"),
        ["Queen of Wands"] = ("权杖王后", "權杖王后"),
        ["King of Wands"] = ("权杖国王", "權杖國王"),
        
        // Minor Arcana - Cups
        ["Ace of Cups"] = ("圣杯王牌", "聖杯王牌"),
        ["Two of Cups"] = ("圣杯二", "聖杯二"),
        ["Three of Cups"] = ("圣杯三", "聖杯三"),
        ["Four of Cups"] = ("圣杯四", "聖杯四"),
        ["Five of Cups"] = ("圣杯五", "聖杯五"),
        ["Six of Cups"] = ("圣杯六", "聖杯六"),
        ["Seven of Cups"] = ("圣杯七", "聖杯七"),
        ["Eight of Cups"] = ("圣杯八", "聖杯八"),
        ["Nine of Cups"] = ("圣杯九", "聖杯九"),
        ["Ten of Cups"] = ("圣杯十", "聖杯十"),
        ["Page of Cups"] = ("圣杯侍从", "聖杯侍從"),
        ["Knight of Cups"] = ("圣杯骑士", "聖杯騎士"),
        ["Queen of Cups"] = ("圣杯王后", "聖杯王后"),
        ["King of Cups"] = ("圣杯国王", "聖杯國王"),
        
        // Minor Arcana - Swords
        ["Ace of Swords"] = ("宝剑王牌", "寶劍王牌"),
        ["Two of Swords"] = ("宝剑二", "寶劍二"),
        ["Three of Swords"] = ("宝剑三", "寶劍三"),
        ["Four of Swords"] = ("宝剑四", "寶劍四"),
        ["Five of Swords"] = ("宝剑五", "寶劍五"),
        ["Six of Swords"] = ("宝剑六", "寶劍六"),
        ["Seven of Swords"] = ("宝剑七", "寶劍七"),
        ["Eight of Swords"] = ("宝剑八", "寶劍八"),
        ["Nine of Swords"] = ("宝剑九", "寶劍九"),
        ["Ten of Swords"] = ("宝剑十", "寶劍十"),
        ["Page of Swords"] = ("宝剑侍从", "寶劍侍從"),
        ["Knight of Swords"] = ("宝剑骑士", "寶劍騎士"),
        ["Queen of Swords"] = ("宝剑王后", "寶劍王后"),
        ["King of Swords"] = ("宝剑国王", "寶劍國王"),
        
        // Minor Arcana - Pentacles
        ["Ace of Pentacles"] = ("金币王牌", "金幣王牌"),
        ["Two of Pentacles"] = ("金币二", "金幣二"),
        ["Three of Pentacles"] = ("金币三", "金幣三"),
        ["Four of Pentacles"] = ("金币四", "金幣四"),
        ["Five of Pentacles"] = ("金币五", "金幣五"),
        ["Six of Pentacles"] = ("金币六", "金幣六"),
        ["Seven of Pentacles"] = ("金币七", "金幣七"),
        ["Eight of Pentacles"] = ("金币八", "金幣八"),
        ["Nine of Pentacles"] = ("金币九", "金幣九"),
        ["Ten of Pentacles"] = ("金币十", "金幣十"),
        ["Page of Pentacles"] = ("金币侍从", "金幣侍從"),
        ["Knight of Pentacles"] = ("金币骑士", "金幣騎士"),
        ["Queen of Pentacles"] = ("金币王后", "金幣王后"),
        ["King of Pentacles"] = ("金币国王", "金幣國王"),
    };

    /// <summary>
    /// Crystal/Stone name translations: English -> (Simplified Chinese, Traditional Chinese)
    /// </summary>
    public static readonly Dictionary<string, (string zh, string zhTw)> StoneTranslations = new()
    {
        // Common gemstones
        ["Amethyst"] = ("紫水晶", "紫水晶"),
        ["Rose Quartz"] = ("粉水晶", "粉水晶"),
        ["Citrine"] = ("黄水晶", "黃水晶"),
        ["Clear Quartz"] = ("白水晶", "白水晶"),
        ["Smoky Quartz"] = ("茶晶", "茶晶"),
        ["Black Obsidian"] = ("黑曜石", "黑曜石"),
        ["Moonstone"] = ("月光石", "月光石"),
        ["Labradorite"] = ("拉长石", "拉長石"),
        ["Lapis Lazuli"] = ("青金石", "青金石"),
        ["Turquoise"] = ("绿松石", "綠松石"),
        ["Malachite"] = ("孔雀石", "孔雀石"),
        ["Jade"] = ("玉", "玉"),
        ["Emerald"] = ("祖母绿", "祖母綠"),
        ["Aquamarine"] = ("海蓝宝", "海藍寶"),
        ["Sapphire"] = ("蓝宝石", "藍寶石"),
        ["Ruby"] = ("红宝石", "紅寶石"),
        ["Garnet"] = ("石榴石", "石榴石"),
        ["Carnelian"] = ("红玛瑙", "紅瑪瑙"),
        ["Agate"] = ("玛瑙", "瑪瑙"),
        ["Moss Agate"] = ("苔藓玛瑙", "苔蘚瑪瑙"),
        ["Tiger's Eye"] = ("虎眼石", "虎眼石"),
        ["Hematite"] = ("赤铁矿", "赤鐵礦"),
        ["Pyrite"] = ("黄铁矿", "黃鐵礦"),
        ["Amazonite"] = ("天河石", "天河石"),
        ["Sodalite"] = ("方钠石", "方鈉石"),
        ["Aventurine"] = ("东陵石", "東陵石"),
        ["Fluorite"] = ("萤石", "螢石"),
        ["Peridot"] = ("橄榄石", "橄欖石"),
        ["Topaz"] = ("黄玉", "黃玉"),
        ["Opal"] = ("蛋白石", "蛋白石"),
        ["Pearl"] = ("珍珠", "珍珠"),
        ["Coral"] = ("珊瑚", "珊瑚"),
        ["Amber"] = ("琥珀", "琥珀"),
        ["Rhodonite"] = ("蔷薇辉石", "薔薇輝石"),
        ["Rhodochrosite"] = ("菱锰矿", "菱錳礦"),
        ["Kunzite"] = ("紫锂辉石", "紫鋰輝石"),
        ["Selenite"] = ("透石膏", "透石膏"),
        ["Calcite"] = ("方解石", "方解石"),
        ["Howlite"] = ("菱镁矿", "菱鎂礦"),
        ["Jasper"] = ("碧玉", "碧玉"),
        ["Bloodstone"] = ("血石", "血石"),
        ["Onyx"] = ("缟玛瑙", "縞瑪瑙"),
        ["Jet"] = ("煤玉", "煤玉"),
    };

    /// <summary>
    /// Tarot card orientation translations: English -> (Simplified Chinese, Traditional Chinese)
    /// </summary>
    public static readonly Dictionary<string, (string zh, string zhTw)> OrientationTranslations = new()
    {
        ["Upright"] = ("正位", "正位"),
        ["Reversed"] = ("逆位", "逆位"),
    };

    #region Translation Helper Methods

    /// <summary>
    /// Translate tarot card name to target language
    /// </summary>
    public static string TranslateTarotCard(string englishName, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(englishName))
            return englishName;
            
        if (!TarotCardTranslations.TryGetValue(englishName, out var translations))
            return englishName;

        return targetLanguage switch
        {
            "zh" => translations.zh,
            "zh-tw" => translations.zhTw,
            _ => englishName
        };
    }

    /// <summary>
    /// Translate crystal/stone name to target language
    /// </summary>
    public static string TranslateStone(string englishName, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(englishName))
            return englishName;
            
        if (!StoneTranslations.TryGetValue(englishName, out var translations))
            return englishName;

        return targetLanguage switch
        {
            "zh" => translations.zh,
            "zh-tw" => translations.zhTw,
            _ => englishName
        };
    }

    /// <summary>
    /// Translate tarot orientation to target language
    /// </summary>
    public static string TranslateOrientation(string englishOrientation, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(englishOrientation))
            return englishOrientation;
            
        if (!OrientationTranslations.TryGetValue(englishOrientation, out var translations))
            return englishOrientation;

        return targetLanguage switch
        {
            "zh" => translations.zh,
            "zh-tw" => translations.zhTw,
            _ => englishOrientation
        };
    }

    #endregion
}

