using Aevatar.Agents.Abstractions;

namespace Demo.Agents;

/// <summary>
/// BankAccountAgent RPC interface
/// </summary>
public interface IBankAccountAgent : IGAgent
{
    Task CreateAccountAsync(string accountHolder, double initialBalance = 0);
    Task DepositAsync(double amount, string description = "");
    Task WithdrawAsync(double amount, string description = "");
    Task<double> GetBalanceAsync();
    Task<string> GetAccountHolderAsync();
    Task<int> GetTransactionCountAsync();
    Task<long> GetCurrentVersionAsync();
}

