using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;
using DotLiquid;

namespace VistaDotNetProjectHelper;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string TargetFrameworks = "TargetFrameworks";
    private const string AssemblyName = "AssemblyName";
    private const string RootNamespace = "RootNamespace";
    private const string GenerateAssemblyInfo = "GenerateAssemblyInfo";
    private const string GenerateDocumentationFile = "GenerateDocumentationFile";
    private const string LangVersion = "LangVersion";
    private const string TreatWarningsAsErrors = "TreatWarningsAsErrors";
    private const string NoWarn = "NoWarn";
    private const string WarningsAsErrors = "WarningsAsErrors";
    private const string Project = "Project";
    private const string PropertyGroup = "PropertyGroup";
    private const string Condition = "Condition";
    private const string DefineConstants = "DefineConstants";
    private const string PackageReference = "PackageReference";
    private const string AdditionalFiles = "AdditionalFiles";
    private const string Include = "Include";
    private const string Version = "Version";
    private const string Link = "Link";
    private const string ItemGroup = "ItemGroup";
    private const string StyleCopJsonFileName = "stylecop.json";
    private const string GitIgnoreFileName = ".gitignore";


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

        var sourceDirectory = GetSourceDirectory();

        if (!Directory.Exists(sourceDirectory))
        {
            MessageBox.Show($"Source directory {sourceDirectory} does not exist", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        ProjectsListBox.Items.Clear();

        foreach (var projectDirectory in Directory.GetDirectories(sourceDirectory))
            if (Directory.GetFiles(projectDirectory, "*.csproj").Length == 1)
                ProjectsListBox.Items.Add(projectDirectory.Split(Path.DirectorySeparatorChar).Last());
    }


    private void MigrateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStyleCop();
            SetGitIgnore();

            var sourceDirectory = GetSourceDirectory();

            foreach (string directory in ProjectsListBox.SelectedItems)
            {
                var projectDirectory = Path.Join(sourceDirectory, directory);
                var csprojFile = Directory.GetFiles(projectDirectory, "*.csproj").First();

                SetAssemblyInfo(projectDirectory, csprojFile);

                var xDocument = XDocument.Load(csprojFile);

                SetPropertyGroups(xDocument, csprojFile);
                SetItemGroup(xDocument);

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

    private void SetStyleCop()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.stylecop.liquid");
        using var streamReader = new StreamReader(stream!);

        var assemblyInfoContent = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), StyleCopJsonFileName), assemblyInfoContent);
    }

    private void SetGitIgnore()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.gitignore.liquid");
        using var streamReader = new StreamReader(stream!);

        var assemblyInfoContent = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), GitIgnoreFileName), assemblyInfoContent);
    }

    private static void SetAssemblyInfo(string projectDirectory, string csprojFile)
    {
        var propertiesDirectory = Path.Join(projectDirectory, "Properties");

        if (!Directory.Exists(propertiesDirectory))
            throw new Exception($"Properties {propertiesDirectory} directory does not exist!!!");

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.AssemblyInfo.liquid");
        using var streamReader = new StreamReader(stream!);

        var assemblyInfoContent = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
                assemblyTitle = GetAssemblyName(csprojFile),
                assemblyProduct = GetAssemblyName(csprojFile),
                year = DateTime.UtcNow.Year.ToString()
            }));

        File.WriteAllText(Path.Join(propertiesDirectory, "AssemblyInfo.cs"), assemblyInfoContent);
    }

    private void SetPropertyGroups(XContainer xDocument, string csprojFile)
    {
        SetTargetFrameworks(xDocument);
        SetGlobalPropertyGroupItems(xDocument, csprojFile);
        SetDebugPropertyGroupItems(xDocument);
        SetReleasePropertyGroupItems(xDocument);
    }

    private static void SetItemGroup(XContainer xDocument)
    {
        var additionalFiles = GetAdditionalFiles(xDocument);

        foreach (var key in additionalFiles.Keys.Where(key => key.Contains(StyleCopJsonFileName)))
            additionalFiles[key].Element.Remove();

        var itemGroup = GetItemGroup(xDocument);

        if (itemGroup == null)
        {
            var projectElement = xDocument.Descendants(Project).First();

            projectElement.Add(new XElement(ItemGroup));

            itemGroup =
                GetPropertyGroup(xDocument);
        }

        itemGroup!.Add(new XElement(AdditionalFiles,
            new XAttribute(Include, Path.Join("..", "..", StyleCopJsonFileName)),
            new XAttribute(Link, StyleCopJsonFileName)));
    }

    private static void SetGlobalPropertyGroupItems(XContainer xDocument, string csprojFile)
    {
        var attributes = new List<Tuple<string, string>>
        {
            new(AssemblyName, GetAssemblyName(csprojFile)),
            new(RootNamespace, GetAssemblyName(csprojFile)),
            new(GenerateAssemblyInfo, "false"),
            new(GenerateDocumentationFile, "true"),
            new(LangVersion, "latest"),
            new(TreatWarningsAsErrors, "true"),
            new(NoWarn, string.Empty),
            new(WarningsAsErrors, string.Empty)
        };

        var propertyGroup = xDocument.Descendants(PropertyGroup)
            .First(propertyGroup => !propertyGroup.Attributes().Any());

        var propertyGroupsElements = GetPropertyGroupsElements(xDocument);

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
    }

    private void SetDebugPropertyGroupItems(XContainer xDocument)
    {
        var debugPropertyGroup =
            GetPropertyGroup(xDocument, Condition, "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'");

        if (debugPropertyGroup == null)
        {
            var projectElement = xDocument.Descendants(Project).First();

            projectElement.Add(new XElement(PropertyGroup,
                new XAttribute(Condition, "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'")));

            debugPropertyGroup =
                GetPropertyGroup(xDocument, Condition, "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'");
        }

        var debugPropertyGroupsElements =
            GetPropertyGroupsElements(xDocument, Condition, "'$(Configuration)|$(Platform)'=='Debug|AnyCPU'");

        var debugDefineConstantsValues =
            ShouldIncludeCodeOfConduct(xDocument) ? "TRACE;DEBUG;VISTA_CODE_CONTRACTS" : "TRACE;DEBUG";

        if (debugPropertyGroupsElements.TryGetValue(DefineConstants, out var debugDefineConstantsElement))
            debugDefineConstantsElement.Value = debugDefineConstantsValues;
        else
            debugPropertyGroup!.Add(new XElement(DefineConstants, debugDefineConstantsValues));
    }

    private void SetReleasePropertyGroupItems(XContainer xDocument)
    {
        var releasePropertyGroup =
            GetPropertyGroup(xDocument, Condition, "'$(Configuration)|$(Platform)'=='Release|AnyCPU'");

        if (releasePropertyGroup == null)
        {
            var projectElement = xDocument.Descendants(Project).First();

            projectElement.Add(new XElement(PropertyGroup,
                new XAttribute(Condition, "'$(Configuration)|$(Platform)'=='Release|AnyCPU'")));

            releasePropertyGroup =
                GetPropertyGroup(xDocument, Condition, "'$(Configuration)|$(Platform)'=='Release|AnyCPU'");
        }

        var releasePropertyGroupElements =
            GetPropertyGroupsElements(xDocument, Condition, "'$(Configuration)|$(Platform)'=='Release|AnyCPU'");

        var releaseDefineConstantsValues =
            ShouldIncludeCodeOfConduct(xDocument) ? "TRACE;VISTA_CODE_CONTRACTS" : "TRACE";

        if (releasePropertyGroupElements.TryGetValue(DefineConstants, out var releaseDefineConstantsElement))
            releaseDefineConstantsElement.Value = releaseDefineConstantsValues;
        else
            releasePropertyGroup!.Add(new XElement(DefineConstants, releaseDefineConstantsValues));
    }

    private static void SetTargetFrameworks(XContainer xDocument)
    {
        var propertyGroupsElements = GetPropertyGroupsElements(xDocument);

        if (propertyGroupsElements.TryGetValue(TargetFrameworks, out var targetFrameworks))
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
            throw new Exception($"No {TargetFrameworks} found!!!");
        }
    }

    private static IDictionary<string, XElement> GetPropertyGroupsElements(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(PropertyGroup)
            .Where(propertyGroup => string.IsNullOrWhiteSpace(attributeName)
                ? !propertyGroup.Attributes().Any()
                : propertyGroup.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue))
            .SelectMany(resource => resource.Elements())
            .ToDictionary(element => element.Name.LocalName, child => child);
    }

    private static XElement? GetPropertyGroup(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(PropertyGroup)
            .FirstOrDefault(propertyGroup => string.IsNullOrWhiteSpace(attributeName)
                ? !propertyGroup.Attributes().Any()
                : propertyGroup.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue));
    }

    private static IDictionary<string, PackageReferenceProps> GetPackageReferences(XContainer xDocument)
    {
        return xDocument
            .Descendants(ItemGroup)
            .SelectMany(resource => resource.Elements())
            .Where(element => element.Name.LocalName == PackageReference)
            .ToDictionary(
                element => element.Attribute(Include)!.Value,
                element => new PackageReferenceProps
                {
                    Element = element,
                    Version = element.Attribute(Version)?.Value
                });
    }

    private static IDictionary<string, XElement> GetItemGroupsElements(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(ItemGroup)
            .Where(propertyGroup => string.IsNullOrWhiteSpace(attributeName)
                ? !propertyGroup.Attributes().Any()
                : propertyGroup.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue))
            .SelectMany(resource => resource.Elements())
            .ToDictionary(element => element.Name.LocalName, child => child);
    }

    private static XElement? GetItemGroup(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(ItemGroup)
            .FirstOrDefault(propertyGroup => string.IsNullOrWhiteSpace(attributeName)
                ? !propertyGroup.Attributes().Any()
                : propertyGroup.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue));
    }

    private static IDictionary<string, AdditionalItemsProps> GetAdditionalFiles(XContainer xDocument)
    {
        return xDocument
            .Descendants(ItemGroup)
            .SelectMany(resource => resource.Elements())
            .Where(element => element.Name.LocalName == AdditionalFiles)
            .ToDictionary(
                element => element.Attribute(Include)!.Value,
                element => new AdditionalItemsProps
                {
                    Element = element,
                    Link = element.Attribute(Link)?.Value
                });
    }

    private string GetDirectoryPath()
    {
        return SolutionDirectoryPathTextBox.Text;
    }

    private string GetSourceDirectory()
    {
        return Path.Join(SolutionDirectoryPathTextBox.Text, "src");
    }

    private bool ShouldIncludeCodeOfConduct(XContainer xDocument)
    {
        return GetPackageReferences(xDocument).ContainsKey("Vista.CodeContracts");
    }

    private static string GetAssemblyName(string csprojFile)
    {
        return Path.GetFileNameWithoutExtension(csprojFile);
    }
}