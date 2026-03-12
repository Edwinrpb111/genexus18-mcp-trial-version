using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Artech.Architecture.Common.Objects;

namespace GxMcp.Worker.Services
{
    public class KbService
    {
        private BuildService _buildService;
        private readonly IndexCacheService _indexCacheService;
        private readonly VectorService _vectorService = new VectorService();

        // Progress Tracking
        private static int _processedCount = 0;
        private static int _totalCount = 0;
        private static bool _isIndexing = false;
        private static string _currentStatus = "";

        private static dynamic _kb;
        private static bool _isOpenInProgress = false;
        private static readonly object _kbLock = new object();

        public KbService(IndexCacheService indexCacheService)
        {
            _indexCacheService = indexCacheService;
        }

        public void SetBuildService(BuildService bs) { _buildService = bs; }
        public IndexCacheService GetIndexCache() { return _indexCacheService; }
        public bool IsInitializing => _isOpenInProgress;

        public dynamic GetKB()
        {
            lock (_kbLock) 
            { 
                if (_kb == null && !_isOpenInProgress)
                {
                    string kbPath = Environment.GetEnvironmentVariable("GX_KB_PATH");
                    if (!string.IsNullOrEmpty(kbPath))
                    {
                        Logger.Info($"Auto-opening KB in background: {kbPath}");
                        System.Threading.Tasks.Task.Run(() => OpenKB(kbPath));
                    }
                }
                return _kb; 
            }
        }

        public string OpenKB(string path)
        {
            lock (_kbLock)
            {
                if (_isOpenInProgress) return "{\"status\":\"In Progress\"}";
                _isOpenInProgress = true;
                
                if (_kb != null)
                {
                    try { if (string.Equals(_kb.Location, path, StringComparison.OrdinalIgnoreCase)) { _isOpenInProgress = false; return "{\"status\":\"Success\"}"; } } catch { }
                    try { _kb.Close(); } catch { }
                }

                try {
                    Logger.Info($"Opening KB: {path}");
                    string oldDir = Directory.GetCurrentDirectory();
                    try {
                        string kbDir = Path.GetDirectoryName(path);
                        Directory.SetCurrentDirectory(kbDir);
                        
                        var options = new KnowledgeBase.OpenOptions(path);
                        _kb = KnowledgeBase.Open(options);
                        
                        Logger.Info($"KB opened successfully.");
                        return "{\"status\":\"Success\"}";
                    } finally { Directory.SetCurrentDirectory(oldDir); _isOpenInProgress = false; }
                } catch (Exception ex) { 
                    Logger.Error($"ERROR opening KB: {ex.Message}");
                    _kb = null;
                    _isOpenInProgress = false;
                    return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
                }
            }
        }

        public string BulkIndex()
        {
            Logger.Info("BulkIndex() requested (Diagnostic Mode).");
            if (_isIndexing) return "{\"status\":\"Already in progress\"}";

            _isIndexing = true;
            _processedCount = 0;
            _totalCount = 0;
            _currentStatus = "Querying Database...";

            Program.BackgroundQueue.Enqueue(() => {
                try {
                    dynamic kb = GetKB();
                    if (kb == null) { _isIndexing = false; return; }
                    
                    // [DIAG] Use the exact property name found via reflection
                    string connStr = "";
                    try {
                        dynamic connInfo = kb.ConnectionInfo;
                        connStr = connInfo.ConnectionStringFull; // FOUND!
                    } catch {}

                    if (string.IsNullOrEmpty(connStr)) {
                        Logger.Error("[SQL-DIAG] ConnectionStringFull not found on kb.ConnectionInfo");
                        _isIndexing = false; return;
                    }

                    Logger.Info("[SQL-DIAG] Executing Definitive Count Query...");
                    using (var conn = new System.Data.SqlClient.SqlConnection(connStr)) {
                        conn.Open();
                        var cmd = new System.Data.SqlClient.SqlCommand(
                            "SELECT T.EntityTypeName as TypeName, COUNT(*) as Qty " +
                            "FROM dbo.Entity E " +
                            "JOIN dbo.EntityType T ON E.EntityTypeId = T.EntityTypeId " +
                            "GROUP BY T.EntityTypeName ORDER BY Qty DESC", conn);
                        
                        using (var reader = cmd.ExecuteReader()) {
                            Logger.Info("[SQL-DIAG] --- THE TRUTH: PHYSICAL OBJECT COUNT ---");
                            while (reader.Read()) {
                                Logger.Info(string.Format("[SQL-DIAG] {0}: {1}", reader["TypeName"], reader["Qty"]));
                            }
                        }
                    }
                    _currentStatus = "Complete";

                    _currentStatus = "Complete";
                    _isIndexing = false;
                } catch (Exception ex) {
                    Logger.Error("[SQL-DIAG] FATAL: " + ex.Message);
                    _isIndexing = false;
                    _currentStatus = "Error: " + ex.Message;
                }
            });

            return "{\"status\":\"Started\"}";
        }

        public string GetIndexStatus()
        {
            var json = new Newtonsoft.Json.Linq.JObject();
            json["isIndexing"] = _isIndexing;
            json["total"] = _totalCount;
            json["processed"] = _processedCount;
            json["status"] = _currentStatus;
            return json.ToString();
        }
    }
}
