using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ECAUM
{
    public class UpdateInfo
    {
        [JsonConverter(typeof(SimpleVersionConverter))]
        public Version UpdaterVersion { get; set; }
        public long UpdaterSize { get; set; }
        public string FullAppArchiveName { get; set; }
        public long FullAppArchiveSize { get; set; }
        public List<Patch> PatchTrail { get; set; }

        public UpdateInfo()
        {
            PatchTrail = new List<Patch>();
            FullAppArchiveName = "app";
            UpdaterVersion = new Version("0.0.0.0");
        }

        public string Serialize()
        {
            return JsonSerializer.Serialize(this);
        }

        public static UpdateInfo Deserialize(string jsonString)
        {
            return JsonSerializer.Deserialize<UpdateInfo>(jsonString);
        }
    }

    public class Patch
    {
        [JsonConverter(typeof(SimpleVersionConverter))]
        public Version Version { get; set; }
        public long SizeInBytes { get; set; }
        public string Description { get; set; }

        public Patch() { }

        public Patch(Version version, string description, long sizeInBytes)
        {
            Version = version;
            Description = description;
            SizeInBytes = sizeInBytes;
        }
    }

    public class SimpleVersionConverter : JsonConverter<Version>
    {
        public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return new Version(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, Version version, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
        }
    }
}
