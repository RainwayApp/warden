using System;
using System.Collections.Generic;
using Warden.Core;
using Warden.Core.Exceptions;
using Warden.Properties;

namespace Warden.Windows.Uwp
{
    /// <summary>
    /// A class for launching and handling UWP applications
    /// </summary>
    public static class UwpShell
    {
        /// <summary>
        /// Combines the Package Family Name and Application ID into a valid AUMID string and then launches the app.
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns>If the app is launched successfully a WardenProcess is returned.</returns>
        public static WardenProcess LaunchApp(WardenStartInfo startInfo)
        {
            var aumid = $"{startInfo.PackageFamilyName}!{startInfo.ApplicationId}";
            var processId = Launch(aumid, startInfo.Arguments);
            if (processId <= 0)
            {
                throw new WardenLaunchException(string.Format(Resources.Exception_Could_Not_Find_Process_Id, aumid));
            }
            return WardenProcess.GetProcessFromId(processId, startInfo.Filters);
        }


        /// <summary>
        ///     Launch a UWP App using a ApplicationActivationManager
        /// </summary>
        /// <param name="aumid">The AUMID format is the package family name followed by an exclamation point and the application ID.</param>
        /// <param name="arguments"></param>
        /// <returns>when this method returns successfully, receives the process ID of the app instance that fulfills this contract.</returns>
        private static int Launch(string aumid, string arguments) 
        {
            using (new WardenImpersonator())
            {
                var mgr = new ApplicationActivationManager();
                try
                {
                    mgr.ActivateApplication(aumid, arguments, ActivateOptionsEnum.None, out var processId);
                    return (int) processId;
                }
                catch (Exception ex)
                {
                    throw new WardenLaunchException(string.Format(Resources.Exception_Error_Trying_To_Launch_App, ex.Message), ex);
                }
            }
        }
    }
}
