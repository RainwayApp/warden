using System.Text.RegularExpressions;

namespace Warden.Core.Launchers.Rules
{
    public class AcceptUcn : IRules
    {
        public string Execute(string path)
        {
            var regex = new Regex(@"([A-Z]:\\[^/:\*\?<>\|]+\.((exe)))|(\\{2}[^/:\*\?<>\|]+\.((exe)))", RegexOptions.IgnoreCase);
            return regex.IsMatch(path) ? regex.Match(path).Value : null;
        }
    }
}
