using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// CalculatorAgentState 已在 demo_messages.proto 中定义

/// <summary>
/// Calculator Agent interface for RPC
/// </summary>
public interface ICalculatorAgent
{
    Task<double> AddAsync(double a, double b, CancellationToken ct = default);
    Task<double> SubtractAsync(double a, double b, CancellationToken ct = default);
    Task<double> MultiplyAsync(double a, double b, CancellationToken ct = default);
    Task<double> DivideAsync(double a, double b, CancellationToken ct = default);
    double GetLastResult();
    int GetOperationCount();
}

/// <summary>
/// 示例：计算器Agent
/// </summary>
public class CalculatorAgent : GAgentBase<CalculatorAgentState>, ICalculatorAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Calculator Agent - Performs basic arithmetic operations");
    }

    /// <summary>
    /// 加法运算
    /// </summary>
    public async Task<double> AddAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a + b;
        await RecordOperation($"{a} + {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 减法运算
    /// </summary>
    public async Task<double> SubtractAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a - b;
        await RecordOperation($"{a} - {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 乘法运算
    /// </summary>
    public async Task<double> MultiplyAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a * b;
        await RecordOperation($"{a} × {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 除法运算
    /// </summary>
    public async Task<double> DivideAsync(double a, double b, CancellationToken ct = default)
    {
        if (Math.Abs(b) < 0.0001)
            throw new DivideByZeroException("除数不能为零");

        var result = a / b;
        await RecordOperation($"{a} ÷ {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// 获取计算历史
    /// </summary>
    public Google.Protobuf.Collections.RepeatedField<string> GetHistory() => State.History;

    /// <summary>
    /// 获取上次结果
    /// </summary>
    public double GetLastResult() => State.LastResult;

    /// <summary>
    /// 获取操作计数
    /// </summary>
    public int GetOperationCount() => State.OperationCount;

    private async Task RecordOperation(string operation, double result, CancellationToken ct)
    {
        State.LastResult = result;
        State.OperationCount++;
        State.History.Add($"[{State.OperationCount}] {operation}");

        Console.WriteLine($"[CalculatorAgent] 计算完成: {operation}");

        // Persist state to MongoDB (non-EventSourcing mode)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, State, ct);
            Console.WriteLine($"[CalculatorAgent] State 已持久化");
        }
    }
}
