// =============================================================================
// Global using directives for GodGPT Agents
// =============================================================================

// Common .NET namespaces
global using System.Text;
global using System.Text.Json;
global using System.Net.Mime;

// Compatibility Layer - only keeping what's actually used
global using Aevatar.GAgents.AI.Abstractions;
global using Aevatar.GAgents.AI.Common;
global using Aevatar.GAgents.AI.Options;
global using Aevatar.GAgents.AIGAgent.Dtos;
global using Aevatar.GAgents.AIGAgent.GEvents;
global using Aevatar.GAgents.ChatAgent.Dtos;

// New Framework - use new IGAgent
global using IGAgent = Aevatar.Agents.Abstractions.IGAgent;
global using EventHandlerAttribute = Aevatar.Agents.Abstractions.Attributes.EventHandlerAttribute;

// Application namespaces (actual business logic)
global using Aevatar.Application.Grains.Agents.ChatManager.Common;
global using Aevatar.Application.Grains.Common.Constants;
global using Aevatar.Application.Grains.ChatManager.Dtos;

// Static utility classes
global using Aevatar.Application.Grains.Common.Helpers;
global using Aevatar.Application.Grains.GodChat;
