using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Assets_Editor
{
    /// <summary>Migrates outfits from old server outfits.xml to Canary format (data/XML/outfits.xml).</summary>
    public static class OutfitsMigration
    {
        public sealed class MigratedOutfit
        {
            public ushort Type { get; set; }
            public ushort LookType { get; set; }
            public string Name { get; set; } = "";
            public string Premium { get; set; } = "no";
            public string Unlocked { get; set; } = "yes";
            public string Enabled { get; set; } = "yes";
            public string? From { get; set; }
        }

        /// <summary>Run migration: old outfits.xml → Canary outfits.xml.</summary>
        public static int Migrate(
            string oldOutfitsXmlPath,
            string outputOutfitsXmlPath,
            IProgress<(int current, int total, string message)>? progress = null)
        {
            if (string.IsNullOrEmpty(oldOutfitsXmlPath) || !File.Exists(oldOutfitsXmlPath))
                throw new FileNotFoundException("Old outfits.xml not found.", oldOutfitsXmlPath);

            progress?.Report((0, 1, "Loading old outfits.xml..."));
            var list = ParseOldOutfitsXml(oldOutfitsXmlPath);

            progress?.Report((0, list.Count, "Writing Canary outfits.xml..."));
            int written = WriteCanaryOutfitsXml(outputOutfitsXmlPath, list, progress);
            progress?.Report((written, written, "Done."));
            return written;
        }

        public static List<MigratedOutfit> ParseOldOutfitsXml(string path)
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "outfits")
                return new List<MigratedOutfit>();

            var result = new List<MigratedOutfit>();
            foreach (var node in root.Elements("outfit"))
            {
                var o = new MigratedOutfit();
                if (node.Attribute("type") is XAttribute typeAttr && ushort.TryParse(typeAttr.Value, out ushort type))
                    o.Type = type;
                if (node.Attribute("looktype") is XAttribute lookAttr && ushort.TryParse(lookAttr.Value, out ushort look))
                    o.LookType = look;
                if (node.Attribute("name") is XAttribute nameAttr)
                    o.Name = nameAttr.Value ?? "";
                if (node.Attribute("premium") is XAttribute premAttr)
                    o.Premium = (premAttr.Value ?? "no").Trim().ToLowerInvariant();
                if (node.Attribute("unlocked") is XAttribute unAttr)
                    o.Unlocked = (unAttr.Value ?? "yes").Trim().ToLowerInvariant();
                if (node.Attribute("enabled") is XAttribute enAttr)
                    o.Enabled = (enAttr.Value ?? "yes").Trim().ToLowerInvariant();
                if (node.Attribute("from") is XAttribute fromAttr && !string.IsNullOrWhiteSpace(fromAttr.Value))
                    o.From = fromAttr.Value.Trim();
                result.Add(o);
            }
            return result;
        }

        private static int WriteCanaryOutfitsXml(
            string path,
            List<MigratedOutfit> list,
            IProgress<(int current, int total, string message)>? progress)
        {
            var outfitsEl = new XElement("outfits");
            int count = 0;
            foreach (var o in list)
            {
                var attrs = new List<XAttribute>
                {
                    new XAttribute("type", o.Type),
                    new XAttribute("looktype", o.LookType),
                    new XAttribute("name", o.Name),
                    new XAttribute("premium", o.Premium),
                    new XAttribute("unlocked", o.Unlocked),
                    new XAttribute("enabled", o.Enabled)
                };
                if (!string.IsNullOrEmpty(o.From))
                    attrs.Add(new XAttribute("from", o.From));
                outfitsEl.Add(new XElement("outfit", attrs));
                count++;
                if (count % 100 == 0)
                    progress?.Report((count, list.Count, $"Writing outfit {o.LookType}..."));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), outfitsEl);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t",
                Encoding = new System.Text.UTF8Encoding(false),
                OmitXmlDeclaration = false
            };
            using (var writer = XmlWriter.Create(path, settings))
                doc.Save(writer);
            return count;
        }
    }
}
