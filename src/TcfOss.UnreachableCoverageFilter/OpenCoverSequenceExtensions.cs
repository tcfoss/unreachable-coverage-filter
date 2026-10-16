using System.Globalization;
using System.Xml.Linq;

namespace TcfOss.UnreachableCoverageFilter;

public static class OpenCoverSequenceExtensions
{
    extension(XElement element)
    {
        public string GetString(string attributeName)
        {
            return (string?)element.Attribute(attributeName)
                   ?? throw new InvalidDataException($"Missing {attributeName} on {element.Name}");
        }

        public int GetNumber(string attributeName)
        {
            return int.Parse(element.GetString(attributeName), CultureInfo.InvariantCulture);
        }

        public int VisitCount()
        {
            return element.GetNumber("vc");
        }

        public int StartLine()
        {
            return element.GetNumber("sl");
        }

        public int EndLine()
        {
            return element.GetNumber("el");
        }
    }
}
