using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Core.Launchers
{
    internal interface ILauncher
    {
        Task<WardenProcess> Launch(string path, string arguments);

        Task<WardenProcess> LaunchUri(string uri, string path, string arguments);
    }
}
