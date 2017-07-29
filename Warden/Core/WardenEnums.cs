using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Core
{
    public enum ProcessState
    {
        Alive = 0,
        Dead = 1
    }

    public enum ProcessTypes
    {
        Win32 = 0,
        Uwp = 1,
        Uri = 2
    }
}
