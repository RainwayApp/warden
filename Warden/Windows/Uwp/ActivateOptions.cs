using System;

namespace Warden.Windows.Uwp
{
    [Flags]
    internal enum ActivateOptionsEnum
    {
        None = 0,
        DesignMode = 0x1,
        NoErrorUI = 0x2,
        NoSplashScreen = 0x4,
    }
}
