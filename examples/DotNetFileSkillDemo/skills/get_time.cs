/*aevatar_tool
{
  "name": "get_time",
  "description": "Return current UTC/local time and timezone",
  "category": "Core",
  "version": "1.0.0",
  "tags": ["dotnet", "file", "skill", "time"],
  "parameters": { "items": {}, "required": [] }
}
*/

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var utc = DateTimeOffset.UtcNow;
var local = DateTimeOffset.Now;

var result = new
{
    utc = utc.ToString("O"),
    local = local.ToString("O"),
    timezone = TimeZoneInfo.Local.Id
};

var jsonOptions = new JsonSerializerOptions
{
    // .NET 10 file-based apps may disable reflection serialization by default.
    // Enable reflection-based metadata generation explicitly.
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

