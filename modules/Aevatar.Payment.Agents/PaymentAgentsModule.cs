using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.Payment.Agents;

/// <summary>
/// ABP Module for Payment Agents (Silo-side)
/// </summary>
public class PaymentAgentsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Agent registration is handled by Orleans/Agent framework
        // This module just needs to be referenced by the Silo project
    }
}

