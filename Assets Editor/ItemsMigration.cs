using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Google.Protobuf;
using Tibia.Protobuf.Appearances;

namespace Assets_Editor
{
    /// <summary>Migrates items from old server format (items.otb + items.xml) to Canary format (items.xml).</summary>
    public static class ItemsMigration
    {
        public sealed class MigratedItem
        {
            public ushort Id { get; set; }
            public string Name { get; set; } = "";
            public string Article { get; set; } = "";
            public string Plural { get; set; } = "";
            public List<(string Key, string Value)> Attributes { get; } = new();
        }

        /// <summary>Run migration: old items.xml (+ optional OTB) -> Canary items.xml.</summary>
        /// <param name="oldItemsXmlPath">Path to old server items.xml.</param>
        /// <param name="oldOtbPath">Optional path to old server items.otb; if set, only items present in OTB are migrated.</param>
        /// <param name="outputItemsXmlPath">Path for output Canary items.xml.</param>
        /// <param name="progress">Optional progress (current count, total, message).</param>
        /// <returns>Number of items written.</returns>
        public static int Migrate(
            string oldItemsXmlPath,
            string? oldOtbPath,
            string? targetAppearancesPath,
            string outputItemsXmlPath,
            IProgress<(int current, int total, string message)>? progress = null)
        {
            if (string.IsNullOrEmpty(oldItemsXmlPath) || !File.Exists(oldItemsXmlPath))
                throw new FileNotFoundException("Old items.xml not found.", oldItemsXmlPath);

            progress?.Report((0, 1, "Loading old items.xml..."));
            var items = ParseOldItemsXml(oldItemsXmlPath);

            Dictionary<ushort, OTB.ServerItem>? oldOtbItemsByServerId = null;
            if (!string.IsNullOrEmpty(oldOtbPath) && File.Exists(oldOtbPath))
            {
                progress?.Report((0, 2, "Loading old OTB (server id -> client id)..."));
                var reader = new OTBReader();
                if (reader.Read(oldOtbPath))
                    oldOtbItemsByServerId = reader.Items
                        .GroupBy(i => (ushort)i.ServerId)
                        .ToDictionary(g => g.Key, g => g.First());
            }

            if (oldOtbItemsByServerId != null)
            {
                items = RemapItemsToClientIds(items, oldOtbItemsByServerId, progress);
            }

            if (!string.IsNullOrEmpty(targetAppearancesPath) && File.Exists(targetAppearancesPath))
            {
                progress?.Report((0, 3, "Loading Canary appearance.dat (validation)..."));
                var validAppearanceIds = LoadAppearanceObjectIds(targetAppearancesPath);
                items = ValidateIdsAgainstAppearances(items, validAppearanceIds, progress);
            }

            progress?.Report((0, 3, "Merging duplicate ids (keep name, description, attributes)..."));
            items = MergeItemsById(items);

            progress?.Report((0, items.Count, "Writing Canary items.xml..."));
            int written = WriteCanaryItemsXml(outputItemsXmlPath, items, progress);
            progress?.Report((written, written, "Done."));
            return written;
        }

        /// <summary>Parse old server items.xml; supports id and fromid/toid.</summary>
        public static List<MigratedItem> ParseOldItemsXml(string path)
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "items")
                return new List<MigratedItem>();

            var result = new List<MigratedItem>();
            foreach (var itemNode in root.Elements("item"))
            {
                if (itemNode.Attribute("id") is XAttribute idAttr && ushort.TryParse(idAttr.Value, out ushort id))
                {
                    result.Add(ParseOneItem(itemNode, id));
                    continue;
                }
                if (itemNode.Attribute("fromid") is XAttribute fromAttr && itemNode.Attribute("toid") is XAttribute toAttr
                    && ushort.TryParse(fromAttr.Value, out ushort fromId) && ushort.TryParse(toAttr.Value, out ushort toId))
                {
                    for (ushort i = fromId; i <= toId; i++)
                        result.Add(ParseOneItem(itemNode, i));
                }
            }
            return result.OrderBy(x => x.Id).ToList();
        }

        private static MigratedItem ParseOneItem(XElement itemNode, ushort id)
        {
            var m = new MigratedItem { Id = id };
            if (itemNode.Attribute("name") is XAttribute nameAttr)
                m.Name = nameAttr.Value ?? "";
            if (itemNode.Attribute("article") is XAttribute articleAttr)
                m.Article = articleAttr.Value ?? "";
            if (itemNode.Attribute("plural") is XAttribute pluralAttr)
                m.Plural = pluralAttr.Value ?? "";

            foreach (var attrNode in itemNode.Elements("attribute"))
            {
                string? key = attrNode.Attribute("key")?.Value;
                string? value = attrNode.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(key))
                    m.Attributes.Add((key, value ?? ""));
            }
            return m;
        }

        private static List<MigratedItem> RemapItemsToClientIds(
            List<MigratedItem> items,
            Dictionary<ushort, OTB.ServerItem> oldOtbItemsByServerId,
            IProgress<(int current, int total, string message)>? progress)
        {
            int mapped = 0;
            int keptOriginalId = 0;
            int missingInOtb = 0;

            foreach (var item in items)
            {
                if (!oldOtbItemsByServerId.TryGetValue(item.Id, out var oldOtbItem) || oldOtbItem.ClientId == 0)
                {
                    missingInOtb++;
                    keptOriginalId++;
                    continue;
                }

                if (item.Id != oldOtbItem.ClientId)
                {
                    item.Id = oldOtbItem.ClientId;
                    mapped++;
                }
            }

            progress?.Report((mapped, items.Count,
                $"Remapped {mapped} ids from old server ids to old client ids. Kept {keptOriginalId} original ids. Missing in OTB: {missingInOtb}."));

            return items;
        }

        private static HashSet<ushort> LoadAppearanceObjectIds(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var appearances = Appearances.Parser.ParseFrom(stream);
            return appearances.Object
                .Where(o => o.Id > 0)
                .Select(o => (ushort)o.Id)
                .ToHashSet();
        }

        private static List<MigratedItem> ValidateIdsAgainstAppearances(
            List<MigratedItem> items,
            HashSet<ushort> validAppearanceIds,
            IProgress<(int current, int total, string message)>? progress)
        {
            int valid = 0;
            int missing = 0;

            foreach (var item in items)
            {
                if (validAppearanceIds.Contains(item.Id))
                    valid++;
                else
                    missing++;
            }

            progress?.Report((valid, items.Count,
                $"Validated ids against appearance.dat. Found {valid} matching item ids and {missing} ids missing from appearance objects."));

            return items;
        }

        /// <summary>Merge all entries with the same id: take first non-empty name/article/plural, merge attributes (last value wins per key).</summary>
        private static List<MigratedItem> MergeItemsById(List<MigratedItem> items)
        {
            return items
                .GroupBy(m => m.Id)
                .Select(g =>
                {
                    var list = g.ToList();
                    var merged = new MigratedItem { Id = list[0].Id };
                    foreach (var x in list)
                    {
                        if (!string.IsNullOrWhiteSpace(x.Name)) merged.Name = x.Name.Trim();
                        if (!string.IsNullOrWhiteSpace(x.Article)) merged.Article = x.Article.Trim();
                        if (!string.IsNullOrWhiteSpace(x.Plural)) merged.Plural = x.Plural.Trim();
                        foreach (var (key, value) in x.Attributes)
                        {
                            var nk = NormalizeKey(key);
                            var idx = merged.Attributes.FindIndex(a => NormalizeKey(a.Key) == nk);
                            if (idx >= 0)
                                merged.Attributes[idx] = (merged.Attributes[idx].Key, value);
                            else
                                merged.Attributes.Add((key, value));
                        }
                    }
                    return merged;
                })
                .OrderBy(m => m.Id)
                .ToList();
        }

        /// <summary>Canary parses attribute keys as lowercase; normalize for output.</summary>
        private static string NormalizeKey(string key) => key.ToLowerInvariant();

        /// <summary>Skip attributes that Canary does not support or that cause warnings.</summary>
        private static bool ShouldSkipAttribute(string key, string value)
        {
            var k = NormalizeKey(key);
            var v = value.Trim().ToLowerInvariant();
            if (k == "corpsetype") return true;
            if (k == "ispokeball") return true;
            if (k == "marketcategory") return true;
            if (k == "fluidsource" && (v == "lava" || v == "lavastatic")) return true;
            if (k == "slottype" && v == "info") return true;
            if (k == "transformdeequipto" && (v == "0" || string.IsNullOrEmpty(v))) return true;
            return false;
        }

        private static int WriteCanaryItemsXml(
            string path,
            List<MigratedItem> items,
            IProgress<(int current, int total, string message)>? progress)
        {
            var decl = new XDeclaration("1.0", "ISO-8859-1", null);
            var itemsEl = new XElement("items");
            int count = 0;
            foreach (var m in items)
            {
                var itemEl = new XElement("item",
                    new XAttribute("id", m.Id));
                if (!string.IsNullOrEmpty(m.Name))
                    itemEl.Add(new XAttribute("name", m.Name));
                if (!string.IsNullOrEmpty(m.Article))
                    itemEl.Add(new XAttribute("article", m.Article));
                if (!string.IsNullOrEmpty(m.Plural))
                    itemEl.Add(new XAttribute("plural", m.Plural));

                foreach (var (key, value) in m.Attributes)
                {
                    if (ShouldSkipAttribute(key, value)) continue;
                    itemEl.Add(new XElement("attribute",
                        new XAttribute("key", NormalizeKey(key)),
                        new XAttribute("value", value)));
                }

                itemsEl.Add(itemEl);
                count++;
                if (count % 500 == 0)
                    progress?.Report((count, items.Count, $"Writing item {m.Id}..."));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                itemsEl);
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
