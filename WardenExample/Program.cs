using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Warden.Core;
using Warden.Core.Utils;

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
            WardenManager.Initialize(new WardenOptions
            {
                CleanOnExit = true,
                DeepKill = true,
                ReadFileHeaders = true
            });
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
    }
}
