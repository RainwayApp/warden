using System;

namespace Warden.Core
{
    public class WardenLogger : IWardenLogger
    {
        public void Debug(string message)
        {
            #if DEBUG
            Console.Out.WriteLine(message);
            #endif
        }

        public void Error(string message)
        {
            Console.Error.WriteLine(message);
        }

        public void Info(string message)
        {
            Console.Out.WriteLine(message);
        }
    }
}
