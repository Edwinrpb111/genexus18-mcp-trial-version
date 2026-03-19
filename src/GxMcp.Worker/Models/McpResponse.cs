using System;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Models
{
    public class McpResponse
    {
        public static string Success(string action, string target, JObject data = null)
        {
            var result = new JObject
            {
                ["status"] = "Success",
                ["action"] = action,
                ["target"] = target
            };

            if (data != null)
            {
                foreach (var prop in data.Properties())
                {
                    result[prop.Name] = prop.Value;
                }
            }

            return result.ToString();
        }

        public static string Error(string message, string target = null)
        {
            var err = new JObject
            {
                ["status"] = "Error",
                ["error"] = message
            };
            if (!string.IsNullOrEmpty(target)) err["target"] = target;
            return err.ToString();
        }

        public static string Error(
            string message,
            string target,
            string part,
            string details,
            string objectName = null,
            string objectType = null,
            JArray availableParts = null)
        {
            var err = new JObject
            {
                ["status"] = "Error",
                ["error"] = message
            };

            if (!string.IsNullOrEmpty(target)) err["target"] = target;
            if (!string.IsNullOrEmpty(part)) err["part"] = part;
            if (!string.IsNullOrEmpty(details)) err["details"] = details;
            if (!string.IsNullOrEmpty(objectName)) err["objectName"] = objectName;
            if (!string.IsNullOrEmpty(objectType)) err["objectType"] = objectType;
            if (availableParts != null && availableParts.Count > 0) err["availableParts"] = availableParts;

            return err.ToString();
        }
    }
}
