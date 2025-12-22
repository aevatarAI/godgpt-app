// =============================================================================
// Global using directives for GodGPT Agents
// =============================================================================

// Common .NET namespaces
global using System.Text;
global using System.Text.Json;
global using System.Net.Mime;

// Proto generated namespaces
global using Aevatar.Agents.GodGPT.Protos.DailyPushUser;

// Legacy Compatibility Layer - Aevatar.Core (Primary for this project)
global using Aevatar.Core;
global using Aevatar.Core.Abstractions;

// Legacy Compatibility Layer - Aevatar.GAgents
global using Aevatar.GAgents.AI.Abstractions;
global using Aevatar.GAgents.AI.Common;
global using Aevatar.GAgents.AI.Options;
global using Aevatar.GAgents.AIGAgent;
global using Aevatar.GAgents.AIGAgent.Agent;
global using Aevatar.GAgents.AIGAgent.Dtos;
global using Aevatar.GAgents.AIGAgent.GEvents;
global using Aevatar.GAgents.AIGAgent.State;
global using Aevatar.GAgents.ChatAgent.Dtos;
global using Aevatar.GAgents.ChatAgent.GAgent;
global using Aevatar.GAgents.ChatAgent.GAgent.State;

// Legacy Compatibility Layer - AI Features
global using Aevatar.AI.Feature.StreamSyncWoker;
global using Aevatar.AI.Exceptions;

// Type aliases to resolve ambiguity - use Legacy types
global using IGAgent = Aevatar.Core.Abstractions.IGAgent;
global using EventHandlerAttribute = Aevatar.Core.EventHandlerAttribute;

// Application namespaces (actual business logic)
global using Aevatar.Application.Grains.Agents.ChatManager.Common;
global using Aevatar.Application.Grains.Common.Constants;
global using Aevatar.Application.Grains.ChatManager.Dtos;
global using Aevatar.Application.Grains.UserBilling;

// C# enums are kept in Common/Constants for business logic compatibility
// Proto enums are used in State and Event definitions only

// Static utility classes - explicitly import their namespaces
global using Aevatar.Application.Grains.Common.Helpers;
global using Aevatar.Application.Grains.GodChat;
