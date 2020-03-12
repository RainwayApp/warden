using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Warden.Core;

namespace WardenExample
{
    class Program
    {
        static void Main(string[] args)
        {
            Start().GetAwaiter().GetResult();
        }

        private static async Task Start()
        {
            WardenManager.Logger = new WardenLogger();
            WardenManager.OnUntrackedProcessAdded += WardenManagerOnOnUntrackedProcessAdded;
            WardenManager.Initialize(new WardenOptions
            {
                CleanOnExit = true,
                DeepKill = true,
                PollingInterval = TimeSpan.FromSeconds(1)
            });
         
            Console.WriteLine("Press any key to continue");
            Console.ReadKey(true);
            Console.Write("Enter the process ID: ");
            var processId = int.Parse(Console.ReadLine());
            var test = WardenProcess.GetProcessFromId(processId);
            if (test != null)
            {
                test.OnProcessAdded += delegate (object sender, ProcessAddedEventArgs args)
                {
                    if (args.ParentId == test.Id)
                    {
                        Console.WriteLine($"Added child {args.Name}({args.Id}) to root process {test.Name}({test.Id})");
                    }
                    else
                    {
                        var parentInfo = test.FindChildById(args.ParentId);
                        if (parentInfo != null)
                        {
                            Console.WriteLine($"Added child process {args.Name}({args.Id}) to child {parentInfo.Name}({parentInfo.Id})");
                        }
                    }
                };
                test.OnStateChange += delegate (object sender, StateEventArgs args)
                {
                    Console.WriteLine($"---\nName: {test.Name}\nId: {test.Id}\nstate changed to {args.State}\n---");
                };
                test.OnChildStateChange += delegate (object sender, StateEventArgs args)
                {
                    var childInfo = test.FindChildById(args.Id);
                    if (childInfo != null)
                    {
                        Console.WriteLine($"---\nName: {childInfo.Name}\nId: {childInfo.Id}\nParentId:{childInfo.ParentId}\nstated changed to {args.State}\n---");
                    }
                };
                Console.WriteLine($"Hooked into {test.Name}({test.Id})");
                Console.Read();
                Console.WriteLine(JsonConvert.SerializeObject(test, Formatting.Indented));
                test.Kill();
            }
            
           

            Console.WriteLine("Start notepad");
            var wardenTest = await WardenProcess.Start("notepad", string.Empty, null);
            if (wardenTest != null)
            {
                wardenTest.OnStateChange += delegate (object sender, StateEventArgs args)
                {
                    Console.WriteLine($"---\nName: {wardenTest.Name}\nId: {wardenTest.Id}\nstate changed to {args.State}\n---");
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
