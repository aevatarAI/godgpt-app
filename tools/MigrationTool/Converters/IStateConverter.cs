using Google.Protobuf;

namespace MigrationTool.Converters;

/// <summary>
/// Interface for converting between old and new state formats
/// </summary>
/// <typeparam name="TOldState">Old C# state type</typeparam>
/// <typeparam name="TNewState">New Protobuf state type</typeparam>
public interface IStateConverter<TOldState, TNewState>
    where TNewState : IMessage<TNewState>, new()
{
    /// <summary>
    /// Convert old state to new Protobuf state
    /// </summary>
    TNewState Convert(TOldState oldState);
    
    /// <summary>
    /// Convert new state back to old state (for verification)
    /// </summary>
    TOldState ConvertBack(TNewState newState);
}
