using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace GxMcp.Gateway
{
    // Item 56: import an object from another KB at the FILESYSTEM level.
    // Full SDK-routed import (resolve dependencies via genexus_analyze
    // mode=impact on the source KB) is structurally infeasible from the
    // gateway because the per-KB worker can only host one open KB at a time —
    // opening the source KB inside the active worker would evict the target
    // and break the import. Shipping the limited-but-real filesystem variant:
    // copy <source>/Objects/<Type>/<Name>/* to <target>/Objects/<Type>/<Name>/.
    // Caller is expected to follow up with genexus_lifecycle action=index
    // (force=true) so the SDK picks up the new object.
    internal static class KbImportHelper
    {
        public static JObject ImportObject(string sourceKbPath, string targetKbPath, string name, string type)
        {
            string sourceObjDir = Path.Combine(sourceKbPath, "Objects", type, name);
            if (!Directory.Exists(sourceObjDir))
            {
                return new JObject
                {
                    ["status"] = "Error",
                    ["code"] = "ObjectNotFound",
                    ["message"] = $"Object '{type}:{name}' not found at {sourceObjDir}.",
                    ["hint"] = "Check 'type' (case-sensitive) and 'name'; pass exact directory names from Objects/<Type>/<Name>/."
                };
            }
            string targetObjDir = Path.Combine(targetKbPath, "Objects", type, name);
            bool overwrote = Directory.Exists(targetObjDir);
            try
            {
                if (overwrote)
                {
                    Directory.Delete(targetObjDir, true);
                }
                Directory.CreateDirectory(targetObjDir);
                int files = 0;
                long bytes = 0;
                foreach (var f in new DirectoryInfo(sourceObjDir).GetFiles("*", SearchOption.TopDirectoryOnly))
                {
                    string targetFile = Path.Combine(targetObjDir, f.Name);
                    f.CopyTo(targetFile, overwrite: true);
                    files++;
                    bytes += f.Length;
                }
                return new JObject
                {
                    ["status"] = "Success",
                    ["code"] = "PartialFeature",
                    ["name"] = name,
                    ["type"] = type,
                    ["filesCopied"] = files,
                    ["bytesCopied"] = bytes,
                    ["overwroteExisting"] = overwrote,
                    ["targetPath"] = targetObjDir,
                    ["dependencies"] = new JArray(), // see hint below
                    ["hint"] = "Filesystem-level import only — dependencies are NOT resolved. " +
                               "After import, run genexus_lifecycle action=index force=true to refresh the worker's " +
                               "index, then validate references via genexus_analyze mode=impact target=" + name + "."
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["status"] = "Error",
                    ["code"] = "IoError",
                    ["message"] = ex.Message
                };
            }
        }
    }
}
