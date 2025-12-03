// Global using directives for legacy framework compatibility
// Maps old namespace patterns to new framework or compatibility layer

// Legacy Aevatar.Core namespace -> Compatibility layer
global using Aevatar.Core;
global using Aevatar.Core.Abstractions;

// Legacy Aevatar.GAgents namespaces -> Compatibility layer  
global using Aevatar.GAgents.AI.Abstractions;
global using Aevatar.GAgents.AI.Common;
global using Aevatar.GAgents.AI.Options;
global using Aevatar.GAgents.AIGAgent;
global using Aevatar.GAgents.AIGAgent.Agent;
global using Aevatar.GAgents.AIGAgent.Dtos;
global using Aevatar.GAgents.AIGAgent.GEvents;
global using Aevatar.GAgents.AIGAgent.State;
// NOTE: Aevatar.GAgents.ChatAgent removed - ChatGAgentBase deleted
global using Aevatar.GAgents.ChatAgent.Dtos;
global using Aevatar.GAgents.ChatAgent.GAgent;
global using Aevatar.GAgents.ChatAgent.GAgent.State;

// Legacy AI namespaces
// NOTE: Aevatar.AI.Feature removed - IAIFeature unused
global using Aevatar.AI.Feature.StreamSyncWoker;
global using Aevatar.AI.Exceptions;

// Use EventHandler from compatibility layer, not System.EventHandler
global using EventHandlerAttribute = Aevatar.Core.EventHandlerAttribute;
