using System.Xml.Linq;

namespace VistaDotNetProjectHelper;

public class PackageReferenceProps
{
    public XElement Element { get; set; }
    public string? Version { get; set; }
}