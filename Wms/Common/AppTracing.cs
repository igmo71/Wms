using System.Diagnostics;

namespace Wms.Common;

internal class AppTracing
{
    public static Activity? StartActivity(string name, string sourceContext)
    {
        var activitySource = new ActivitySource(sourceContext);

        return activitySource.StartActivity(name);
    }
}
