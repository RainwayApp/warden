using System.Text.RegularExpressions;

namespace Warden.Core.Launchers.Rules
{
    public class AcceptCodeInQuotationMarks : IRules
    {
        public string Execute(string path)
        {
            var regex = new Regex("\"(.*?)\"", RegexOptions.IgnoreCase); //TODO: It accepts everything between quotation marks for now.
            return regex.IsMatch(path) ? regex.Match(path).Value : null;
        }
    }
}
