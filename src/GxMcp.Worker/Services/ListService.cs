using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class ListService
    {
        private readonly KbService _kbService;
        private readonly IndexCacheService _indexCacheService;

        public ListService(KbService kbService, IndexCacheService indexCacheService)
        {
            _kbService = kbService;
            _indexCacheService = indexCacheService;
        }

        // v2.3.8 (Task 2.2): test-only seam — drive ListService with just an
        // IndexCacheService (no KB). Used by ListDiscoveryTests with the
        // LoadFromEntries fixture.
        public ListService(IndexCacheService indexCacheService)
            : this(null, indexCacheService)
        {
        }

        // v2.3.8 (Task 2.2): structured criteria for the new name/description/path
        // filters. Existing callers keep using ListObjects(...); this overload is
        // the supported entrypoint for unit tests and future callers that want
        // typed args. NameFilter matches name only, DescriptionFilter matches
        // description only, PathPrefix is a case-insensitive StartsWith over
        // ParentFolderPath (e.g. "Root Module/ClickSign/"). Legacy Filter still
        // matches both name and description.
        public string List(ListCriteria c)
        {
            if (c == null) c = new ListCriteria();
            return ListObjects(
                filter: c.Filter,
                limit: c.Limit,
                offset: c.Offset,
                parentFilter: null,
                typeFilter: c.TypeFilter,
                parentPathFilter: null,
                verbose: c.Verbose,
                invokerNameFilter: c.NameFilter,
                invokerDescriptionFilter: c.DescriptionFilter,
                invokerPathPrefix: c.PathPrefix);
        }

        public string ListObjects(string filter, int limit, int offset, string parentFilter = null, string typeFilter = null, string parentPathFilter = null, bool verbose = false, string invokerNameFilter = null, string invokerDescriptionFilter = null, string invokerPathPrefix = null)
        {
            var sw = Stopwatch.StartNew();
            string source = "none";
            string Finalize(string response)
            {
                sw.Stop();
                Logger.Debug($"[ListService] source={source} limit={limit} offset={offset} parentPath='{parentPathFilter ?? ""}' parent='{parentFilter ?? ""}' typeFilter='{typeFilter ?? ""}' filter='{filter ?? ""}' nameFilter='{invokerNameFilter ?? ""}' descriptionFilter='{invokerDescriptionFilter ?? ""}' pathPrefix='{invokerPathPrefix ?? ""}' verbose={verbose} elapsedMs={sw.ElapsedMilliseconds}");
                return response;
            }

            try
            {
                var array = new JArray();

                // Parse filter: can be a comma-separated list of types or a partial name
                var filterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string nameFilter = null;

                if (!string.IsNullOrEmpty(filter))
                {
                    if (filter.Contains(","))
                    {
                        foreach (var t in filter.Split(',')) filterTypes.Add(t.Trim());
                    }
                    else if (IsLikelyType(filter))
                    {
                        filterTypes.Add(filter.Trim());
                    }
                    else
                    {
                        nameFilter = filter.Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(typeFilter))
                {
                    foreach (var t in typeFilter.Split(','))
                    {
                        var trimmed = t.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) filterTypes.Add(trimmed);
                    }
                }

                var index = _indexCacheService.GetIndex();
                if (index != null && index.Objects.Count > 0)
                {
                    IEnumerable<SearchIndex.IndexEntry> entries;
                    source = "index-all";

                    if (!string.IsNullOrWhiteSpace(parentPathFilter) &&
                        index.ChildrenByParent != null &&
                        index.ChildrenByParent.TryGetValue(parentPathFilter, out var childrenByPath))
                    {
                        entries = childrenByPath;
                        source = "index-parentPath";
                    }
                    else if (!string.IsNullOrWhiteSpace(parentPathFilter))
                    {
                        entries = Enumerable.Empty<SearchIndex.IndexEntry>();
                        source = "index-parentPath-miss";
                    }
                    else if (!string.IsNullOrWhiteSpace(parentFilter) &&
                             index.ChildrenByParent != null &&
                             index.ChildrenByParent.TryGetValue(parentFilter, out var childrenByParent))
                    {
                        entries = childrenByParent;
                        source = "index-parent";
                    }
                    else if (!string.IsNullOrWhiteSpace(parentFilter))
                    {
                        entries = Enumerable.Empty<SearchIndex.IndexEntry>();
                        source = "index-parent-miss";
                    }
                    else
                    {
                        entries = index.Objects.Values;
                    }

                    if (filterTypes.Count > 0)
                    {
                        entries = entries.Where(e => filterTypes.Contains(e.Type ?? string.Empty));
                    }

                    // Legacy filter: matches on EITHER name or description (kept
                    // for backward compatibility). Prefer the targeted nameFilter
                    // / descriptionFilter / pathPrefix parameters below.
                    if (!string.IsNullOrEmpty(nameFilter))
                    {
                        entries = entries.Where(e =>
                            (e.Name ?? string.Empty).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (e.Description ?? string.Empty).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    // v2.3.8 (Task 2.2): targeted discovery filters.
                    // The "nameFilter" parameter on this method historically
                    // refers to the legacy filter token derived from the user's
                    // `filter` arg (matches name OR description). The function
                    // arguments below — invokerNameFilter / invokerDescriptionFilter / invokerPathPrefix —
                    // come from the new `nameFilter`/`descriptionFilter`/`pathPrefix`
                    // tool args and match exactly one column each.
                    if (!string.IsNullOrEmpty(invokerNameFilter))
                    {
                        entries = entries.Where(e =>
                            (e.Name ?? string.Empty).IndexOf(invokerNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    if (!string.IsNullOrEmpty(invokerDescriptionFilter))
                    {
                        entries = entries.Where(e =>
                            (e.Description ?? string.Empty).IndexOf(invokerDescriptionFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    if (!string.IsNullOrEmpty(invokerPathPrefix))
                    {
                        entries = entries.Where(e =>
                            (e.ParentFolderPath ?? string.Empty).StartsWith(invokerPathPrefix, StringComparison.OrdinalIgnoreCase));
                    }

                    var orderedIndexEntries = entries
                        .OrderBy(e => GetTypeSortBucket(e.Type))
                        .ThenBy(e => e.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(e => e.Type ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    int totalIndex = orderedIndexEntries.Count;
                    int startIndex = Math.Max(0, offset);
                    int pageSize = limit <= 0 ? int.MaxValue : limit;
                    foreach (var entry in orderedIndexEntries
                        .Skip(startIndex)
                        .Take(pageSize))
                    {
                        array.Add(BuildItem(
                            entry.Name,
                            entry.Type ?? "Unknown",
                            entry.Description,
                            entry.Parent ?? string.Empty,
                            entry.Module ?? string.Empty,
                            entry.Path ?? string.Empty,
                            entry.ParentPath ?? string.Empty,
                            entry.ParentFolderPath ?? string.Empty,
                            verbose
                        ));
                    }

                    var paged = BuildPagedResponseInternal(array, totalIndex, startIndex, pageSize);
                    // Empty typeFilter result: hand back the distinct types present so the agent finds the canonical name.
                    if (array.Count == 0 && filterTypes.Count > 0 && index.Objects.Count > 0)
                    {
                        var distinctTypes = index.Objects.Values
                            .Select(e => e.Type ?? string.Empty)
                            .Where(t => !string.IsNullOrEmpty(t))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                            .Take(60)
                            .ToArray();
                        var meta = paged["_meta"] as JObject ?? new JObject();
                        meta["typesAvailable"] = new JArray(distinctTypes);
                        meta["filterHint"] = "typeFilter='" + string.Join(",", filterTypes) + "' matched nothing. See typesAvailable for canonical type names actually present in this KB.";
                        paged["_meta"] = meta;
                    }
                    return Finalize(paged.ToString());
                }

                source = "runtime-sdk";
                var kb = _kbService.GetKB();
                if (kb == null) return Finalize("{\"error\":\"KB not open\"}");
                if (kb.DesignModel == null) return Finalize("{\"error\":\"KB DesignModel is null\"}");
                var objects = kb.DesignModel.Objects;
                if (objects == null) return Finalize("{\"error\":\"KB DesignModel.Objects is null\"}");

                var allObjects = ((System.Collections.IEnumerable)objects.GetAll())
                    .Cast<global::Artech.Architecture.Common.Objects.KBObject>();

                var filteredObjects = allObjects
                    .Select(obj => new RuntimeListEntry
                    {
                        Object = obj,
                        Hierarchy = ResolveHierarchy(obj),
                        TypeName = obj.TypeDescriptor?.Name ?? "Unknown",
                    });

                if (filterTypes.Count > 0)
                {
                    filteredObjects = filteredObjects.Where(x => filterTypes.Contains(x.TypeName));
                }

                if (!string.IsNullOrEmpty(nameFilter))
                {
                    filteredObjects = filteredObjects.Where(x =>
                        (x.Object.Name ?? string.Empty).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (x.Object.Description ?? string.Empty).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                // v2.3.8 (Task 2.2): targeted discovery filters on the runtime-SDK fallback path.
                if (!string.IsNullOrEmpty(invokerNameFilter))
                {
                    filteredObjects = filteredObjects.Where(x =>
                        (x.Object.Name ?? string.Empty).IndexOf(invokerNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (!string.IsNullOrEmpty(invokerDescriptionFilter))
                {
                    filteredObjects = filteredObjects.Where(x =>
                        (x.Object.Description ?? string.Empty).IndexOf(invokerDescriptionFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (!string.IsNullOrEmpty(invokerPathPrefix))
                {
                    filteredObjects = filteredObjects.Where(x =>
                    {
                        // ParentPath here is hierarchy.ParentPath (without "Root Module").
                        // Synthesize the same Root-Module-prefixed string used by ParentFolderPath.
                        var pp = x.Hierarchy.ParentPath ?? string.Empty;
                        string folderPath = string.IsNullOrEmpty(pp)
                            ? "Root Module"
                            : "Root Module/" + pp;
                        return folderPath.StartsWith(invokerPathPrefix, StringComparison.OrdinalIgnoreCase);
                    });
                }

                if (parentPathFilter != null)
                {
                    filteredObjects = filteredObjects.Where(x => string.Equals(x.Hierarchy.ParentPath, parentPathFilter, StringComparison.OrdinalIgnoreCase));
                }
                else if (!string.IsNullOrWhiteSpace(parentFilter))
                {
                    filteredObjects = filteredObjects.Where(x => string.Equals(x.Hierarchy.ParentName, parentFilter, StringComparison.OrdinalIgnoreCase));
                }

                var orderedRuntime = filteredObjects
                    .OrderBy(x => GetTypeSortBucket(x.TypeName))
                    .ThenBy(x => x.Object.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int totalRuntime = orderedRuntime.Count;
                int startRuntime = Math.Max(0, offset);
                int pageSizeRuntime = limit <= 0 ? int.MaxValue : limit;
                foreach (var item in orderedRuntime
                    .Skip(startRuntime)
                    .Take(pageSizeRuntime))
                {
                    var runtimeParentFolderPath = string.IsNullOrEmpty(item.Hierarchy.ParentPath)
                        ? "Root Module"
                        : "Root Module/" + item.Hierarchy.ParentPath;
                    array.Add(BuildItem(
                        item.Object.Name,
                        item.TypeName,
                        item.Object.Description,
                        item.Hierarchy.ParentName,
                        item.Hierarchy.ModuleName,
                        item.Hierarchy.Path,
                        item.Hierarchy.ParentPath,
                        runtimeParentFolderPath,
                        verbose
                    ));
                }

                return Finalize(BuildPagedResponseInternal(array, totalRuntime, startRuntime, pageSizeRuntime).ToString());
            }
            catch (Exception ex)
            {
                source = source + "-error";
                return Finalize("{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}");
            }
        }

        public JObject BuildPagedResponseInternal(JArray results, int total, int offset, int pageSize)
        {
            var response = new JObject();
            response["count"] = results.Count;
            response["total"] = total;
            response["offset"] = offset;
            int consumed = offset + results.Count;
            bool hasMore = consumed < total;
            response["hasMore"] = hasMore;
            if (hasMore)
            {
                response["nextOffset"] = consumed;
            }
            response["results"] = results;

            var meta = new JObject();

            // Handle empty results: determine and attach empty_reason
            if (results.Count == 0)
            {
                string emptyReason = DetermineEmptyReason(total);
                meta["empty_reason"] = emptyReason;
            }
            else
            {
                // Non-empty results: compute and attach aggregates
                var aggregates = ComputeAggregates(results);
                if (aggregates != null)
                {
                    meta["aggregates"] = aggregates;
                }

                // Add suggested_next if we have results
                var suggestion = BuildSuggestedNext(results);
                if (suggestion != null)
                {
                    meta["suggested_next"] = suggestion;
                }
            }

            // Only attach _meta if it has content
            if (meta.Count > 0)
            {
                response["_meta"] = meta;
            }

            return response;
        }

        private string DetermineEmptyReason(int total)
        {
            // If total is 0, no items match at all
            if (total == 0)
            {
                // Check if KB is loaded by trying to get it
                // In test contexts where _kbService is null, default to "no_matches"
                if (_kbService != null)
                {
                    var kb = _kbService.GetKB();
                    if (kb == null || kb.DesignModel == null || kb.DesignModel.Objects == null)
                    {
                        return "kb_not_loaded";
                    }
                }

                // KB is loaded but no objects match (either no filter applied or filter matched nothing)
                // We can't directly tell if a filter was applied from this context,
                // so we default to "no_matches"
                return "no_matches";
            }

            // total > 0 but results.Count == 0 means a filter was applied and filtered everything out
            return "filtered_out";
        }

        private JObject ComputeAggregates(JArray items)
        {
            if (items == null || items.Count == 0)
                return null;

            var aggregates = new JObject();

            // total: count of items in the current page result
            aggregates["total"] = items.Count;

            // by_type: group items by type and count each type
            var typeGrouping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Cast<JObject>())
            {
                var type = item["type"]?.ToString() ?? "Unknown";
                if (typeGrouping.ContainsKey(type))
                {
                    typeGrouping[type]++;
                }
                else
                {
                    typeGrouping[type] = 1;
                }
            }

            var byTypeObj = new JObject();
            foreach (var kvp in typeGrouping.OrderBy(x => x.Key))
            {
                byTypeObj[kvp.Key] = kvp.Value;
            }
            aggregates["by_type"] = byTypeObj;

            // Note: modified_last_7d is skipped because IndexEntry does not have timestamp data
            // and KBObject does not expose modification time through the public API

            return aggregates;
        }

        public static JObject BuildSuggestedNext(JArray items)
        {
            if (items == null || items.Count == 0)
                return null;

            var top = items[0] as JObject;
            if (top == null)
                return null;

            return new JObject
            {
                ["tool"] = "genexus_read",
                ["args"] = new JObject
                {
                    ["name"] = top["name"]?.ToString(),
                    ["type"] = top["type"]?.ToString()
                }
            };
        }

        public static JObject BuildItemForTest(string name, string type, string description, string parent, string module, string path, string parentPath, bool verbose = false)
        {
            return BuildItemInternal(name, type, description, parent, module, path, parentPath, null, verbose);
        }

        // Test helper: allows tests to call BuildPagedResponse with mocked data
        // Note: This uses null for _kbService, so DetermineEmptyReason will always return "no_matches" for empty results
        public static JObject BuildPagedResponseForTest(JArray items, int total, int offset, int pageSize)
        {
            var svc = new ListService(null, null);
            return svc.BuildPagedResponseInternal(items, total, offset, pageSize);
        }

        private JObject BuildItem(string name, string type, string description, string parent, string module, string path, string parentPath, string parentFolderPath, bool verbose = false)
        {
            return BuildItemInternal(name, type, description, parent, module, path, parentPath, parentFolderPath, verbose);
        }

        private static JObject BuildItemInternal(string name, string type, string description, string parent, string module, string path, string parentPath, string parentFolderPath, bool verbose = false)
        {
            var item = new JObject();
            item["name"] = name;
            item["type"] = type;

            // Check if we're in legacy mode (MCP_PERF_PROFILE=legacy means V1Enabled=false)
            string perfProfile = Environment.GetEnvironmentVariable("MCP_PERF_PROFILE");
            bool isLegacyMode = !string.IsNullOrWhiteSpace(perfProfile) &&
                               string.Equals(perfProfile, "legacy", StringComparison.OrdinalIgnoreCase);

            // In legacy mode, always return full shape for backward compatibility
            if (isLegacyMode || verbose)
            {
                item["description"] = description;
                item["parent"] = parent;
                item["module"] = module;
                item["path"] = path;
                item["parentPath"] = parentPath;
            }
            else
            {
                // Minimal shape (4 fields): name, type, path, parent
                item["path"] = path;
                item["parent"] = parent;
            }

            // v2.3.8 (Task 2.2): always expose parentFolderPath when known so the
            // agent can pathPrefix-filter the next call without round-tripping
            // through verbose mode.
            if (!string.IsNullOrEmpty(parentFolderPath))
            {
                item["parentFolderPath"] = parentFolderPath;
            }

            return item;
        }

        private HierarchyInfo ResolveHierarchy(dynamic obj)
        {
            string parentName = string.Empty;
            string moduleName = null;
            var parentSegments = new List<string>();

            try
            {
                dynamic currentParent = obj.Parent;
                bool isImmediateParent = true;

                while (currentParent != null)
                {
                    try
                    {
                        if (currentParent.Guid == obj.Guid)
                        {
                            break;
                        }
                    }
                    catch
                    {
                    }

                    string parentTypeName = null;
                    try { parentTypeName = currentParent.TypeDescriptor?.Name; } catch { }

                    if (string.Equals(parentTypeName, "DesignModel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isImmediateParent)
                        {
                            parentName = "Root Module";
                        }
                        break;
                    }

                    if (currentParent is global::Artech.Architecture.Common.Objects.Module ||
                        currentParent is global::Artech.Architecture.Common.Objects.Folder)
                    {
                        string currentName = null;
                        try { currentName = currentParent.Name; } catch { }

                        if (!string.IsNullOrWhiteSpace(currentName))
                        {
                            parentSegments.Insert(0, currentName);
                            if (isImmediateParent)
                            {
                                parentName = currentName;
                            }

                            if (moduleName == null &&
                                currentParent is global::Artech.Architecture.Common.Objects.Module)
                            {
                                moduleName = currentName;
                            }
                        }
                    }

                    currentParent = currentParent.Parent;
                    isImmediateParent = false;
                }
            }
            catch { }

            try
            {
                if (moduleName == null && obj.Module != null && obj.Module.Guid != obj.Guid)
                {
                    moduleName = obj.Module.Name;
                }
            }
            catch
            {
            }

            string parentPath = string.Join("/", parentSegments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
            string resolvedPath = string.IsNullOrWhiteSpace(obj.Name)
                ? parentPath
                : string.IsNullOrEmpty(parentPath) ? (string)obj.Name : parentPath + "/" + (string)obj.Name;

            return new HierarchyInfo
            {
                ParentName = parentName,
                ParentPath = parentPath,
                Path = resolvedPath,
                ModuleName = moduleName ?? string.Empty,
            };
        }

        private bool IsLikelyType(string s)
        {
            var types = new[] { "Folder", "Module", "Procedure", "Transaction", "WebPanel", "Attribute", "Table", "DataView", "Domain", "WorkPanel", "ExternalObject", "Menu", "SDPanel", "DataProvider", "SDT", "StructuredDataType", "Image" };
            return types.Any(t => string.Equals(t, s, StringComparison.OrdinalIgnoreCase));
        }

        private int GetTypeSortBucket(string type)
        {
            if (string.Equals(type, "Folder", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Module", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return 1;
        }

        private sealed class RuntimeListEntry
        {
            public global::Artech.Architecture.Common.Objects.KBObject Object { get; set; }
            public HierarchyInfo Hierarchy { get; set; }
            public string TypeName { get; set; }
        }

        private sealed class HierarchyInfo
        {
            public string ParentName { get; set; }
            public string ParentPath { get; set; }
            public string Path { get; set; }
            public string ModuleName { get; set; }
        }
    }

    // v2.3.8 (Task 2.2): typed criteria for ListService.List. Mirrors the
    // tool-schema args of genexus_list_objects.
    public class ListCriteria
    {
        // Substring match on object NAME only.
        public string NameFilter { get; set; }
        // Substring match on object DESCRIPTION only.
        public string DescriptionFilter { get; set; }
        // Case-insensitive StartsWith over ParentFolderPath, e.g. "Root Module/ClickSign/".
        public string PathPrefix { get; set; }
        // Legacy: matches name OR description (kept for backward compatibility).
        public string Filter { get; set; }
        public string TypeFilter { get; set; }
        public int Limit { get; set; } = 200;
        public int Offset { get; set; } = 0;
        public bool Verbose { get; set; } = false;
    }
}
