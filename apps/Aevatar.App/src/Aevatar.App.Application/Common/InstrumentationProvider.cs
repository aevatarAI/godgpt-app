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