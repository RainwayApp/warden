using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Core
{
    public enum ProcessState
    {
        Alive,
        Dead
    }

    public enum ProcessTypes
    {
        Win32,
        Uwp,
        Uri
    }
}
