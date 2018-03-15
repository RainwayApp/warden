using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Core
{
    public interface IWardenLogger
    {
        void Error(string message);

        void Debug(string message);

        void Info(string message);
    }
}
