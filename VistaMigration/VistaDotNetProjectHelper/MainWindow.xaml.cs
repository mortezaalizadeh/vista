using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;

namespace VistaDotNetProjectHelper;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        LoadProjectsButton_Click(null, null);

        ProjectsListBox.SelectionMode = SelectionMode.Multiple;
        ProjectsListBox.SelectedItems.Add(ProjectsListBox.Items[0]);

        MigrateButton_Click(null, null);
    }

    private void LoadProjectsButton_Click(object sender, RoutedEventArgs e)
    {
        var directoryPath = GetDirectoryPath();

        if (!Directory.Exists(directoryPath))
        {
            MessageBox.Show($"Directory {directoryPath} does not exist", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        ProjectsListBox.Items.Clear();

        foreach (var directory in Directory.GetDirectories(directoryPath))
            if (Directory.GetFiles(directory, "*.csproj").Length == 1)
                ProjectsListBox.Items.Add(directory.Split(Path.DirectorySeparatorChar).Last());
    }

    private void MigrateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directoryPath = GetDirectoryPath();

            foreach (string directory in ProjectsListBox.SelectedItems)
            {
                var csprojFile = Directory.GetFiles(Path.Join(directoryPath, directory), "*.csproj").First();

                var xDocument = XDocument.Load(csprojFile);

                var itemGroups = GetPackageReferences(xDocument);

                var additionalFiles = GetAdditionalFiles(xDocument);

                SetPropertyGroups(xDocument, csprojFile);

                using var xmlWriter = XmlWriter.Create(csprojFile, new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true
                });

                xDocument.Save(xmlWriter);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Exception: {exception.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private static void SetPropertyGroups(XContainer xDocument, string csprojFile)
    {
        var propertyGroupsElements = GetPropertyGroupsElements(xDocument);

        if (propertyGroupsElements.TryGetValue("TargetFrameworks", out var targetFrameworks))
        {
            var targetFrameworksToAdd = new List<string> { "net461", "netstandard2.0", "net6.0" };
            var allTargetedFrameworks = targetFrameworks.Value.Split(";").ToList();

            foreach (var targetFrameworkToAdd in targetFrameworksToAdd
                         .Where(targetFrameworkToAdd => !allTargetedFrameworks.Contains(targetFrameworkToAdd)))
                allTargetedFrameworks.Add(targetFrameworkToAdd);

            allTargetedFrameworks.Sort();

            targetFrameworks.Value = string.Join(';', allTargetedFrameworks);
        }
        else
        {
            throw new Exception("No TargetFrameworks found!!!");
        }

        var attributes = new List<Tuple<string, string>>
        {
            new("AssemblyName", Path.GetFileNameWithoutExtension(csprojFile)),
            new("RootNamespace", Path.GetFileNameWithoutExtension(csprojFile)),
            new("GenerateAssemblyInfo", "false"),
            new("GenerateDocumentationFile", "true"),
            new("LangVersion", "latest"),
            new("TreatWarningsAsErrors", "true"),
            new("NoWarn", string.Empty),
            new("WarningsAsErrors", string.Empty)
        };

        var propertyGroup = xDocument.Descendants("PropertyGroup")
            .First(propertyGroup => !propertyGroup.Attributes().Any());

        foreach (var attribute in attributes)
            if (propertyGroupsElements.TryGetValue(attribute.Item1, out var element))
            {
                if (string.IsNullOrWhiteSpace(attribute.Item2))
                {
                    element.Remove();
                    propertyGroup.Add(new XElement(attribute.Item1));
                }
                else
                {
                    element.Value = attribute.Item2;
                }
            }
            else
            {
                propertyGroup.Add(string.IsNullOrWhiteSpace(attribute.Item2)
                    ? new XElement(attribute.Item1)
                    : new XElement(attribute.Item1, attribute.Item2));
            }


        var debugPropertyGroup =
            GetPropertyGroup(xDocument, "Condition", "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'");

        if (debugPropertyGroup == null)
        {
            var projectElement = xDocument.Descendants("Project").First();

            projectElement.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'")));

            debugPropertyGroup =
                GetPropertyGroup(xDocument, "Condition", "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'");
        }

        var debugPropertyGroupsElements =
            GetPropertyGroupsElements(xDocument, "Condition", "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'");

        var vistaCodeContractsUsed = GetPackageReferences(xDocument).ContainsKey("Vista.CodeContracts");

        var debugDefineConstantsValues = vistaCodeContractsUsed ? "TRACE;DEBUG;VISTA_CODE_CONTRACTS" : "TRACE;DEBUG";

        if (debugPropertyGroupsElements.TryGetValue("DefineConstants", out var debugDefineConstantsElement))
            debugDefineConstantsElement.Value = debugDefineConstantsValues;
        else
            debugPropertyGroup.Add(new XElement("DefineConstants", debugDefineConstantsValues));













        var releasePropertyGroup =
            GetPropertyGroup(xDocument, "Condition", "'$(Configuration)|$(Platform)'=='Release|AnyCPU'");

        if (releasePropertyGroup == null)
        {
            var projectElement = xDocument.Descendants("Project").First();

            projectElement.Add(new XElement("PropertyGroup",
                new XAttribute("Condition", "'$(Configuration)|$(Platform)'=='Release|AnyCPU'")));

            releasePropertyGroup =
                GetPropertyGroup(xDocument, "Condition", "'$(Configuration)|$(Platform)'=='Release|AnyCPU'");
        }

        var releasePropertyGroupElements =
            GetPropertyGroupsElements(xDocument, "Condition", "'$(Configuration)|$(Platform)'=='Release|AnyCPU'");

        var releaseDefineConstantsValues = vistaCodeContractsUsed ? "TRACE;VISTA_CODE_CONTRACTS" : "TRACE";

        if (releasePropertyGroupElements.TryGetValue("DefineConstants", out var releaseDefineConstantsElement))
            releaseDefineConstantsElement.Value = releaseDefineConstantsValues;
        else
            releasePropertyGroup.Add(new XElement("DefineConstants", releaseDefineConstantsValues));

    }

    private static Dictionary<string, XElement> GetPropertyGroupsElements(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants("PropertyGroup")
            .Where(propertyGroup => string.IsNullOrWhiteSpace(attributeName)
                ? !propertyGroup.Attributes().Any()
                : propertyGroup.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue))
            .SelectMany(resource => resource.Elements())
            .ToDictionary(child => child.Name.LocalName, child => child);
    }

    private static XElement? GetPropertyGroup(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants("PropertyGroup")
            .FirstOrDefault(propertyGroup => string.IsNullOrWhiteSpace(attributeName)
                ? !propertyGroup.Attributes().Any()
                : propertyGroup.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue));
    }

    private static IDictionary<string, string> GetPackageReferences(XContainer xDocument)
    {
        return xDocument
            .Descendants("ItemGroup")
            .SelectMany(resource => resource.Elements())
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                child => child.Attribute("Include")!.Value,
                child => child.Attribute("Version")!.Value);
    }

    private static IReadOnlyCollection<string> GetAdditionalFiles(XContainer xDocument)
    {
        return xDocument
            .Descendants("ItemGroup")
            .SelectMany(resource => resource.Elements())
            .Where(element => element.Name.LocalName == "AdditionalFiles")
            .Select(child => child.Attribute("Include")!.Value)
            .ToList();
    }

    private string GetDirectoryPath()
    {
        return SolutionDirectoryPathTextBox.Text;
    }
}