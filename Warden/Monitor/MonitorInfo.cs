using System;

namespace Warden.Monitor
{
    internal class MonitorInfo
    {
        internal MonitorInfo(string targetImage)
        {
            TargetImage = targetImage;
        }
        internal string TargetImage { get; }
    }
}