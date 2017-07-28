using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Warden.Core;
using Xunit;

namespace WardenExample
{
  public class TestWardenOnTestProcess
  {
    [Fact]
    public async void WardenGetsTestProcessInformation()
    {
      WardenManager.Initialize();
      var testProcess = await WardenProcess.Start( "TestProcess.exe", string.Empty, ProcessTypes.Win32 );
      var children = testProcess.Children.ToArray();
      testProcess.OnChildStateChange += TestProcessOnOnChildStateChange;
      await Task.Delay( 5000 );

      Assert.NotEmpty( testProcess.Children );
      testProcess.Kill();
      await Task.Delay( 2000 );
      Assert.True( testProcess.State == ProcessState.Dead );
    }

    private void TestProcessOnOnChildStateChange( object sender, StateEventArgs stateEventArgs )
    {
      Console.WriteLine( stateEventArgs );
    }
  }
}
