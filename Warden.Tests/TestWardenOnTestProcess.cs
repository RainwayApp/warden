using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Warden.Core;
using Xunit;

namespace Warden.Tests
{
  public class TestWardenOnTestProcess : IDisposable
  {
    private readonly WardenProcess _currentProcess;

    public TestWardenOnTestProcess()
    {
      WardenManager.Initialize( true );
      _currentProcess = WardenProcess.GetProcessFromId( Process.GetCurrentProcess().Id );
    }

    /// <summary>
    /// Verify Collection Changes when starting a new process
    /// </summary>
    [Fact]
    public void TestWardenProcess()
    {
      var expected = new List<string>( new[] { "cmd" } );
      var actual = new List<string>();
      _currentProcess.Children.CollectionChanged += ( s, e ) => actual.AddRange( e.NewItems.OfType<WardenProcess>().Select( p => p.Name ) );
      Thread.Sleep( 1000 );
      Process.Start( "cmd" );
      Thread.Sleep( 1000 );
      Assert.Equal( expected, actual );
    }

    /// <summary>
    /// Current bug where there is no way to subscibe to children becoming alive from an event
    /// </summary>
    [Fact]
    public void TestWardenProcessSendsEventsWhenChildProcessStarts()
    {
      var expected = new List<string>( new[] { "cmd" } );
      var actual = new List<string>();
      _currentProcess.OnChildStateChange += ( s, e ) => actual.Add( _currentProcess.FindChildById( e.Id ).Name );
      Thread.Sleep( 1000 );
      Process.Start( "cmd" );
      Thread.Sleep( 1000 );
      Assert.Equal( expected, actual );
    }

    public void Dispose()
    {
      foreach ( var child in _currentProcess.Children.ToArray() )
      {
        child.Kill();
      }
    }
  }
}
