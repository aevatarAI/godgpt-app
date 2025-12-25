/*aevatar_tool
{
  "name": "system_info",
  "description": "Return runtime + OS + process information",
  "category": "Core",
  "version": "1.0.0",
  "tags": ["dotnet", "file", "skill", "system"],
  "parameters": { "items": {}, "required": [] }
}
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var proc = Process.GetCurrentProcess();

var result = new
{
    os = RuntimeInformation.OSDescription,
    osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    framework = RuntimeInformation.FrameworkDescription,
    dotnetVersion = Environment.Version.ToString(),
    machineName = Environment.MachineName,
    userName = Environment.UserName,
    processorCount = Environment.ProcessorCount,
    pid = proc.Id,
    workingSetBytes = proc.WorkingSet64,
    uptimeMs = Environment.TickCount64,
    aevatar = new
    {
        agentId = Environment.GetEnvironmentVariable("AEVATAR_AGENT_ID"),
        agentType = Environment.GetEnvironmentVariable("AEVATAR_AGENT_TYPE"),
        sessionId = Environment.GetEnvironmentVariable("AEVATAR_SESSION_ID"),
        toolName = Environment.GetEnvironmentVariable("AEVATAR_TOOL_NAME")
    }
};

var jsonOptions = new JsonSerializerOptions
{
    // .NET 10 file-based apps may disable reflection serialization by default.
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

