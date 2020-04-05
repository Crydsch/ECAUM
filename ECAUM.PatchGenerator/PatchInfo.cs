using System.Collections.Generic;
using System.Text.Json;

namespace ECAUM.PatchGenerator
{
    class PatchInfo
    {
        public Dictionary<string, string> FileHashes { get; set; } // relative filename (to workingDir), xxHash as hexString

        public PatchInfo()
        {
            FileHashes = new Dictionary<string, string>();
        }

        public string Serialize()
        {
            return JsonSerializer.Serialize(this);
        }

        public static PatchInfo Deserialize(string jsonString)
        {
            return JsonSerializer.Deserialize<PatchInfo>(jsonString);
        }
    }
}
