using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests.Integration;

[CollectionDefinition(nameof(SupabaseIntegrationCollection), DisableParallelization = true)]
public sealed class SupabaseIntegrationCollection : ICollectionFixture<SupabaseIntegrationFixture>
{
}


