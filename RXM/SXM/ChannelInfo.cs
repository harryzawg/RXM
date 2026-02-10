using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;

namespace RXM.SXM
{
    public class Channel
    {
        public string Name { get; set; } = "";
        public string UUID { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public static class ChannelInfo
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        public static List<Channel> Channels { get; private set; } = new();
        public static readonly Dictionary<string, (string Artist, string Title)> SongCache = new();
        public static readonly Dictionary<string, DateTime> LastRefreshTime = new();

        public static void LoadJSON(string filePath)
        {
            var json = System.IO.File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new Dictionary<string, JsonElement>();

            Channels.Clear();
            foreach (var kv in data)
            {
                var CHName = kv.Key;
                var CHInfo = kv.Value;
                Channels.Add(new Channel
                {
                    Name = CHName,
                    UUID = CHInfo.GetProperty("xmchannel").GetString() ?? "",
                    Url = CHInfo.GetProperty("xmurl").GetString() ?? ""
                });
            }
        }

        public static async Task<Dictionary<string, (string Artist, string Title)>> GetSongInfo(string ChannelUUID)
        {
            try
            {
                var url = "https://lookaround-cache-prod.streaming.siriusxm.com/playbackservices/v1/live/lookAround";
                var response = await _httpClient.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<JsonElement>(response);
                var ChannelsData = data.GetProperty("channels");
                var results = new Dictionary<string, (string Artist, string Title)>();

                foreach (var ch in Channels)
                {
                    if (!ChannelsData.TryGetProperty(ch.UUID, out var chData))
                    {
                        results[ch.Name] = ("NO INFO", "NO INFO");
                        continue;
                    }

                    var cuts = chData.GetProperty("cuts").EnumerateArray();
                    foreach (var cut in cuts)
                    {
                        if (cut.TryGetProperty("isAd", out var isAd) && isAd.GetBoolean())
                            continue;

                        var artist = cut.TryGetProperty("artistName", out var a) ? a.GetString() ?? "N/A" : "N/A";
                        var title = cut.TryGetProperty("name", out var t) ? t.GetString() ?? "N/A" : "N/A";

                        //if (ch.UUID == ChannelUUID)
                            //Console.WriteLine($"[XM Info] Channel: {ch.UUID} | Artist: {artist} | Title: {title}");

                        results[ch.Name] = (artist.Length > 30 ? artist[..30] : artist,
                                            title.Length > 30 ? title[..30] : title);
                        break;
                    }

                    if (!results.ContainsKey(ch.Name))
                        results[ch.Name] = ("N/A", "N/A");
                }

                foreach (var kv in results)
                {
                    SongCache[kv.Key] = kv.Value;
                    LastRefreshTime[kv.Key] = DateTime.Now;
                }
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn't get song info: {ex.Message}");
                return new Dictionary<string, (string Artist, string Title)>();
            }
        }
    }
}
