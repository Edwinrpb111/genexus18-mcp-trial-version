using System;
using System.Collections.Generic;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public interface IKbObjectInfo
    {
        string Guid { get; }
        string Name { get; }
        string Type { get; }
        string ParentPath { get; }
        string Path { get; }
        string Description { get; }
        string Module { get; }
    }

    public class KbObjectStub : IKbObjectInfo
    {
        public string Guid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string ParentPath { get; set; } = "";
        public string Path { get; set; } = "";
        public string Description { get; set; } = "";
        public string Module { get; set; } = "";
    }

    public class LiteIndexBuilder
    {
        public IEnumerable<SearchIndex.IndexEntry> Build(IEnumerable<IKbObjectInfo> objects)
        {
            foreach (var obj in objects)
            {
                yield return new SearchIndex.IndexEntry
                {
                    Guid = obj.Guid,
                    Name = obj.Name,
                    Type = obj.Type,
                    ParentPath = obj.ParentPath,
                    Path = string.IsNullOrEmpty(obj.Path) ? (obj.ParentPath + "/" + obj.Name) : obj.Path,
                    Description = obj.Description,
                    Module = obj.Module,
                    IsEnriched = false
                };
            }
        }
    }
}
