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
    private const string ProjectVersion = "Version";
    private const string Link = "Link";
    private const string ItemGroup = "ItemGroup";
    private const string PrivateAssets = "PrivateAssets";
    private const string IncludeAssets = "IncludeAssets";
    private const string DebugType = "DebugType";
    private const string DebugSymbols = "DebugSymbols";
    private const string DefaultItemExcludes = "DefaultItemExcludes";

    private const string Metadata = "metadata";
    private const string Id = "id";
    private const string NuspecVersion = "version";
    private const string RequireLicenseAcceptance = "requireLicenseAcceptance";
    private const string Files = "files";
    private const string Src = "src";
    private const string Target = "target";
    private const string FileConstant = "file";
    private const string Package = "package";

    private const string StyleCopJsonFileName = "stylecop.json";
    private const string GitIgnoreFileName = ".gitignore";
    private const string EditorConfigFileName = ".editorconfig";
    private const string GitAttributesFileName = ".gitattributes";
    private const string NugetConfigFileName = "nuget.config";

    private const string PipelineVersionConstant = "$VersionNuGet$";
    private const string PipelineDirSrcConstant = "$DirSrc$";
    private const string PipelineConfigurationConstant = "$configuration$";
    private static readonly XNamespace NuspecNamespace = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";


    public MainWindow()
    {
        InitializeComponent();

        LoadProjectsButton_Click(null, null);
        ProjectsListBox.SelectedItems.Add(ProjectsListBox.Items[0]);
        ProjectsListBox.SelectedItems.Add(ProjectsListBox.Items[1]);
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
            SetEditorConfig();
            SetGitAttributes();
            SetNugetConfig();

            var sourceDirectory = GetSourceDirectory();

            foreach (string directory in ProjectsListBox.SelectedItems)
            {
                var projectDirectory = Path.Join(sourceDirectory, directory);
                var csprojFile = Directory.GetFiles(projectDirectory, "*.csproj").First();

                SetAssemblyInfo(projectDirectory, csprojFile);

                SetProjectFile(csprojFile);
                CheckAndCreateNuspecFile(csprojFile);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Exception: {exception.Message}", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void SetProjectFile(string csprojFile)
    {
        var xDocument = XDocument.Load(csprojFile);

        SetPropertyGroups(xDocument, csprojFile);
        SetPackageReferences(xDocument);
        SetItemGroup(xDocument);

        xDocument.Descendants()
            .Where(element => (element.IsEmpty || string.IsNullOrWhiteSpace(element.Value)) &&
                              element.Name.LocalName is PropertyGroup or ItemGroup &&
                              !element.Nodes().Any())
            .Remove();

        using var xmlWriter = XmlWriter.Create(csprojFile, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true
        });

        xDocument.Save(xmlWriter);
    }

    private static void CheckAndCreateNuspecFile(string csprojFile)
    {
        var xDocument = XDocument.Load(csprojFile);

        if (csprojFile.Contains("UnitTests", StringComparison.InvariantCultureIgnoreCase)) return;

        var nuspecFileName = $"{Path.GetFileNameWithoutExtension(csprojFile)}.nuspec";
        var nuspecFilePath = Path.Join(Path.GetDirectoryName(csprojFile), nuspecFileName);

        if (!File.Exists(nuspecFilePath)) throw new Exception($"File {nuspecFilePath} does not exist");

        var nuspecXDocument = XDocument.Load(nuspecFilePath);

        SetMetadataItems(nuspecXDocument, csprojFile);
        SetAllFiles(xDocument, nuspecXDocument, csprojFile);

        foreach (var element in nuspecXDocument.Descendants())
        {
            if (element.Name.LocalName == Package)
                continue;

            element.Name = element.Name.LocalName;
        }

        using (var xmlWriter = XmlWriter.Create(nuspecFilePath, new XmlWriterSettings
               {
                   Indent = true
               }))
        {
            nuspecXDocument.Save(xmlWriter);
        }

        File.WriteAllText(nuspecFilePath,
            File.ReadAllText(nuspecFilePath)
                .Replace(" xmlns=\"\"", string.Empty));
    }

    private void SetStyleCop()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.stylecop.liquid");
        using var streamReader = new StreamReader(stream!);

        var content = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), StyleCopJsonFileName), content);
    }

    private void SetGitIgnore()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.gitignore.liquid");
        using var streamReader = new StreamReader(stream!);

        var content = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), GitIgnoreFileName), content);
    }

    private void SetEditorConfig()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.editorconfig.liquid");
        using var streamReader = new StreamReader(stream!);

        var content = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), EditorConfigFileName), content);
    }

    private void SetGitAttributes()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.gitattributes.liquid");
        using var streamReader = new StreamReader(stream!);

        var content = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), GitAttributesFileName), content);
    }

    private void SetNugetConfig()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VistaDotNetProjectHelper.Templates.nuget.config.liquid");
        using var streamReader = new StreamReader(stream!);

        var content = DotLiquid.Template.Parse(
                streamReader.ReadToEnd())
            .Render(Hash.FromAnonymousObject(new
            {
            }));

        File.WriteAllText(Path.Join(GetDirectoryPath(), NugetConfigFileName), content);
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

    private static void SetPropertyGroups(XContainer xDocument, string csprojFile)
    {
        SetTargetFrameworks(xDocument);
        SetGlobalPropertyGroupItems(xDocument, csprojFile);
        SetDebugPropertyGroupItems(xDocument);
        SetReleasePropertyGroupItems(xDocument);
        AddDefaultItemExcludes(xDocument);
    }

    private static void SetItemGroup(XContainer xDocument)
    {
        AddStyleCop(xDocument);
    }

    private static void SetPackageReferences(XContainer xDocument)
    {
        AddCodeAnalysisPackages(xDocument);
    }

    private static void AddStyleCop(XContainer xDocument)
    {
        var elements = GetAdditionalFiles(xDocument);

        foreach (var key in elements.Keys.Where(key => key.Contains(StyleCopJsonFileName)))
            elements[key].Element.Remove();

        var itemGroup = new XElement(ItemGroup);

        itemGroup!.Add(new XElement(AdditionalFiles,
            new XAttribute(Include, Path.Join("..", "..", StyleCopJsonFileName)),
            new XAttribute(Link, StyleCopJsonFileName)));

        xDocument.Descendants(Project).First().Add(itemGroup);
    }

    private static void AddDefaultItemExcludes(XContainer xDocument)
    {
        var excludeItemsList = new List<string> { "$(DefaultItemExcludes);*.ncrunchproject" };

        var elements = GetDefaultItemExcludes(xDocument);

        foreach (var key in elements.Keys.Where(key => excludeItemsList.Contains(key)))
            elements[key].Remove();

        var propertyGroup = new XElement(PropertyGroup);

        foreach (var excludeItem in excludeItemsList)
            propertyGroup!.Add(new XElement(DefaultItemExcludes, excludeItem));

        xDocument.Descendants(Project).First().Add(propertyGroup);
    }

    private static void AddCodeAnalysisPackages(XContainer xDocument)
    {
        var packages = new List<CodeAnalyzerPackageProps>
        {
            new()
            {
                Name = "Microsoft.CodeAnalysis.FxCopAnalyzers",
                Version = "2.9.8",
                PrivateAssets = "all",
                IncludeAssets = "runtime; build; native; contentfiles; analyzers"
            },
            new()
            {
                Name = "StyleCop.Analyzers",
                Version = "1.1.118",
                PrivateAssets = "all",
                IncludeAssets = "runtime; build; native; contentfiles; analyzers"
            },
            new()
            {
                Name = "Vista.CodeAnalysisRuleSet.FxCop",
                Version = "2.1.0",
                PrivateAssets = "all",
                IncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive"
            },
            new()
            {
                Name = "Vista.CodeAnalysisRuleSet.Global",
                Version = "2.1.0",
                PrivateAssets = "all",
                IncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive"
            },
            new()
            {
                Name = "Vista.CodeAnalysisRuleSet.StyleCop",
                Version = "2.1.0",
                PrivateAssets = "all",
                IncludeAssets = "runtime; build; native; contentfiles; analyzers; buildtransitive"
            }
        };

        var elements = GetPackageReferences(xDocument);

        foreach (var key in elements.Keys.Where(key => packages.Any(package => package.Name == key)))
            elements[key].Element.Remove();


        var packageReferencesToAdd = packages.Select(package =>
        {
            var privateAssets = new XElement(PrivateAssets, package.PrivateAssets);
            var includeAssets = new XElement(IncludeAssets, package.IncludeAssets);

            var packageReference = new XElement(
                PackageReference,
                new XAttribute(Include, package.Name!),
                new XAttribute(ProjectVersion, package.Version!));

            packageReference.Add(privateAssets);
            packageReference.Add(includeAssets);

            return packageReference;
        }).ToList();

        var itemGroup = new XElement(ItemGroup);

        foreach (var packageReferenceToAdd in packageReferencesToAdd) itemGroup!.Add(packageReferenceToAdd);

        xDocument.Descendants(Project).First().Add(itemGroup);
    }

    private static void SetGlobalPropertyGroupItems(XContainer xDocument, string csprojFile)
    {
        var itemsToOverride = new List<Tuple<string, string>>
        {
            new(AssemblyName, GetAssemblyName(csprojFile)),
            new(RootNamespace, GetAssemblyName(csprojFile)),
            new(GenerateAssemblyInfo, "false"),
            new(GenerateDocumentationFile, "true"),
            new(LangVersion, "10"),
            new(TreatWarningsAsErrors, "true"),
            new(NoWarn, string.Empty),
            new(WarningsAsErrors, string.Empty)
        };

        var propertyGroup = xDocument.Descendants(PropertyGroup)
            .First(element => !element.Attributes().Any());

        var elements = GetPropertyGroupsElements(xDocument);

        foreach (var itemToOverride in itemsToOverride)
            if (elements.TryGetValue(itemToOverride.Item1, out var element))
            {
                if (string.IsNullOrWhiteSpace(itemToOverride.Item2))
                {
                    element.Remove();
                    propertyGroup.Add(new XElement(itemToOverride.Item1));
                }
                else
                {
                    element.Value = itemToOverride.Item2;
                }
            }
            else
            {
                propertyGroup.Add(string.IsNullOrWhiteSpace(itemToOverride.Item2)
                    ? new XElement(itemToOverride.Item1)
                    : new XElement(itemToOverride.Item1, itemToOverride.Item2));
            }
    }

    private static void SetDebugPropertyGroupItems(XContainer xDocument)
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

    private static void SetReleasePropertyGroupItems(XContainer xDocument)
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

        if (releasePropertyGroupElements.TryGetValue(DebugType, out var debugTypeElement))
            debugTypeElement.Value = "pdbonly";
        else
            releasePropertyGroup!.Add(new XElement(DebugType, "pdbonly"));

        if (releasePropertyGroupElements.TryGetValue(DebugSymbols, out var debugSymbolsElement))
            debugSymbolsElement.Value = "true";
        else
            releasePropertyGroup!.Add(new XElement(DebugSymbols, "true"));
    }

    private static void SetTargetFrameworks(XContainer xDocument)
    {
        var propertyGroupsElements = GetPropertyGroupsElements(xDocument);

        if (propertyGroupsElements.TryGetValue(TargetFrameworks, out var targetFrameworks))
        {
            var targetFrameworksToAdd = new List<string> { "net461", "netstandard2.0" };
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

    private static IEnumerable<string> GetAllTargetFrameworks(XContainer xDocument)
    {
        var propertyGroupsElements = GetPropertyGroupsElements(xDocument);

        if (propertyGroupsElements.TryGetValue(TargetFrameworks, out var targetFrameworks))
            return targetFrameworks.Value.Split(";");

        throw new Exception($"No {TargetFrameworks} found!!!");
    }

    private static IDictionary<string, XElement> GetPropertyGroupsElements(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(PropertyGroup)
            .Where(element => string.IsNullOrWhiteSpace(attributeName)
                ? !element.Attributes().Any()
                : element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue))
            .SelectMany(element => element.Elements())
            .ToDictionary(element => element.Name.LocalName, child => child);
    }

    private static XElement? GetPropertyGroup(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(PropertyGroup)
            .FirstOrDefault(element => string.IsNullOrWhiteSpace(attributeName)
                ? !element.Attributes().Any()
                : element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue));
    }

    private static IDictionary<string, PackageReferenceProps> GetPackageReferences(XContainer xDocument)
    {
        return xDocument
            .Descendants(ItemGroup)
            .SelectMany(element => element.Elements())
            .Where(element => element.Name.LocalName == PackageReference)
            .ToDictionary(
                element => element.Attribute(Include)!.Value,
                element => new PackageReferenceProps
                {
                    Element = element,
                    Version = element.Attribute(ProjectVersion)?.Value
                });
    }

    private static IDictionary<string, XElement> GetItemGroupsElements(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(ItemGroup)
            .Where(element => string.IsNullOrWhiteSpace(attributeName)
                ? !element.Attributes().Any()
                : element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue))
            .SelectMany(element => element.Elements())
            .ToDictionary(element => element.Name.LocalName, child => child);
    }

    private static XElement? GetItemGroup(
        XContainer xDocument,
        string attributeName = "",
        string attributeValue = "")
    {
        return xDocument.Descendants(ItemGroup)
            .FirstOrDefault(element => string.IsNullOrWhiteSpace(attributeName)
                ? !element.Attributes().Any()
                : element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == attributeName && attribute.Value == attributeValue));
    }

    private static IDictionary<string, AdditionalItemsProps> GetAdditionalFiles(XContainer xDocument)
    {
        return xDocument
            .Descendants(ItemGroup)
            .SelectMany(element => element.Elements())
            .Where(element => element.Name.LocalName == AdditionalFiles)
            .ToDictionary(
                element => element.Attribute(Include)!.Value,
                element => new AdditionalItemsProps
                {
                    Element = element,
                    Link = element.Attribute(Link)?.Value
                });
    }

    private static IDictionary<string, XElement> GetDefaultItemExcludes(XContainer xDocument)
    {
        return xDocument
            .Descendants(PropertyGroup)
            .SelectMany(element => element.Elements())
            .Where(element => element.Name.LocalName == DefaultItemExcludes)
            .ToDictionary(
                element => element.Value,
                element => element);
    }

    private static IDictionary<string, XElement> GetMetadataElements(XContainer xDocument)
    {
        return xDocument.Descendants(NuspecNamespace + Metadata)
            .Where(element =>
                !element.Attributes().Any())
            .SelectMany(element => element.Elements())
            .ToDictionary(element => element.Name.LocalName, child => child);
    }

    private static IReadOnlyCollection<FileProps> GetAllFiles(XContainer xDocument)
    {
        return xDocument.Descendants(NuspecNamespace + Files)
            .Where(element =>
                !element.Attributes().Any())
            .SelectMany(element => element.Elements())
            .Select(element => new FileProps
            {
                Element = element,
                Src = element.Attribute(Src)!.Value,
                Target = element.Attribute(Target)!.Value
            }).ToList();
    }

    private static XElement? GetFile(XContainer xDocument)
    {
        return xDocument.Descendants(NuspecNamespace + Files).FirstOrDefault();
    }

    private static void SetMetadataItems(XContainer xDocument, string csprojFile)
    {
        var itemsToOverride = new List<Tuple<string, string>>
        {
            new(Id, GetAssemblyName(csprojFile)),
            new(NuspecVersion, PipelineVersionConstant),
            new(RequireLicenseAcceptance, "false")
        };

        var metadata = xDocument.Descendants(NuspecNamespace + "metadata").First();

        var elements = GetMetadataElements(xDocument);

        foreach (var itemToOverride in itemsToOverride)
            if (elements.TryGetValue(itemToOverride.Item1, out var element))
            {
                if (string.IsNullOrWhiteSpace(itemToOverride.Item2))
                {
                    element.Remove();
                    metadata.Add(new XElement(itemToOverride.Item1));
                }
                else
                {
                    element.Value = itemToOverride.Item2;
                }
            }
            else
            {
                metadata.Add(string.IsNullOrWhiteSpace(itemToOverride.Item2)
                    ? new XElement(itemToOverride.Item1)
                    : new XElement(itemToOverride.Item1, itemToOverride.Item2));
            }
    }

    private static void SetAllFiles(XContainer xDocument, XContainer nuspecXDocument, string csprojFile)
    {
        var allFiles = GetAllFiles(nuspecXDocument);

        foreach (var file in allFiles) file.Element.Remove();

        var allTargetFrameworks = GetAllTargetFrameworks(xDocument);

        var assemblyName = GetAssemblyName(csprojFile);

        var files = allTargetFrameworks
            .SelectMany(targetFramework =>
            {
                var fileNameWithoutExtension =
                    $"{PipelineDirSrcConstant}\\{assemblyName}\\bin\\{PipelineConfigurationConstant}\\{targetFramework}\\{assemblyName}";
                var target = $"lib\\{targetFramework}";

                return new List<XElement>
                {
                    new(FileConstant, new XAttribute(Src, $"{fileNameWithoutExtension}.dll"),
                        new XAttribute(Target, target)),
                    new(FileConstant, new XAttribute(Src, $"{fileNameWithoutExtension}.pdb"),
                        new XAttribute(Target, target)),
                    new(FileConstant, new XAttribute(Src, $"{fileNameWithoutExtension}.xml"),
                        new XAttribute(Target, target))
                };
            }).Concat(new List<XElement>
            {
                new(FileConstant, new XAttribute(Src, $"{PipelineDirSrcConstant}\\{assemblyName}\\**\\*.cs"),
                    new XAttribute(Target, $"src\\{assemblyName}"))
            })
            .ToList();

        var filesElement = GetFile(nuspecXDocument);

        foreach (var file in files) filesElement!.Add(file);
    }

    private string GetDirectoryPath()
    {
        return SolutionDirectoryPathTextBox.Text;
    }

    private string GetSourceDirectory()
    {
        return Path.Join(SolutionDirectoryPathTextBox.Text, "src");
    }

    private static bool ShouldIncludeCodeOfConduct(XContainer xDocument)
    {
        return GetPackageReferences(xDocument).ContainsKey("Vista.CodeContracts");
    }

    private static string GetAssemblyName(string csprojFile)
    {
        return Path.GetFileNameWithoutExtension(csprojFile);
    }
}