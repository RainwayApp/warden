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
            Start();

            Console.Read();
        }

        private static async void Start()
        {

          
             WardenManager.Initialize(true);
      
           /* var test = await WardenProcess.StartUri("steam://run/107410", "G:/Games/steamapps/common/Arma 3/arma3launcher.exe", string.Empty);
            await Task.Delay(TimeSpan.FromSeconds(60));
            string SerializedResponse = JsonConvert.SerializeObject(
                test,
                new Newtonsoft.Json.Converters.StringEnumConverter()
            );
            File.WriteAllText("test.json", SerializedResponse);*/
            //var test = await WardenManager.Launch("Microsoft.Halo5Forge_8wekyb3d8bbwe", "!Ausar", ProcessTypes.Uwp);
            //    var test = await WardenProcess.StartUri("steam://run/107410", "G:\\Games\\steamapps\\common\\Arma 3\\arma3launcher.exe", string.Empty);
            /*var test = WardenProcess.GetProcessFromId(6716);
            if (test != null)
            {
                Console.WriteLine(test.Name);
                test.OnStateChange += delegate (object sender, StateEventArgs args)
                {
                    Console.WriteLine(args.State);
                };
                test.OnChildStateChange += delegate (object sender, StateEventArgs args)
                {
                    Console.WriteLine($"{args.Id} {args.State}");
                };
            }*/
        }

    }
}
