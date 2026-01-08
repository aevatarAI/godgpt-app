using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.Agents.Lumen.UserProfile;

public partial class LumenUserProfileGAgent
{
    #region Zodiac Calculations

    private static string CalculateZodiacSign(DateValue? birthDate)
    {
        if (birthDate == null) return "Unknown";
        
        var month = birthDate.Month;
        var day = birthDate.Day;
        
        return (month, day) switch
        {
            (3, >= 21) or (4, <= 19) => "Aries",
            (4, >= 20) or (5, <= 20) => "Taurus",
            (5, >= 21) or (6, <= 20) => "Gemini",
            (6, >= 21) or (7, <= 22) => "Cancer",
            (7, >= 23) or (8, <= 22) => "Leo",
            (8, >= 23) or (9, <= 22) => "Virgo",
            (9, >= 23) or (10, <= 22) => "Libra",
            (10, >= 23) or (11, <= 21) => "Scorpio",
            (11, >= 22) or (12, <= 21) => "Sagittarius",
            (12, >= 22) or (1, <= 19) => "Capricorn",
            (1, >= 20) or (2, <= 18) => "Aquarius",
            _ => "Pisces"
        };
    }

    private static string CalculateChineseZodiac(int birthYear)
    {
        var animals = new[] { "Rat", "Ox", "Tiger", "Rabbit", "Dragon", "Snake", 
                             "Horse", "Goat", "Monkey", "Rooster", "Dog", "Pig" };
        var index = (birthYear - 1900) % 12;
        if (index < 0) index += 12;
        return animals[index];
    }

    private static string CalculateChineseElement(int birthYear)
    {
        var elements = new[]
        {
            "Metal", "Metal", "Fire", "Fire", "Wood", "Wood", "Earth", "Earth", "Metal", "Metal",
            "Fire", "Fire", "Water", "Water", "Earth", "Earth", "Metal", "Metal", "Wood", "Wood",
            "Water", "Water", "Earth", "Earth", "Fire", "Fire", "Wood", "Wood", "Water", "Water",
            "Metal", "Metal", "Fire", "Fire", "Wood", "Wood", "Earth", "Earth", "Metal", "Metal",
            "Fire", "Fire", "Water", "Water", "Earth", "Earth", "Metal", "Metal", "Wood", "Wood",
            "Water", "Water", "Earth", "Earth", "Fire", "Fire", "Wood", "Wood", "Water", "Water"
        };
        var position = (birthYear - 4) % 60;
        if (position < 0) position += 60;
        return elements[position];
    }

    private static string GetChineseZodiacWithElement(int birthYear)
    {
        var element = CalculateChineseElement(birthYear);
        var animal = CalculateChineseZodiac(birthYear);
        return $"{element} {animal}";
    }

    private static ZodiacSignEnum ParseZodiacSignEnum(string zodiacSign)
    {
        return zodiacSign switch
        {
            "Aries" => ZodiacSignEnum.ZodiacAries,
            "Taurus" => ZodiacSignEnum.ZodiacTaurus,
            "Gemini" => ZodiacSignEnum.ZodiacGemini,
            "Cancer" => ZodiacSignEnum.ZodiacCancer,
            "Leo" => ZodiacSignEnum.ZodiacLeo,
            "Virgo" => ZodiacSignEnum.ZodiacVirgo,
            "Libra" => ZodiacSignEnum.ZodiacLibra,
            "Scorpio" => ZodiacSignEnum.ZodiacScorpio,
            "Sagittarius" => ZodiacSignEnum.ZodiacSagittarius,
            "Capricorn" => ZodiacSignEnum.ZodiacCapricorn,
            "Aquarius" => ZodiacSignEnum.ZodiacAquarius,
            "Pisces" => ZodiacSignEnum.ZodiacPisces,
            _ => ZodiacSignEnum.ZodiacUnknown
        };
    }

    private static ChineseZodiacEnum ParseChineseZodiacEnum(string animal)
    {
        return animal switch
        {
            "Rat" => ChineseZodiacEnum.ChineseZodiacRat,
            "Ox" => ChineseZodiacEnum.ChineseZodiacOx,
            "Tiger" => ChineseZodiacEnum.ChineseZodiacTiger,
            "Rabbit" => ChineseZodiacEnum.ChineseZodiacRabbit,
            "Dragon" => ChineseZodiacEnum.ChineseZodiacDragon,
            "Snake" => ChineseZodiacEnum.ChineseZodiacSnake,
            "Horse" => ChineseZodiacEnum.ChineseZodiacHorse,
            "Goat" => ChineseZodiacEnum.ChineseZodiacGoat,
            "Monkey" => ChineseZodiacEnum.ChineseZodiacMonkey,
            "Rooster" => ChineseZodiacEnum.ChineseZodiacRooster,
            "Dog" => ChineseZodiacEnum.ChineseZodiacDog,
            "Pig" => ChineseZodiacEnum.ChineseZodiacPig,
            _ => ChineseZodiacEnum.ChineseZodiacUnknown
        };
    }

    #endregion

    #region Welcome Note Generation

    private static Dictionary<string, string> GenerateWelcomeNote(DateValue? birthDate)
    {
        if (birthDate == null)
        {
            return new Dictionary<string, string>
            {
                { "zodiac", "Unknown" },
                { "chineseZodiac", "Unknown" },
                { "rhythm", "Unknown" },
                { "essence", "Unique" }
            };
        }
        
        var birthYear = birthDate.Year;
        var zodiac = CalculateZodiacSign(birthDate);
        var chineseZodiac = GetChineseZodiacWithElement(birthYear);
        var rhythm = GetRhythmFromYear(birthYear);
        var (yinYang, element) = ParseRhythm(rhythm);
        var chineseZodiacAnimal = ExtractChineseZodiacAnimal(chineseZodiac);
        var essence = GetDeterministicEssence(zodiac, element, yinYang, chineseZodiacAnimal, birthDate);
        
        return new Dictionary<string, string>
        {
            { "zodiac", zodiac },
            { "chineseZodiac", chineseZodiac },
            { "rhythm", rhythm },
            { "essence", essence }
        };
    }

    private static string GetRhythmFromYear(int year)
    {
        var stemIndex = (year - 4) % 10;
        if (stemIndex < 0) stemIndex += 10;
        
        return stemIndex switch
        {
            0 => "Yang Wood",
            1 => "Yin Wood",
            2 => "Yang Fire",
            3 => "Yin Fire",
            4 => "Yang Earth",
            5 => "Yin Earth",
            6 => "Yang Metal",
            7 => "Yin Metal",
            8 => "Yang Water",
            9 => "Yin Water",
            _ => "Yang Wood"
        };
    }

    private static (string yinYang, string element) ParseRhythm(string rhythm)
    {
        var parts = rhythm.Split(' ');
        return (parts[0], parts.Length > 1 ? parts[1] : "Wood");
    }

    private static string ExtractChineseZodiacAnimal(string chineseZodiac)
    {
        var parts = chineseZodiac.Split(' ');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    private static string GetDeterministicEssence(string zodiac, string element, string yinYang, string animal, DateValue birthDate)
    {
        var wordPool = new List<string>();
        wordPool.AddRange(GetZodiacTraits(zodiac));
        wordPool.AddRange(GetElementTraits(element));
        wordPool.AddRange(GetPolarityTraits(yinYang));
        wordPool.Add(GetChineseZodiacTrait(animal));
        
        var uniqueWords = wordPool.Distinct().ToList();
        var seed = birthDate.Year * 10000 + birthDate.Month * 100 + birthDate.Day;
        var random = new Random(seed);
        var shuffled = uniqueWords.OrderBy(_ => random.Next()).ToList();
        var selectedWords = shuffled.Take(2).ToList();
        
        return string.Join(", ", selectedWords.Select(w => char.ToUpper(w[0]) + w.Substring(1)));
    }

    private static List<string> GetZodiacTraits(string zodiac) => zodiac switch
    {
        "Aries" => new List<string> { "bold", "passionate", "daring" },
        "Taurus" => new List<string> { "grounded", "loyal", "steady" },
        "Gemini" => new List<string> { "curious", "expressive", "agile" },
        "Cancer" => new List<string> { "nurturing", "sensitive", "intuitive" },
        "Leo" => new List<string> { "radiant", "confident", "magnetic" },
        "Virgo" => new List<string> { "precise", "thoughtful", "reliable" },
        "Libra" => new List<string> { "graceful", "diplomatic", "balanced" },
        "Scorpio" => new List<string> { "intuitive", "intense", "resilient" },
        "Sagittarius" => new List<string> { "adventurous", "fiery", "free-spirited" },
        "Capricorn" => new List<string> { "disciplined", "ambitious", "wise" },
        "Aquarius" => new List<string> { "visionary", "eccentric", "insightful" },
        "Pisces" => new List<string> { "dreamy", "empathic", "fluid" },
        _ => new List<string> { "unique", "dynamic" }
    };

    private static List<string> GetElementTraits(string element) => element switch
    {
        "Wood" => new List<string> { "adaptable", "growth-focused", "expansive" },
        "Fire" => new List<string> { "dynamic", "passionate", "high-energy" },
        "Earth" => new List<string> { "stable", "practical", "grounded" },
        "Metal" => new List<string> { "refined", "sharp", "resilient" },
        "Water" => new List<string> { "fluid", "deep", "intuitive" },
        _ => new List<string> { "balanced" }
    };

    private static List<string> GetPolarityTraits(string yinYang) => yinYang switch
    {
        "Yin" => new List<string> { "introspective", "subtle", "nurturing" },
        "Yang" => new List<string> { "expressive", "active", "assertive" },
        _ => new List<string> { "balanced" }
    };

    private static string GetChineseZodiacTrait(string animal) => animal switch
    {
        "Rat" => "clever",
        "Ox" => "enduring",
        "Tiger" => "daring",
        "Rabbit" => "graceful",
        "Dragon" => "powerful",
        "Snake" => "wise",
        "Horse" => "free-spirited",
        "Goat" => "gentle",
        "Monkey" => "playful",
        "Rooster" => "focused",
        "Dog" => "loyal",
        "Pig" => "generous",
        _ => "unique"
    };

    #endregion

    #region Translation

    private static Dictionary<string, string> TranslateWelcomeNote(Dictionary<string, string> baseNote, string language)
    {
        if (language == "en" || string.IsNullOrEmpty(language))
        {
            return baseNote;
        }
        
        var translated = new Dictionary<string, string>();
        
        if (baseNote.TryGetValue("zodiac", out var zodiac))
            translated["zodiac"] = TranslateZodiacSign(zodiac, language);
        
        if (baseNote.TryGetValue("chineseZodiac", out var chineseZodiac))
            translated["chineseZodiac"] = TranslateChineseZodiac(chineseZodiac, language);
        
        if (baseNote.TryGetValue("rhythm", out var rhythm))
            translated["rhythm"] = TranslateRhythm(rhythm, language);
        
        if (baseNote.TryGetValue("essence", out var essence))
            translated["essence"] = TranslateEssence(essence, language);
        
        return translated;
    }

    private static string TranslateZodiacSign(string zodiacSign, string language) => (zodiacSign, language) switch
    {
        ("Aries", "zh-tw") => "白羊座",
        ("Taurus", "zh-tw") => "金牛座",
        ("Gemini", "zh-tw") => "雙子座",
        ("Cancer", "zh-tw") => "巨蟹座",
        ("Leo", "zh-tw") => "獅子座",
        ("Virgo", "zh-tw") => "處女座",
        ("Libra", "zh-tw") => "天秤座",
        ("Scorpio", "zh-tw") => "天蠍座",
        ("Sagittarius", "zh-tw") => "射手座",
        ("Capricorn", "zh-tw") => "摩羯座",
        ("Aquarius", "zh-tw") => "水瓶座",
        ("Pisces", "zh-tw") => "雙魚座",
        ("Aries", "zh") => "白羊座",
        ("Taurus", "zh") => "金牛座",
        ("Gemini", "zh") => "双子座",
        ("Cancer", "zh") => "巨蟹座",
        ("Leo", "zh") => "狮子座",
        ("Virgo", "zh") => "处女座",
        ("Libra", "zh") => "天秤座",
        ("Scorpio", "zh") => "天蝎座",
        ("Sagittarius", "zh") => "射手座",
        ("Capricorn", "zh") => "摩羯座",
        ("Aquarius", "zh") => "水瓶座",
        ("Pisces", "zh") => "双鱼座",
        ("Taurus", "es") => "Tauro",
        ("Gemini", "es") => "Géminis",
        ("Cancer", "es") => "Cáncer",
        ("Scorpio", "es") => "Escorpio",
        ("Sagittarius", "es") => "Sagitario",
        ("Capricorn", "es") => "Capricornio",
        ("Aquarius", "es") => "Acuario",
        ("Pisces", "es") => "Piscis",
        _ => zodiacSign
    };

    private static string TranslateChineseZodiac(string chineseZodiac, string language)
    {
        var parts = chineseZodiac.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return chineseZodiac;
        
        var element = parts[0];
        var animal = parts[^1];
        
        return language switch
        {
            "zh-tw" or "zh" => $"{TranslateElement(element, language)}{TranslateAnimal(animal, language)}",
            "es" => $"{TranslateAnimal(animal, language)} de {TranslateElement(element, language)}",
            _ => chineseZodiac
        };
    }

    private static string TranslateElement(string element, string language) => (element, language) switch
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
        _ => element
    };

    private static string TranslateAnimal(string animal, string language) => (animal, language) switch
    {
        ("Rat", "zh-tw") => "鼠", ("Ox", "zh-tw") => "牛", ("Tiger", "zh-tw") => "虎",
        ("Rabbit", "zh-tw") => "兔", ("Dragon", "zh-tw") => "龍", ("Snake", "zh-tw") => "蛇",
        ("Horse", "zh-tw") => "馬", ("Goat", "zh-tw") => "羊", ("Monkey", "zh-tw") => "猴",
        ("Rooster", "zh-tw") => "雞", ("Dog", "zh-tw") => "狗", ("Pig", "zh-tw") => "豬",
        ("Rat", "zh") => "鼠", ("Ox", "zh") => "牛", ("Tiger", "zh") => "虎",
        ("Rabbit", "zh") => "兔", ("Dragon", "zh") => "龙", ("Snake", "zh") => "蛇",
        ("Horse", "zh") => "马", ("Goat", "zh") => "羊", ("Monkey", "zh") => "猴",
        ("Rooster", "zh") => "鸡", ("Dog", "zh") => "狗", ("Pig", "zh") => "猪",
        ("Rat", "es") => "Rata", ("Ox", "es") => "Buey", ("Tiger", "es") => "Tigre",
        ("Rabbit", "es") => "Conejo", ("Dragon", "es") => "Dragón", ("Snake", "es") => "Serpiente",
        ("Horse", "es") => "Caballo", ("Goat", "es") => "Cabra", ("Monkey", "es") => "Mono",
        ("Rooster", "es") => "Gallo", ("Dog", "es") => "Perro", ("Pig", "es") => "Cerdo",
        _ => animal
    };

    private static string TranslateRhythm(string rhythm, string language)
    {
        var parts = rhythm.Split(' ');
        if (parts.Length != 2) return rhythm;
        
        var yinYang = parts[0];
        var element = parts[1];
        
        return language switch
        {
            "zh-tw" => $"{(yinYang == "Yang" ? "陽" : "陰")}{TranslateElement(element, language)}",
            "zh" => $"{(yinYang == "Yang" ? "阳" : "阴")}{TranslateElement(element, language)}",
            "es" => $"{TranslateElement(element, language)} {yinYang}",
            _ => rhythm
        };
    }

    private static string TranslateEssence(string essence, string language)
    {
        if (language == "en" || string.IsNullOrEmpty(language))
            return essence;
        
        var traits = essence.Split(',').Select(t => t.Trim()).ToList();
        var translatedTraits = traits.Select(trait => TranslateTrait(trait, language)).ToList();
        return string.Join(", ", translatedTraits);
    }

    private static string TranslateTrait(string trait, string language)
    {
        var lowerTrait = trait.ToLower();
        
        if (language == "zh-tw")
        {
            return lowerTrait switch
            {
                "adventurous" => "冒險", "bold" => "勇敢", "passionate" => "熱情",
                "reliable" => "可靠", "patient" => "耐心", "practical" => "務實",
                "curious" => "好奇", "adaptable" => "適應力強", "confident" => "自信",
                "generous" => "慷慨", "analytical" => "分析", "diplomatic" => "圓融",
                "intense" => "強烈", "optimistic" => "樂觀", "ambitious" => "有抱負",
                "innovative" => "創新", "intuitive" => "直覺", "compassionate" => "富有同情心",
                "active" => "活躍", "agile" => "敏捷", "assertive" => "果斷",
                "balanced" => "平衡", "clever" => "聰明", "daring" => "大膽",
                "deep" => "深刻", "disciplined" => "自律", "dreamy" => "夢幻",
                "dynamic" => "充滿活力", "eccentric" => "獨特", "empathic" => "共情",
                "enduring" => "持久", "expansive" => "開放", "expressive" => "善於表達",
                "fiery" => "熱情似火", "fluid" => "靈活", "focused" => "專注",
                "free-spirited" => "自由奔放", "gentle" => "溫和", "graceful" => "優雅",
                "grounded" => "踏實", "growth-focused" => "注重成長", "high-energy" => "精力充沛",
                "insightful" => "有洞察力", "introspective" => "內省", "loyal" => "忠誠",
                "magnetic" => "有魅力", "nurturing" => "關懷", "playful" => "愛玩",
                "powerful" => "強大", "precise" => "精確", "radiant" => "光彩照人",
                "refined" => "精緻", "resilient" => "堅韌", "sensitive" => "敏感",
                "sharp" => "敏銳", "stable" => "穩定", "steady" => "穩重",
                "subtle" => "細膩", "thoughtful" => "體貼", "visionary" => "有遠見",
                "wise" => "智慧",
                _ => trait
            };
        }
        
        if (language == "zh")
        {
            return lowerTrait switch
            {
                "adventurous" => "冒险", "bold" => "勇敢", "passionate" => "热情",
                "reliable" => "可靠", "patient" => "耐心", "practical" => "务实",
                "curious" => "好奇", "adaptable" => "适应力强", "confident" => "自信",
                "generous" => "慷慨", "analytical" => "分析", "diplomatic" => "圆融",
                "intense" => "强烈", "optimistic" => "乐观", "ambitious" => "有抱负",
                "innovative" => "创新", "intuitive" => "直觉", "compassionate" => "富有同情心",
                "active" => "活跃", "agile" => "敏捷", "assertive" => "果断",
                "balanced" => "平衡", "clever" => "聪明", "daring" => "大胆",
                "deep" => "深刻", "disciplined" => "自律", "dreamy" => "梦幻",
                "dynamic" => "充满活力", "eccentric" => "独特", "empathic" => "共情",
                "enduring" => "持久", "expansive" => "开放", "expressive" => "善于表达",
                "fiery" => "热情似火", "fluid" => "灵活", "focused" => "专注",
                "free-spirited" => "自由奔放", "gentle" => "温和", "graceful" => "优雅",
                "grounded" => "踏实", "growth-focused" => "注重成长", "high-energy" => "精力充沛",
                "insightful" => "有洞察力", "introspective" => "内省", "loyal" => "忠诚",
                "magnetic" => "有魅力", "nurturing" => "关怀", "playful" => "爱玩",
                "powerful" => "强大", "precise" => "精确", "radiant" => "光彩照人",
                "refined" => "精致", "resilient" => "坚韧", "sensitive" => "敏感",
                "sharp" => "敏锐", "stable" => "稳定", "steady" => "稳重",
                "subtle" => "细腻", "thoughtful" => "体贴", "visionary" => "有远见",
                "wise" => "智慧",
                _ => trait
            };
        }
        
        if (language == "es")
        {
            return lowerTrait switch
            {
                "adventurous" => "Aventurero", "bold" => "Audaz", "passionate" => "Apasionado",
                "reliable" => "Confiable", "patient" => "Paciente", "practical" => "Práctico",
                "curious" => "Curioso", "adaptable" => "Adaptable", "confident" => "Seguro",
                "generous" => "Generoso", "analytical" => "Analítico", "diplomatic" => "Diplomático",
                "intense" => "Intenso", "optimistic" => "Optimista", "ambitious" => "Ambicioso",
                "innovative" => "Innovador", "intuitive" => "Intuitivo", "compassionate" => "Compasivo",
                "active" => "Activo", "agile" => "Ágil", "assertive" => "Asertivo",
                "balanced" => "Equilibrado", "clever" => "Astuto", "daring" => "Atrevido",
                "deep" => "Profundo", "disciplined" => "Disciplinado", "dreamy" => "Soñador",
                "dynamic" => "Dinámico", "eccentric" => "Excéntrico", "empathic" => "Empático",
                "enduring" => "Duradero", "expansive" => "Expansivo", "expressive" => "Expresivo",
                "fiery" => "Ardiente", "fluid" => "Fluido", "focused" => "Concentrado",
                "free-spirited" => "Espíritu libre", "gentle" => "Gentil", "graceful" => "Elegante",
                "grounded" => "Arraigado", "growth-focused" => "Enfocado en crecimiento",
                "high-energy" => "Alta energía", "insightful" => "Perspicaz",
                "introspective" => "Introspectivo", "loyal" => "Leal", "magnetic" => "Magnético",
                "nurturing" => "Protector", "playful" => "Juguetón", "powerful" => "Poderoso",
                "precise" => "Preciso", "radiant" => "Radiante", "refined" => "Refinado",
                "resilient" => "Resistente", "sensitive" => "Sensible", "sharp" => "Agudo",
                "stable" => "Estable", "steady" => "Firme", "subtle" => "Sutil",
                "thoughtful" => "Considerado", "visionary" => "Visionario", "wise" => "Sabio",
                _ => trait
            };
        }
        
        return trait;
    }

    #endregion
}


