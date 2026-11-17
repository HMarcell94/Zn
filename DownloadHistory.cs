using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZnDownloader
{
    // One finished (or failed) YouTube / Pinterest download, as shown on the "Előzmények" tab.
    public sealed class DownloadHistoryEntry
    {
        public DateTime Time { get; set; }
        public string Source { get; set; } = "";   // "YouTube" / "Pinterest"
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Format { get; set; } = "";
        public bool Success { get; set; }
        public string FilePath { get; set; } = "";
        public string Folder { get; set; } = "";
        public string Error { get; set; } = "";

        [JsonIgnore] public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : Url;
        [JsonIgnore] public string Meta => $"{Time:yyyy.MM.dd. HH:mm}  ·  {Source}  ·  {Format}" + (Success ? "" : "  ·  Sikertelen");
        [JsonIgnore] public string Detail => Success ? (FilePath != "" ? FilePath : Folder) : Error;
    }

    // Persists the history as JSON under %LOCALAPPDATA%\ZnTools (per user, survives rebuilds).
    public static class DownloadHistoryStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZnTools", "download-history.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static List<DownloadHistoryEntry> Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return new List<DownloadHistoryEntry>();
                return JsonSerializer.Deserialize<List<DownloadHistoryEntry>>(File.ReadAllText(StorePath)) ?? new List<DownloadHistoryEntry>();
            }
            catch
            {
                // Unreadable file: keep a copy instead of silently overwriting it on the next save.
                try { File.Copy(StorePath, StorePath + ".bak", overwrite: true); } catch { }
                return new List<DownloadHistoryEntry>();
            }
        }

        public static void Save(IEnumerable<DownloadHistoryEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                string tmp = StorePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(entries, JsonOptions));
                File.Move(tmp, StorePath, overwrite: true);
            }
            catch { /* history is a convenience; never break a download over it */ }
        }
    }
}
