using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Parsers
{
    public class TransactionDslParser : IDslParser
    {
        public void Serialize(KBObject obj, StringBuilder sb)
        {
            if (obj is Transaction trn)
            {
                SerializeLevel(trn.Structure.Root, sb, 0);
            }
        }

        public void Parse(KBObject obj, string text)
        {
            if (obj is Transaction trn)
            {
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();
                var parsedNodes = DslParserUtils.ParseLinesIntoNodes(lines);
                
                var targetNodes = parsedNodes;
                if (parsedNodes.Count == 1 && parsedNodes[0].IsCompound)
                {
                    targetNodes = parsedNodes[0].Children;
                }

                SyncTransactionNodes(trn.Structure.Root, targetNodes, obj.Model);
            }
        }

        private void SerializeLevel(dynamic level, StringBuilder sb, int indent)
        {
            string indentStr = new string(' ', indent * 4);
            if (indent > 0)
            {
                sb.AppendLine($"{indentStr}{level.Name}");
                sb.AppendLine($"{indentStr}{{");
            }

            if (level.Attributes != null)
            {
                foreach (dynamic attr in level.Attributes)
                {
                    string keyMarker = attr.IsKey ? "*" : "";
                    string typeStr = "Unknown";
                    try {
                        if (attr.Type != null) typeStr = attr.Type.ToString();
                    } catch {
                        try { if (attr.Attribute != null && attr.Attribute.Type != null) typeStr = attr.Attribute.Type.ToString(); } catch { }
                    }

                    try {
                        if (attr.Attribute != null) {
                            int len = attr.Attribute.Length;
                            int dec = attr.Attribute.Decimals;
                            if (len > 0) {
                                if (dec > 0) typeStr += $"({len},{dec})";
                                else typeStr += $"({len})";
                            }
                        }
                    } catch { }

                    string desc = "";
                    string formula = "";
                    bool isNullable = false;
                    try {
                        if (attr.Attribute != null) {
                            desc = attr.Attribute.Description?.ToString() ?? "";
                            formula = attr.Attribute.Formula?.ToString() ?? "";
                            dynamic pNullable = attr.Attribute.Properties.Get("Nullable");
                            if (pNullable != null) {
                                string nVal = pNullable.ToString();
                                isNullable = nVal.Equals("Yes", StringComparison.OrdinalIgnoreCase) || nVal.Equals("Nullable", StringComparison.OrdinalIgnoreCase);
                            }
                        }
                    } catch { }

                    var lineElements = new List<string>();
                    lineElements.Add(string.Format("{0}{1} : {2}", attr.Name, keyMarker, typeStr));
                    if (!string.IsNullOrEmpty(desc) && !desc.Equals(attr.Name, StringComparison.OrdinalIgnoreCase)) lineElements.Add(string.Format("\"{0}\"", desc));
                    if (!string.IsNullOrEmpty(formula)) lineElements.Add(string.Format("[Formula: {0}]", formula));
                    if (isNullable) lineElements.Add("[Nullable]");

                    string extraInfo = lineElements.Count > 1 ? " // " + string.Join(", ", lineElements.Skip(1)) : "";
                    sb.AppendLine(string.Format("{0}{1}{2}{3}", indentStr, indent > 0 ? "    " : "", lineElements[0], extraInfo));
                }
            }

            if (level.Levels != null)
            {
                foreach (dynamic subLevel in level.Levels) SerializeLevel(subLevel, sb, indent + 1);
            }

            if (indent > 0) sb.AppendLine($"{indentStr}}}");
        }

        private void SyncTransactionNodes(dynamic sdkLevel, List<DslParserUtils.ParsedNode> parsedNodes, KBModel model)
        {
            var existingItems = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            if (sdkLevel.Attributes != null) { foreach (dynamic attr in sdkLevel.Attributes) existingItems[attr.Name] = attr; }

            if (sdkLevel.Attributes != null)
            {
                var toRemove = new List<dynamic>();
                foreach (dynamic attr in sdkLevel.Attributes) {
                    if (!parsedNodes.Any(p => !p.IsCompound && p.Name.Equals(attr.Name, StringComparison.OrdinalIgnoreCase))) toRemove.Add(attr);
                }
                foreach (dynamic dead in toRemove) { try { sdkLevel.Attributes.Remove(dead); } catch {} }
            }

            foreach (var pNode in parsedNodes)
            {
                if (pNode.IsCompound)
                {
                    dynamic targetSubLevel = null;
                    if (sdkLevel.Levels != null) {
                        foreach (dynamic subLvl in sdkLevel.Levels) { if (subLvl.Name.Equals(pNode.Name, StringComparison.OrdinalIgnoreCase)) { targetSubLevel = subLvl; break; } }
                    }

                    if (targetSubLevel == null)
                    {
                        Type levelType = sdkLevel.GetType().Assembly.GetType("Artech.Genexus.Common.Objects.TransactionLevel");
                        if (levelType != null) {
                            targetSubLevel = Activator.CreateInstance(levelType, new object[] { sdkLevel });
                            targetSubLevel.Name = pNode.Name;
                            try { sdkLevel.Levels.Add(targetSubLevel); } catch { }
                        }
                    }
                    if (targetSubLevel != null) SyncTransactionNodes(targetSubLevel, pNode.Children, model);
                }
                else
                {
                    if (existingItems.TryGetValue(pNode.Name, out var existing)) existing.IsKey = pNode.IsKey;
                    else
                    {
                        Type attrType = sdkLevel.GetType().Assembly.GetType("Artech.Genexus.Common.Objects.TransactionAttribute");
                        if (attrType != null) {
                            try {
                                var globalAttr = Artech.Genexus.Common.Objects.Attribute.Get(model, pNode.Name);
                                dynamic trnAttr;
                                if (globalAttr != null)
                                    trnAttr = Activator.CreateInstance(attrType, new object[] { sdkLevel.Structure, globalAttr });
                                else
                                    trnAttr = Activator.CreateInstance(attrType, new object[] { sdkLevel });
                                
                                trnAttr.Name = pNode.Name;
                                trnAttr.IsKey = pNode.IsKey;
                                sdkLevel.Attributes.Add(trnAttr);
                            } catch { }
                        }
                    }
                }
            }
        }
    }
}
