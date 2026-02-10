using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.IO;

namespace RXM.Devices.SMS3
{
    public sealed class MusicDB
    {
        private static readonly HttpClient _http = new HttpClient();

        public string BaseUrl { get; private set; } = "http://localhost:3100";

        public List<string> Playlists { get; private set; } = new();
        public List<string> Genres { get; private set; } = new();
        public List<string> Artists { get; private set; } = new();
        public Dictionary<string, List<string>> Albums { get; private set; } = new();
        public Dictionary<string, List<string>> Songs { get; private set; } = new();

        public List<TrackRef> AllTracks { get; } = new();
        public Dictionary<string, List<TrackRef>> TracksByArtist { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TrackRef>> TracksByAlbum { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? CurrentArtist { get; set; }
        public string? CurrentAlbum { get; set; }
        public string? CurrentPlaylist { get; set; }
        public string? CurrentGenre { get; set; }
        public string? CurrentTheme { get; set; }

        public bool IsLoaded { get; private set; }

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task Load(string url = "http://localhost:3100")
        {
            if (IsLoaded) return;

            BaseUrl = url.TrimEnd('/');

            var json = await _http.GetStringAsync($"{BaseUrl}/catalog");
            var dto = JsonSerializer.Deserialize<CatalogDto>(json, _jsonOpts);

            if (dto == null)
                throw new Exception("Bad catalog JSON");

            Playlists = dto.Playlists ?? new();
            Genres = dto.Genres ?? new();
            Artists = dto.Artists ?? new();
            Albums = dto.Albums ?? new();
            Songs = dto.Songs ?? new();

            RebuildIndexes();
            IsLoaded = true;
        }

        public async Task Rescan(string URL = "http://localhost:3100")
        {
            BaseUrl = URL.TrimEnd('/');
            await _http.PostAsync($"{BaseUrl}/rescan", null);
            IsLoaded = false;
            await Load(BaseUrl);
        }

        private void RebuildIndexes()
        {
            AllTracks.Clear();
            TracksByArtist.Clear();
            TracksByAlbum.Clear();

            foreach (var (artist, albums) in Albums)
            {
                foreach (var album in albums)
                {
                    if (!Songs.TryGetValue(album, out var titles)) continue;

                    foreach (var title in titles)
                    {
                        var tr = new TrackRef
                        {
                            Artist = artist,
                            Album = album,
                            Title = title
                        };

                        AllTracks.Add(tr);

                        if (!TracksByArtist.TryGetValue(artist, out var a))
                            TracksByArtist[artist] = a = new List<TrackRef>();
                        a.Add(tr);

                        if (!TracksByAlbum.TryGetValue(album, out var b))
                            TracksByAlbum[album] = b = new List<TrackRef>();
                        b.Add(tr);
                    }
                }
            }
        }

        public async Task<StatusDto?> GetStatusAsync()
        {
            using var resp = await _http.GetAsync($"{BaseUrl}/status");
            var body = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<StatusDto>(body, _jsonOpts);
        }

        public Task PlayArtistAsync(string artist, int volume = 80)
            => GetStringAsync($"{BaseUrl}/play/artist?name={Uri.EscapeDataString(artist)}&volume={volume}");

        public Task PlayAlbumAsync(string album, int volume = 80)
            => GetStringAsync($"{BaseUrl}/play/album?name={Uri.EscapeDataString(album)}&volume={volume}");

        public Task PlayTrackAsync(string album, string title, int volume = 80)
            => GetStringAsync($"{BaseUrl}/play/track?album={Uri.EscapeDataString(album)}&title={Uri.EscapeDataString(title)}&volume={volume}");

        public Task NextAsync()
            => GetStringAsync($"{BaseUrl}/next");

        public Task PrevAsync()
            => GetStringAsync($"{BaseUrl}/prev");

        public async Task PlayTrackInQueueAsync(List<TrackRef> queue, int index, int volume = 80)
        {
            if (queue == null || queue.Count == 0) return;
            index = Math.Clamp(index, 0, queue.Count - 1);
            var t = queue[index];
            await PlayTrackAsync(t.Album, t.Title, volume);
        }

        private static async Task GetStringAsync(string url)
        {
            using var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
        }

        private sealed class CatalogDto
        {
            public List<string>? Playlists { get; set; }
            public List<string>? Genres { get; set; }
            public List<string>? Artists { get; set; }
            public Dictionary<string, List<string>>? Albums { get; set; }
            public Dictionary<string, List<string>>? Songs { get; set; }
        }

        public sealed class StatusDto
        {
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("state")] public string? State { get; set; }
            [JsonPropertyName("volume")] public int Volume { get; set; }
            [JsonPropertyName("position_ms")] public int PositionMs { get; set; }
            [JsonPropertyName("duration_ms")] public int DurationMs { get; set; }
            [JsonPropertyName("index")] public int Index { get; set; }
            [JsonPropertyName("queue_len")] public int QueueLen { get; set; }
            [JsonPropertyName("now")] public TrackDto? Now { get; set; }
        }

        public sealed class TrackDto
        {
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("artist")] public string? Artist { get; set; }
            [JsonPropertyName("album")] public string? Album { get; set; }
            [JsonPropertyName("genre")] public string? Genre { get; set; }
            [JsonPropertyName("track")] public int Track { get; set; }
            [JsonPropertyName("path")] public string? Path { get; set; }
            [JsonPropertyName("playlist")] public string? Playlist { get; set; }
        }

        public sealed class TrackRef
        {
            [JsonPropertyName("artist")] public string Artist { get; set; } = "";
            [JsonPropertyName("album")] public string Album { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
        }

        public sealed class PlaylistDef
        {
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("tracks")] public List<TrackRef> Tracks { get; set; } = new();
        }

        public sealed class PlaylistFile
        {
            [JsonPropertyName("playlists")] public List<PlaylistDef> Playlists { get; set; } = new();

            public static async Task<PlaylistFile> LoadAsync(string path)
            {
                if (!File.Exists(path)) return new PlaylistFile();
                await using var fs = File.OpenRead(path);
                return (await JsonSerializer.DeserializeAsync<PlaylistFile>(fs)) ?? new PlaylistFile();
            }

            public async Task SaveAsync(string path)
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                await using var fs = File.Create(path);
                await JsonSerializer.SerializeAsync(fs, this, opts);
            }
        }
    }
}