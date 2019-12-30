﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml;

using MassEffectModManagerCore.modmanager.helpers;
using Serilog;

namespace MassEffectModManagerCore.modmanager.me3tweaks
{
    [Localizable(false)]
    partial class OnlineContent
    {
        private static readonly string LatestHelpFileLink = StaticFilesBaseURL + "dynamichelp/latesthelp-localized.xml";
        internal static readonly string HelpResourcesBaseURL = StaticFilesBaseURL + "dynamichelp/resources";

        public static List<SortableHelpElement> FetchLatestHelp(string language, bool preferLocal, bool overrideThrottling = false)
        {
            var localHelpExists = File.Exists(Utilities.GetLocalHelpFile());
            if (localHelpExists && preferLocal)
            {
                return ParseLocalHelp(File.ReadAllText(Utilities.GetLocalHelpFile()), language);
            }


            if (!localHelpExists || overrideThrottling || Utilities.CanFetchContentThrottleCheck())
            {
                try
                {
                    using var wc = new System.Net.WebClient();
                    string xml = wc.DownloadStringAwareOfEncoding(LatestHelpFileLink);
                    File.WriteAllText(Utilities.GetLocalHelpFile(), xml);
                    return ParseLocalHelp(File.ReadAllText(Utilities.GetLocalHelpFile()), language);
                }
                catch (Exception e)
                {
                    //Unable to fetch latest help.
                    Log.Error("Error fetching online help: " + e.Message);

                    if (File.Exists(Utilities.GetLocalHelpFile()))
                    {
                        Log.Warning("Using cached help instead");
                        return ParseLocalHelp(File.ReadAllText(Utilities.GetLocalHelpFile()), language);
                    }
                    else
                    {
                        Log.Error("Unable to display dynamic help: Could not fetch online asset and cached help asset does not exist.");
                        return null;
                    }
                }
            }

            return ParseLocalHelp(File.ReadAllText(Utilities.GetLocalHelpFile()), language);
        }

        private static List<SortableHelpElement> ParseLocalHelp(string xml, string language)
        {
            //We can't use LINQ here as we have to multiselect
            //And parse things in a certain order
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                var sortableHelpItems = new List<SortableHelpElement>();
                //Get top level items, in order
                string xpathExpression = $"/localizations/helpmenu[@lang='{language}']/helpitem|/localizations/helpmenu[@lang='{language}']/list";

                var nodes = doc.SelectNodes(xpathExpression);
                foreach (XmlNode node in nodes)
                {
                    var title = node.Attributes.GetNamedItem("title");
                    if (title != null)
                    {
                        var helpItem = new SortableHelpElement(node);
                        sortableHelpItems.Add(helpItem);
                    }
                }
                sortableHelpItems.Sort();
                return sortableHelpItems;
            }
            catch (Exception e)
            {
                Log.Error("ERROR IN LOCAL HELP FILE: " + e.Message);
                return new List<SortableHelpElement>();
            }
        }
    }

    [Localizable(false)]
    public class SortableHelpElement : IComparable<SortableHelpElement>
    {
        private string priority;
        internal string Title;
        internal string ToolTip;
        internal string URL;
        internal string ModalTitle;
        internal string ModalIcon;
        internal string ModalText;
        internal string ResourceName;
        internal string ResourceMD5;
        internal int ResourceSize;
        internal List<SortableHelpElement> Children = new List<SortableHelpElement>();
        public SortableHelpElement(XmlNode source)
        {
            this.priority = source.Attributes.GetNamedItem("sort")?.InnerText;
            this.Title = source.Attributes.GetNamedItem("title")?.InnerText;
            this.ToolTip = source.Attributes.GetNamedItem("tooltip")?.InnerText;
            this.URL = source.Attributes.GetNamedItem("url")?.InnerText;

            //Modal help items
            this.ModalTitle = source.Attributes.GetNamedItem("modaltitle")?.InnerText;
            this.ModalIcon = source.Attributes.GetNamedItem("modalicon")?.InnerText;
            this.ModalText = source.InnerText;
            this.ResourceName = source.Attributes.GetNamedItem("resource")?.InnerText;
            this.ResourceMD5 = source.Attributes.GetNamedItem("md5")?.InnerText;

            string resourceSizeStr = source.Attributes.GetNamedItem("size")?.InnerText;
            if (resourceSizeStr != null)
            {
                this.ResourceSize = int.Parse(resourceSizeStr);
            }

            //Download resource if any
            if (ResourceName != null)
            {
                //Validate locally
                var localFile = Path.Combine(Utilities.GetLocalHelpResourcesDirectory(), ResourceName);
                if (!File.Exists(localFile))
                {
                    //Download
                    using (var wc = new System.Net.WebClient())
                    {
                        var fullURL = OnlineContent.HelpResourcesBaseURL + "/" + ResourceName;
                        Log.Information("Downloading dynamic help image asset: " + fullURL);
                        wc.DownloadFile(fullURL, localFile);
                    }

                    var md5OfDownloadedFile = Utilities.CalculateMD5(localFile);
                    if (md5OfDownloadedFile != ResourceMD5)
                    {
                        Log.Error($"Downloaded asset has wrong hash. Expected: {ResourceMD5}, got: {md5OfDownloadedFile}");
                        File.Delete(localFile);
                    }
                    else
                    {
                        Log.Information("Downloaded resource passed md5 check");
                    }
                }
            }

            foreach (XmlNode v in source.ChildNodes)
            {
                if (v.NodeType == XmlNodeType.Element)
                {
                    Children.Add(new SortableHelpElement(v));
                }
            }
        }
        public int CompareTo(SortableHelpElement other)
        {
            return getPriorityValue(priority).CompareTo(getPriorityValue(other.priority));
        }

        private int getPriorityValue(string priority)
        {
            if (string.IsNullOrEmpty(priority))
                return 0;
            switch (priority)
            {
                case "low":
                    return 1;
                case "medium":
                    return 0;
                case "high":
                    return -1;
                default:
                    return 0;
            }
        }
    }
}
