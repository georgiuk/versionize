using System;
using System.IO;
using System.Text;
using System.Xml;
using Version = NuGet.Versioning.SemanticVersion;

namespace Versionize
{
    public class Project
    {
        public string ProjectFile { get; }
        public Version Version { get; }

        private Project(string projectFile, Version version)
        {
            ProjectFile = projectFile;
            Version = version;
        }
        public static Project Create(string projectFile)
        {
            return Create(projectFile, false);
        }
        
        public static Project Create(string projectFile, bool skipCurrentVersion)
        {
            Version version = null;
            if (!skipCurrentVersion)
                version = ReadVersion(projectFile);

            return new Project(projectFile, version);
        }

        public static bool IsVersionable(string projectFile)
        {
            try
            {
                ReadVersion(projectFile);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Version ReadVersion(string projectFile)
        {
            XmlDocument doc = new XmlDocument {PreserveWhitespace = true};

            try
            {
                doc.Load(projectFile);
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"Project {projectFile} is not a valid csproj file. Please make sure that you have a valid csproj file in place!");
            }

            var versionString = doc.SelectSingleNode("/Project/PropertyGroup/Version")?.InnerText;

            if (string.IsNullOrWhiteSpace(versionString))
            {
                throw new InvalidOperationException($"Project {projectFile} contains no or an empty <Version> XML Element. Please add one if you want to version this project - for example use <Version>1.0.0</Version>");
            }

            try
            {
                return Version.Parse(versionString);
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"Project {projectFile} contains an invalid version {versionString}. Please fix the currently contained version - for example use <Version>1.0.0</Version>");
            }
        }

        public void WriteVersion(Version nextVersion)
        {
            var doc = new XmlDocument {PreserveWhitespace = true};

            try
            {
                doc.Load(ProjectFile);
            }
            catch (Exception)
            {
                throw new InvalidOperationException($"Project {ProjectFile} is not a valid csproj file. Please make sure that you have a valid csproj file in place!");
            }

            var propNode = doc.SelectSingleNode("/Project/PropertyGroup");

            var versionElement = doc.SelectSingleNode("/Project/PropertyGroup/Version");
            if (versionElement != null)
            {
                versionElement.InnerText = nextVersion.ToString();
            }
            else
            {
                versionElement = doc.CreateElement("Version");
                versionElement.InnerText = nextVersion.ToString();
                propNode.AppendChild(versionElement);
                propNode.AppendChild(doc.CreateWhitespace(Environment.NewLine));
            }

            var assemblyVersionElement = doc.SelectSingleNode("/Project/PropertyGroup/AssemblyVersion");
            if (assemblyVersionElement != null)
            {
                assemblyVersionElement.InnerText = $"{nextVersion.Major}.0.0.0";
            }
            else
            {
                assemblyVersionElement = doc.CreateElement("AssemblyVersion");
                assemblyVersionElement.InnerText = $"{nextVersion.Major}.0.0.0";
                propNode.AppendChild(assemblyVersionElement);
                propNode.AppendChild(doc.CreateWhitespace(Environment.NewLine));
            }

            var assemblyFileVersionElement = doc.SelectSingleNode("/Project/PropertyGroup/FileVersion");
            if (assemblyFileVersionElement != null)
            {
                assemblyFileVersionElement.InnerText = $"{nextVersion.Major}.{nextVersion.Minor}.{nextVersion.Patch}.0";
            }
            else
            {
                assemblyFileVersionElement = doc.CreateElement("FileVersion");
                assemblyFileVersionElement.InnerText = $"{nextVersion.Major}.{nextVersion.Minor}.{nextVersion.Patch}.0";
                propNode.AppendChild(assemblyFileVersionElement);
                propNode.AppendChild(doc.CreateWhitespace(Environment.NewLine));
            }
            var assemblyInformationalVersionElement = doc.SelectSingleNode("/Project/PropertyGroup/InformationalVersion");
            if (assemblyInformationalVersionElement != null)
            {
                assemblyInformationalVersionElement.InnerText = nextVersion.ToFullString();
            }
            else
            {
                assemblyInformationalVersionElement = doc.CreateElement("InformationalVersion");
                assemblyInformationalVersionElement.InnerText = nextVersion.ToFullString();
                propNode.AppendChild(assemblyInformationalVersionElement);
                propNode.AppendChild(doc.CreateWhitespace(Environment.NewLine));
            }



            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "\t";

            using (Stream stream = new FileStream(ProjectFile, FileMode.Open, FileAccess.ReadWrite)) //or FileStream to write an Xml file directly to disk
            {
                XmlWriter writer = XmlWriter.Create(stream, settings);
                doc.WriteTo(writer);

                writer.Close();
            }


            //doc.Save(ProjectFile);
        }
    }
}
