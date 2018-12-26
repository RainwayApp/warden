using Warden.Core;
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
   }
}
