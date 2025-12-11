using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Demo.Agents;
using EventSourcingDemo.Events;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace EventSourcingDemo;

/// <summary>
/// 支持 EventSourcing 的银行账户 Agent
/// 使用新的批量提交和纯函数式状态转换模式
/// </summary>
public class BankAccountAgent : GAgentBase<BankAccountState>, IBankAccountAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Bank Account Agent for {State.AccountHolder}");
    }

    // ========== Business Operations (使用新 API) ==========

    /// <summary>
    /// 创建账户
    /// </summary>
    public async Task CreateAccountAsync(string accountHolder, decimal initialBalance = 0)
    {
        Logger.LogInformation("Creating account for {Holder} with initial balance ${Balance}",
            accountHolder, initialBalance);

        var evt = new AccountCreated
        {
            AccountHolder = accountHolder,
            InitialBalance = (double)initialBalance
        };

        // ✅ 新 API: RaiseEvent (暂存)
        RaiseEvent(evt, new Dictionary<string, string>
        {
            ["Operation"] = "CreateAccount",
            ["AccountHolder"] = accountHolder
        });

        // ✅ 新 API: ConfirmEventsAsync (批量提交)
        await ConfirmEventsAsync();

        Logger.LogInformation("Account created successfully. Version: {Version}", GetCurrentVersion());
    }

    /// <summary>
    /// 存款
    /// </summary>
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
            Description = description
        };

        // ✅ 新 API: RaiseEvent (暂存)
        RaiseEvent(evt, new Dictionary<string, string>
        {
            ["Operation"] = "Deposit",
            ["Amount"] = amount.ToString("F2")
        });

        // ✅ 新 API: ConfirmEventsAsync (批量提交)
        await ConfirmEventsAsync();

        Logger?.LogInformation("Deposit confirmed. New balance: ${Balance}", State.Balance);
    }

    /// <summary>
    /// 取款
    /// </summary>
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

        // ✅ 新 API: RaiseEvent (暂存)
        RaiseEvent(evt, new Dictionary<string, string>
        {
            ["Operation"] = "Withdraw",
            ["Amount"] = amount.ToString("F2")
        });

        // ✅ 新 API: ConfirmEventsAsync (批量提交)
        await ConfirmEventsAsync();

        Logger?.LogInformation("Withdrawal confirmed. New balance: ${Balance}", State.Balance);
    }

    /// <summary>
    /// 批量交易（展示批量提交优势）
    /// </summary>
    public async Task BatchTransactionsAsync(
        IEnumerable<(string type, decimal amount, string description)> transactions)
    {
        Logger?.LogInformation("Starting batch transactions...");

        // ✅ 新 API 优势: 可以先暂存多个事件，然后一次性提交
        foreach (var (type, amount, description) in transactions)
        {
            IMessage evt = type.ToLower() switch
            {
                "deposit" => new MoneyDeposited
                {
                    Amount = (double)amount,
                    Description = description
                },
                "withdraw" => new MoneyWithdrawn
                {
                    Amount = (double)amount,
                    Description = description
                },
                _ => throw new ArgumentException($"Unknown transaction type: {type}")
            };

            RaiseEvent(evt); // 暂存，不立即提交
        }

        // ✅ 一次性批量提交所有事件
        await ConfirmEventsAsync();

        Logger?.LogInformation("Batch transactions completed. New balance: ${Balance}", State.Balance);
    }

    // ========== Pure Functional State Transition (新 API) ==========

    /// <summary>
    /// ✅ 纯函数式状态转换
    /// 框架已自动Clone状态，开发者只需修改传入的state即可
    /// </summary>
    protected override void TransitionState(BankAccountState state, IMessage evt)
    {
        Logger?.LogInformation("🔄 TransitionState called with event type: {EventType}", evt.GetType().Name);
        Logger?.LogInformation("   Current state: Balance=${Balance}, Transactions={Count}", state.Balance,
            state.TransactionCount);

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

        Logger?.LogInformation("   New state: Balance=${Balance}, Transactions={Count}", state.Balance,
            state.TransactionCount);
    }

    // ========== RPC Query Methods (Interface Implementation) ==========

    /// <summary>
    /// Get current account balance
    /// </summary>
    public Task<double> GetBalanceAsync()
    {
        return Task.FromResult(State.Balance);
    }

    /// <summary>
    /// Get account holder name
    /// </summary>
    public Task<string> GetAccountHolderAsync()
    {
        return Task.FromResult(State.AccountHolder);
    }

    /// <summary>
    /// Get transaction count
    /// </summary>
    public Task<int> GetTransactionCountAsync()
    {
        return Task.FromResult(State.TransactionCount);
    }

    /// <summary>
    /// Get current version
    /// </summary>
    public Task<long> GetCurrentVersionAsync()
    {
        return Task.FromResult(GetCurrentVersion());
    }

    /// <summary>
    /// Get account summary
    /// </summary>
    public Task<Events.AccountSummary> GetAccountSummaryAsync()
    {
        return Task.FromResult(new Events.AccountSummary
        {
            AccountHolder = State.AccountHolder,
            Balance = State.Balance,
            TransactionCount = State.TransactionCount,
            Version = GetCurrentVersion()
        });
    }
}