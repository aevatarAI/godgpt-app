using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aevatar.App.Common;

public interface IInstrumentationProvider
{
    ActivitySource ActivitySource { get; }
    Meter Meter { get; }
    
}

public class InstrumentationProvider : IInstrumentationProvider, IDisposable
{
    public InstrumentationProvider(string activitySourceName, string activitySourceVersion)
    {
        ActivitySource = new ActivitySource(activitySourceName, activitySourceVersion);
        Meter = new Meter(activitySourceName, activitySourceVersion);
    }

    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}

/// <summary>
/// No-op instrumentation provider for when OpenTelemetry is disabled
/// Metrics are still recorded but not collected/exported
/// </summary>
public class NullInstrumentationProvider : IInstrumentationProvider, IDisposable
{
    private const string NullSourceName = "Aevatar.App.Null";
    
    public NullInstrumentationProvider()
    {
        ActivitySource = new ActivitySource(NullSourceName, "0.0.0");
        Meter = new Meter(NullSourceName, "0.0.0");
    }

    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}