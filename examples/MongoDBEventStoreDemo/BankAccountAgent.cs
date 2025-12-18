using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventSourcing;
using Demo.Agents;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MongoDBEventStoreDemo.Events;

namespace MongoDBEventStoreDemo;

/// <summary>
/// Bank Account Agent with EventSourcing support (MongoDB backend)
/// Demonstrates EventSourcing V2 API with MongoDB storage
/// Implements IBankAccountAgent for RPC calls via actor.As&lt;IBankAccountAgent&gt;()
/// </summary>
public class BankAccountAgent : GAgentBase<BankAccountState>, IBankAccountAgent
{
    // No need for ID constructor anymore - framework handles it automatically!
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MongoDB Bank Account Agent for {State.AccountHolder}");
    }

    protected override ISnapshotStrategy SnapshotStrategy => new IntervalSnapshotStrategy(10);

    // ========== Business Operations ==========

    public async Task CreateAccountAsync(string accountHolder, decimal initialBalance = 0)
    {
        Logger.LogInformation("Creating account for {Holder} with initial balance ${Balance}", 
            accountHolder, initialBalance);

        var evt = new AccountCreated
        {
            AccountHolder = accountHolder,
            InitialBalance = (double)initialBalance
        };

        RaiseEvent(evt, new Dictionary<string, string>
        {
            ["Operation"] = "CreateAccount",
            ["AccountHolder"] = accountHolder
        });

        await ConfirmEventsAsync();

        Logger?.LogInformation("Account created successfully. Version: {Version}", GetCurrentVersion());
    }

    public async Task DepositAsync(decimal amount, string description = "")
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Deposit amount must be positive", nameof(amount));
        }

        Logger?.LogInformation("Depositing ${Amount}: {Description}", amount, description);

        var evt = new MoneyDeposited
        {
            Amount = (double)amount,
            Description = description ?? $"Deposit at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
        };

        RaiseEvent(evt, new Dictionary<string, string>
        {
            ["Operation"] = "Deposit",
            ["Amount"] = amount.ToString("F2")
        });

        await ConfirmEventsAsync();

        Logger?.LogInformation("Deposit confirmed. New balance: ${Balance}", State.Balance);
    }

    public async Task WithdrawAsync(decimal amount, string description = "")
    {
        if (amount <= 0)
        {
            throw new ArgumentException("Withdrawal amount must be positive", nameof(amount));
        }

        if (State.Balance < (double)amount)
        {
            throw new InvalidOperationException(
                $"Insufficient balance. Current: ${State.Balance:F2}, Requested: ${amount:F2}");
        }

        Logger?.LogInformation("Withdrawing ${Amount}: {Description}", amount, description);

        var evt = new MoneyWithdrawn
        {
            Amount = (double)amount,
            Description = description ?? $"Withdrawal at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
        };

        RaiseEvent(evt, new Dictionary<string, string>
        {
            ["Operation"] = "Withdraw",
            ["Amount"] = amount.ToString("F2")
        });

        await ConfirmEventsAsync();

        Logger?.LogInformation("Withdrawal confirmed. New balance: ${Balance}", State.Balance);
    }

    public async Task BatchTransactionsAsync(params (decimal amount, string description)[] transactions)
    {
        Logger?.LogInformation("Starting batch transactions...");

        foreach (var (amount, description) in transactions)
        {
            if (amount > 0)
            {
                var depositEvt = new MoneyDeposited
                {
                    Amount = (double)amount,
                    Description = description
                };
                RaiseEvent(depositEvt);
            }
            else
            {
                var withdrawEvt = new MoneyWithdrawn
                {
                    Amount = (double)Math.Abs(amount),
                    Description = description
                };
                RaiseEvent(withdrawEvt);
            }
        }

        await ConfirmEventsAsync();
        Logger?.LogInformation("Batch transactions completed. New balance: ${Balance}", State.Balance);
    }

    // ========== RPC Methods (for actor.As<IBankAccountAgent>()) ==========

    public Task<double> GetBalanceAsync() => Task.FromResult(State.Balance);

    public Task<string> GetAccountHolderAsync() => Task.FromResult(State.AccountHolder);

    public Task<int> GetTransactionCountAsync() => Task.FromResult(State.TransactionCount);

    public Task<long> GetCurrentVersionAsync() => Task.FromResult(GetCurrentVersion());

    // ========== Pure Functional State Transition ==========

    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        Logger?.LogInformation("🔄 TransitionState called with event type: {EventType}", evt.GetType().Name);
        Logger?.LogInformation("   Current state: Balance=${Balance}, Transactions={Count}", state.Balance, state.TransactionCount);

        switch (evt)
        {
            case AccountCreated created:
                Logger?.LogInformation("   ✅ Matched AccountCreated: Holder={Holder}, InitialBalance={Balance}", 
                    created.AccountHolder, created.InitialBalance);
                state.AccountHolder = created.AccountHolder;
                state.Balance = created.InitialBalance;
                state.TransactionCount = 0;
                state.History.Add($"[{DateTime.UtcNow:HH:mm:ss}] Account created for {created.AccountHolder}");
                break;

            case MoneyDeposited deposited:
                Logger?.LogInformation("   ✅ Matched MoneyDeposited: Amount={Amount}", deposited.Amount);
                state.Balance += deposited.Amount;
                state.TransactionCount++;
                state.History.Add(
                    $"[{state.TransactionCount}] Deposited ${deposited.Amount:F2} - {deposited.Description}");
                break;

            case MoneyWithdrawn withdrawn:
                Logger?.LogInformation("   ✅ Matched MoneyWithdrawn: Amount={Amount}", withdrawn.Amount);
                state.Balance -= withdrawn.Amount;
                state.TransactionCount++;
                state.History.Add(
                    $"[{state.TransactionCount}] Withdrew ${withdrawn.Amount:F2} - {withdrawn.Description}");
                break;
                
            default:
                Logger?.LogWarning("   ❌ Unknown event type in switch: {EventType}", evt.GetType().FullName);
                break;
        }

        Logger?.LogInformation("   New state: Balance=${Balance}, Transactions={Count}", state.Balance, state.TransactionCount);
    }
}

