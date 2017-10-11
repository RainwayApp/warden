using System;
using System.IO;
using Warden.Windows;

namespace Warden.Core.Utils
{
    internal static class ShutdownUtils
    {
        public static void RegisterEvents()
        {
            var isConsoleApp = Console.OpenStandardInput(1) != Stream.Null;
            if (isConsoleApp)
            {
                Api.SetConsoleCtrlHandler(type => ConsoleCtrlCheck(type), true);
            }
            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
        }

        private static void CurrentDomainOnProcessExit(object sender, EventArgs eventArgs)
        {
            WardenManager.Stop();
        }

        private static bool ConsoleCtrlCheck(Api.CtrlTypes ctrlType)
        {
            WardenManager.Stop();
            return true;
        }
    }
}
