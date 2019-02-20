using System.Text.RegularExpressions;

namespace Warden.Core.Launchers.Rules
{
    public class AcceptCommands : IRules
    {
        public string Execute(string path)
        {
            var regex = new Regex(@"([A-Z0-9]*)", RegexOptions.IgnoreCase);
            return regex.IsMatch(path) ? regex.Match(path).Value : null;
        }
    }
}
