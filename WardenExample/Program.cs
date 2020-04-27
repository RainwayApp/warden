using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Warden.Core;

namespace WardenExample
{
    class Program
    {
       
        static void Main(string[] ars)
        {
            WardenManager.Logger = new WardenLogger();
            WardenManager.OnUntrackedProcessAdded += WardenManagerOnOnUntrackedProcessAdded;
            WardenManager.Initialize(new WardenOptions
            {
                CleanOnExit = true,
                DeepKill = true,
                PollingInterval = TimeSpan.FromSeconds(1)
            });

            Console.WriteLine("Start notepad");
           /* var wardenTest = WardenProcess.StartUwp(new WardenStartInfo
            {
                PackageFamilyName = "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                ApplicationId = "App"
            });*/

           var cancel = new CancellationTokenSource();
           cancel.CancelAfter(TimeSpan.FromSeconds(10));
           var wardenTest = WardenProcess.StartUri(new WardenStartInfo
           {
               FileName = "steam://run/107410",
               TargetFileName = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Arma 3\\arma3launcher.exe"
           }, delegate(bool b)
           {
               Console.WriteLine($"Alive! {b}");
           });

            if (wardenTest != null)
            {
              

                Console.WriteLine($"Hello {wardenTest.Id}");
                wardenTest.OnStateChange += delegate (object sender, StateEventArgs args)
                {
                    Console.WriteLine($"---\nName: {wardenTest.Name}\nId: {wardenTest.Id}\nstate changed to {args.State}\n---");
                    if (!wardenTest.IsTreeActive())
                    {
                        Console.WriteLine("We're gone!");
                        Environment.Exit(0);
                    }
                };

                wardenTest.OnProcessAdded += delegate(object sender, ProcessAddedEventArgs args)
                {
                  Console.WriteLine($"Added process {args.ProcessPath} to {args.ParentId}");
                };

                wardenTest.OnChildStateChange += delegate(object sender, StateEventArgs args)
                {
                    var childInfo = wardenTest.FindChildById(args.Id);
                    if (childInfo != null)
                    {
                        Console.WriteLine($"---\nName: {childInfo.Name}\nId: {childInfo.Id}\nParentId:{childInfo.ParentId}\nstated changed to {args.State}\n---");
                    }
                };
            }
            Console.ReadKey(true);
        }

     
        private static void WardenManagerOnOnUntrackedProcessAdded(object sender, UntrackedProcessEventArgs e)
        {
           Console.WriteLine($"{e.ProcessPath} / {e.Id} / {e.Name} / {string.Join(" ", e.CommandLine?.ToArray() ?? new string[0])}");
        }
    }
}
