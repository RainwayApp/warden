using Warden.Core;
using Warden.Core.Launchers;
using Xunit;

namespace Warden.Tests
{
   public class W32LauncherTests
   {
      public W32LauncherTests()
      {
         WardenManager.Initialize(new WardenOptions());
      }

      [Fact]
      public async void Given_EnvironmentPathIsSet_When_CommandIsTypedInsteadOfPath_LauncherShouldStart()
      {
         const string command = "cmd";
         WardenProcess testProcess = null;
         try
         {
            testProcess = await WardenProcess.Start(command, string.Empty);

         }
         catch
         {
         }

         Assert.NotNull(testProcess);
      }

      [Fact]
      public async void Given_EnvironmentPathIsNotSet_When_CommandIsTypedInsteadOfPath_LauncherShouldThrowException()
      {
         const string command = "incorrectCommand";

         WardenProcess testProcess = null;
         try
         {
            testProcess = await WardenProcess.Start(command, string.Empty);
         }
         catch
         {
         }

         Assert.Null(testProcess);
      }


      [Theory]
      [InlineData(@"cmd", @"cmd")]
      [InlineData(@"garbled cmd garbled", @"garbled")]
      [InlineData(@"cmd.exe", @"cmd.exe")]
      [InlineData(@"garbled cmd.exe garbled", @"cmd.exe")]
      [InlineData(@"C:\cmd.exe", @"C:\cmd.exe")]
      [InlineData(@"garbled C:\cmd.exe garbled", @"C:\cmd.exe")]
      [InlineData(@"C:\Windows\system32\cmd.exe", @"C:\Windows\system32\cmd.exe")]
      [InlineData(@"garbled C:\Windows\system32\cmd.exe garbled", @"C:\Windows\system32\cmd.exe")]
      [InlineData(@"\\server\cmd.exe", @"\\server\cmd.exe")]
      [InlineData(@"garbled \\server\cmd.exe garbled", @"\\server\cmd.exe")]
      [InlineData("\"warden dot dll.exe\"", "\"warden dot dll.exe\"")]
      [InlineData("garbled \"warden dot dll.exe\" garbled", "\"warden dot dll.exe\"")]
      public void Given_PathOrCommandSpecified_When_FullLocalOrNetworkPathIsSpecified_Then_PrioritizeAndSanitize(string path, string expected)
      {
         Assert.Equal(expected, Win32Launcher.GetSafePath(path));
      }

   }
}
