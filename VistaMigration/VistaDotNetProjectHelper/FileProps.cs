using System.Xml.Linq;

namespace VistaDotNetProjectHelper;

public class FileProps
{
    public XElement Element { get; set; }
    public string? Src { get; set; }
    public string? Target { get; set; }
}