using System;
using Warden.Core;
using Warden.Monitor;

namespace TestApp
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var process = WardenProcess.GetCurrentProcess();
            Console.WriteLine(process.Info);

            SystemProcessMonitor.Start(new MonitorOptions());

            WardenProcess.Start(new WardenStartInfo
            {
                FileName = "spotify://album:27ftYHLeunzcSzb33Wk1hf",
                Track = false,
            });

            var solitaire = WardenProcess.Start(
                new WardenStartInfo
                {
                    FileName = "xboxliveapp-1297287741:",
                    TargetImage = "C:\\Program Files\\WindowsApps\\Microsoft.MicrosoftSolitaireCollection_4.7.10142.0_x64__8wekyb3d8bbwe\\Solitaire.exe",
                    Track = true,
                }, OnFound);

            if (solitaire is {Info: not null})
            {
                solitaire.OnExit += OnExit;
            }
            Console.ReadLine();
        }

        private static void OnExit(object sender, int exitCode)
        {
            Console.WriteLine("Tracked Process Closed: " + sender + " / " + exitCode);
            Environment.Exit(0);
        }

        private static void OnFound(object sender, WardenProcess e)
        {
            Console.WriteLine("Tracked Process Found: " + e.Info);
        }
    }
}