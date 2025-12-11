using Aevatar.Agents.Abstractions;

namespace EventSourcingDemo;

/// <summary>
/// Bank Account Agent interface for RPC calls
/// Methods defined in this interface can be called via RPC
/// </summary>
public interface IBankAccountAgent : IGAgent
{
    /// <summary>
    /// Create a new account
    /// </summary>
    Task CreateAccountAsync(string accountHolder, decimal initialBalance = 0);

    /// <summary>
    /// Deposit money into the account
    /// </summary>
    Task DepositAsync(decimal amount, string description = "");

    /// <summary>
    /// Withdraw money from the account
    /// </summary>
    Task WithdrawAsync(decimal amount, string description = "");

    /// <summary>
    /// Get current account balance
    /// </summary>
    Task<double> GetBalanceAsync();

    /// <summary>
    /// Get account holder name
    /// </summary>
    Task<string> GetAccountHolderAsync();

    /// <summary>
    /// Get transaction count
    /// </summary>
    Task<int> GetTransactionCountAsync();

    /// <summary>
    /// Get current version
    /// </summary>
    Task<long> GetCurrentVersionAsync();

    /// <summary>
    /// Get account summary
    /// </summary>
    Task<Events.AccountSummary> GetAccountSummaryAsync();
}

