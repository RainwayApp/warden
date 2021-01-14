using System;

namespace Warden.Monitor
{
    /// <summary>
    /// Options for configuring the <see cref="SystemProcessMonitor"/>
    /// </summary>
    public class MonitorOptions
    {
        /// <summary>
        /// If set to true all tracked processes will be killed when <see cref="SystemProcessMonitor"/> stops.
        /// </summary>
        public bool KillTrackedProcesses { get; set; }

        /// <summary>
        /// If set this property and <see cref="KillTrackedProcesses"/> are set to true then tracked processes will have their entire family tree killed too.
        /// </summary>
        public bool RecursiveKill { get; set; }
        
        /// <summary>
        /// The <see cref="SystemProcessMonitor"/> polling rate.
        /// </summary>
        /// <remarks>
        ///  The default rate is one second.
        /// </remarks>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
        
        /// <summary>
        /// If set to true the <see cref="SystemProcessMonitor.ProcessBlacklist"/> will not be used to filter processes from being added as children to a <see cref="Core.WardenProcess"/>.
        /// </summary>
        public bool DisableProcessBlacklist { get; set; }
    }
}
