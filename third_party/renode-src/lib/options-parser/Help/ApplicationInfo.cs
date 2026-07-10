using System;
using System.IO;
using System.Reflection;

namespace Antmicro.OptionsParser
{
    public class ApplicationInfo
    {
        public ApplicationInfo()
        {
            var entryAssembly = Assembly.GetEntryAssembly();

            var applicationNameAttribute = entryAssembly?.GetCustomAttribute<AssemblyTitleAttribute>();
            if(applicationNameAttribute != null)
            {
                ApplicationName = applicationNameAttribute.Title;
            }

            // assembly version is not available through custom attribute...
            ApplicationVersion = entryAssembly?.GetName().Version.ToString();

            var applicationCopyrightAttribute = entryAssembly?.GetCustomAttribute<AssemblyCopyrightAttribute>();
            if(applicationCopyrightAttribute != null)
            {
                ApplicationCopyrights = applicationCopyrightAttribute.Copyright;
            }

            ApplicationBinaryName = AppDomain.CurrentDomain.FriendlyName;

            var metadataAttributes = entryAssembly?.GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach(var attribute in metadataAttributes)
            {
                switch(attribute.Key)
                {
                    case "BinaryName":
                        ApplicationBinaryName = attribute.Value;
                        break;
                }
            }
        }

        public string ApplicationName { get; private set; }
        public string ApplicationBinaryName { get; private set; }
        public string ApplicationVersion { get; private set; }
        public string ApplicationCopyrights { get; private set; }
    }
}

