using System;
using System.Collections.Generic;
using Volo.Abp.Caching;

namespace Aevatar.App.LanguageManagement;

[CacheName("Aevatar.App.LanguageManagement.Texts")]
[Serializable]
public class LanguageTextCacheItem
{
    public Dictionary<string, string> Dictionary { get; set; } = new Dictionary<string, string>();

    public static string CalculateCacheKey(string resourceName, string cultureName)
    {
        return $"{resourceName}_{cultureName}";
    }
}