using RXM.Serial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using static RXM.Devices.SMS3.MusicDB;

// RECYCLED IBRIDGE. THIS IS BARELY FUNCTIONAL.

// Just an example as well to see how to emulate the SMS

namespace RXM.Devices.SMS3
{
    public enum ScreenType : byte
    {
        SMS3_NOW_PLAYING = 0x07,
        SMS3_REQUEST = 0x10,
        SMS3_REQUEST_THEME = 0x0C,
        SMS3_REQUEST_GENRE = 0x09,
        SMS3_REQUEST_ARTIST = 0x0A,
        SMS3_REQUEST_ALBUM = 0x0B,
        SMS3_REQUEST_SONG = 0x37,
        SMS3_REQUEST_INTERNET_RADIO = 0x35,
        SMS3_PLAY_ARTIST = 0x11,
        SMS3_PLAY_ALBUM_BY_ARTIST = 0x0F
    }

    public sealed class MenuItem
    {
        public string Text { get; set; } = "";
        public object? Data { get; set; }
    }

    public static class RNETSMS3
    {
        public static byte Checksum(List<byte> data)
        {
            int sum = 0;
            for (int i = 0; i < data.Count - 2; i++) sum += data[i];
            sum += (data.Count - 2);
            return (byte)(sum & 0x7F);
        }

        public static byte[] CreateTransition(int controller, int zone, int source, byte screenType)
        {
            var data = new List<byte>
            {
                0xF0,
                (byte)(controller > 0 ? controller - 1 : 0),
                (byte)(zone > 0 ? zone - 1 : 0),
                0x70, 0x00, 0x7D,
                (byte)(source > 0 ? source - 1 : 0),
                0x05, 0x02, 0x01, 0x00, 0x02, 0x01, 0x00,
                0xE6, 0x00, 0x00, 0x00,
                screenType,
                0x00,
                0x00, // checksum gets replaced
                0xF7
            };
            data[^2] = Checksum(data);
            return data.ToArray();
        }

        public static byte[] CreateMenuItemV551(int controller, int zone, int source, int ItemNumber, byte ScreenMarker, string text)
        {
            byte Item = (byte)(0x40 + (ItemNumber - 1));
            text = SafeText(text, 28);
            byte[] TextBytes = Encoding.ASCII.GetBytes(text);

            var payload = new List<byte> { ScreenMarker };
            payload.AddRange(TextBytes);

            byte LL = (byte)((payload.Count + 3) & 0x7F);

            var data = new List<byte>
            {
                0xF0,
                (byte)(controller - 1),
                (byte)(zone - 1),
                0x70, 0x00, 0x7D,
                (byte)(source - 1),
                0x00,
                0x02, 0x01, 0x01, 0x02, 0x01, 0x01,
                0x00, 0x00,
                0x01, 0x00,
                LL, 0x00, 0x22,
                Item
            };

            data.AddRange(payload);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0xF7);

            data[^2] = Checksum(data);
            return data.ToArray();
        }

        private static string SafeText(string? s, int maxLen)
        {
            s ??= "";

            var safe = new string(s.Where(c => c >= 0x20 && c <= 0x7E).ToArray());

            if (safe.Length > maxLen)
                safe = safe.Substring(0, maxLen);

            if (safe.Length == 0)
                safe = "-";

            return safe;
        }

        public static byte[] CreateNP(int source, int Id, string text)
        {
            var Bytes = new List<byte>();
            if (Id > 0x7F)
            {
                Bytes.Add(0xF1);
                Bytes.Add((byte)(Id ^ 0xFF));
            }
            else Bytes.Add((byte)Id);

            byte[] TextBytes = Encoding.ASCII.GetBytes(text ?? "");

            var payload = new List<byte>();
            payload.AddRange(Bytes);
            payload.Add((byte)ScreenType.SMS3_NOW_PLAYING);
            payload.AddRange(TextBytes);

            byte LL = (byte)((payload.Count + 3) & 0x7F);

            var data = new List<byte>
            {
                0xF0,
                0x7D, 0x00,
                0x79, 0x00, 0x7D,
                (byte)((source - 1) & 0x7F),
                0x00,
                0x02, 0x01, 0x01, 0x02, 0x01, 0x01,
                0x00, 0x00,
                0x01, 0x00,
                LL, 0x00, 0x22
            };

            data.AddRange(payload);
            data.Add(0x00);
            data.Add(0x00);
            data.Add(0xF7);

            data[^2] = Checksum(data);
            return data.ToArray();
        }
    }

    public sealed class SMS3 : IDisposable
    {
        private readonly SerialConnection _sm;
        public int Source { get; }
        public int ActiveController { get; private set; } = 1;
        public int ActiveZone { get; private set; } = 1;

        private ScreenType CurrentScreen = ScreenType.SMS3_NOW_PLAYING;
        private int OffsetMENU = 0;
        private readonly List<MenuItem> Items = new();
        private readonly MusicDB MusicDB = new();
        private readonly Stack<NavState> Nav = new();
        private CancellationTokenSource? NowPlayingCTS;
        private string NPApi = $"{RXM.Configuration.Configuration.Config.PC.BaseUrliBridge}";
        private string? LastNP;
        private enum SongScope { AllSongs, ArtistSongs, AlbumSongs, PlaylistSongs, ThemeSongs }
        private SongScope CurrentSongScope = SongScope.AllSongs;
        private List<MusicDB.TrackRef> VisibleSongs = new();

        // playlist/theme stuff
        private readonly Dictionary<string, MusicDB.PlaylistDef> LocalPlaylists = new(StringComparer.OrdinalIgnoreCase);
        private MusicDB.PlaylistDef? CurrentLocalPlaylist;

        private static readonly JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private string PlaylistsRoot =>
            Path.Combine(AppContext.BaseDirectory, "SMS3", "Playlists");

        private string ThemesRoot =>
            Path.Combine(AppContext.BaseDirectory, "SMS3", "Themes");

        private sealed record NavState(ScreenType Screen, int MenuOffset, string? CurrentArtist, string? CurrentAlbum, string? CurrentPlaylist, string? CurrentGenre, string? CurrentTheme, SongScope Scope, string? LocalPlaylistName);

        public SMS3(SerialConnection serial, int source)
        {
            _sm = serial;
            Source = source;
        }

        public async Task StartAsync(bool StartNP = true)
        {
            NPApi = (NPApi).TrimEnd('/');
            try
            {
                await MusicDB.Load(NPApi);
                LoadLocalPlaylists();
                MusicDB.Playlists.Clear();
                MusicDB.Playlists.AddRange(LocalPlaylists.Keys.OrderBy(x => x));

            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[NP SMS3] Couldn't connect to '{NPApi}':", ex);
            }

            DisplayNowPlaying();

            if (StartNP)
                StartNowPlayingPolling();
        }

        public void Dispose() => StopNowPlayingTESTING();

        private void SetNP()
        {
            CurrentScreen = ScreenType.SMS3_NOW_PLAYING;
            OffsetMENU = 0;
            Nav.Clear();

            Items.Clear();
            Items.Add(new MenuItem { Text = "Music" });
        }

        public void HandlePress(RXM.Serial.Button e)
        {
            // stupid zero based shit.
            ActiveZone = Math.Clamp((int)e.Zone + 1, 1, 32);
            ActiveController = 1;

            if (e.EventId == 0x27)
            {
                Console.WriteLine("Bumping");
                Bumpy = true;
                return;
            }
            if (e.EventId == 0x45 || e.EventId == 0x42)
            {
                if (CurrentScreen == ScreenType.SMS3_NOW_PLAYING)
                {
                    PushState();
                    OffsetMENU = 0;
                    DisplayRequest();
                }
                else
                {
                    SetNP();
                }

                return;
            }

            var button = EventToButton(e.EventId);
            if (button == null) return;

            if (button == "BACK") HandleBack();
            else if (button == "PREV") HandlePrev();
            else if (button == "NEXT") HandleNext();
            else if (button == "TRACKNEXT") TrackNext();
            else if (button == "TRACKPREV") TrackPrev();
            else if (button.StartsWith("SELECT_", StringComparison.Ordinal))
            {
                if (int.TryParse(button.Split('_')[1], out int n))
                    HandleSelect(n);
            }
        }

        private static string? EventToButton(byte id) => id switch
        {
            0x2F => "NEXT",
            0x30 => "PREV",
            0x0E => "TRACKNEXT",
            0x0F => "TRACKPREV",
            0x5A => "SELECT_1",
            0x5B => "SELECT_2",
            0x5C => "SELECT_3",
            0x5D => "SELECT_4",
            0x5E => "SELECT_5",
            0x5F => "SELECT_6",
            0x12 => "BACK",
            0x67 => "PREV",
            0x68 => "NEXT",
            _ => null
        };

        private void PushState()
        {
            Nav.Push(new NavState(CurrentScreen, OffsetMENU, MusicDB.CurrentArtist, MusicDB.CurrentAlbum, MusicDB.CurrentPlaylist, MusicDB.CurrentGenre, MusicDB.CurrentTheme, CurrentSongScope, CurrentLocalPlaylist?.Name));
        }

        private void HandleBack()
        {
            if (Nav.Count == 0)
            {
                DisplayNowPlaying();
                return;
            }

            var prev = Nav.Pop();

            OffsetMENU = prev.MenuOffset;
            MusicDB.CurrentArtist = prev.CurrentArtist;
            MusicDB.CurrentAlbum = prev.CurrentAlbum;
            MusicDB.CurrentPlaylist = prev.CurrentPlaylist;
            MusicDB.CurrentGenre = prev.CurrentGenre;
            MusicDB.CurrentTheme = prev.CurrentTheme;

            CurrentSongScope = prev.Scope;
            CurrentLocalPlaylist = (!string.IsNullOrEmpty(prev.LocalPlaylistName) && LocalPlaylists.TryGetValue(prev.LocalPlaylistName, out var pl)) ? pl : null;

            switch (prev.Screen)
            {
                case ScreenType.SMS3_NOW_PLAYING: DisplayNowPlaying(); break;
                case ScreenType.SMS3_REQUEST: DisplayRequest(); break;
                case ScreenType.SMS3_REQUEST_THEME: DisplayThemes(); break;
                case ScreenType.SMS3_REQUEST_GENRE: DisplayGenres(); break;
                case ScreenType.SMS3_REQUEST_ARTIST: DisplayArtists(); break;
                case ScreenType.SMS3_REQUEST_ALBUM: DisplayAlbums(MusicDB.CurrentArtist); break;
                case ScreenType.SMS3_REQUEST_SONG: DisplaySongOpts(MusicDB.CurrentAlbum); break;
                case ScreenType.SMS3_PLAY_ARTIST: DisplayArtistOpts(MusicDB.CurrentArtist ?? ""); break;
                case ScreenType.SMS3_PLAY_ALBUM_BY_ARTIST: DisplayAlbumOpts(MusicDB.CurrentAlbum ?? ""); break;
                default: DisplayNowPlaying(); break;
            }
        }

        private static void TaskForget(Func<Task> fn)
        {
            _ = Task.Run(async () =>
            {
                try { await fn(); }
                catch (Exception ex) { Console.WriteLine($"[SMS3] API error: {ex.Message}"); }
            });
        }

        private int PageSize(ScreenType screen) => screen switch
        {
            ScreenType.SMS3_REQUEST_SONG => 5,
            _ => 4
        };

        private void HandlePrev()
        {
            int page = PageSize(CurrentScreen);
            if (OffsetMENU <= 0) return;
            OffsetMENU = Math.Max(0, OffsetMENU - page);
            RefreshScreeen();
        }

        private void HandleNext()
        {
            int page = PageSize(CurrentScreen);
            int Items = CurrentScreen switch
            {
                ScreenType.SMS3_REQUEST_THEME => MusicDB.Playlists.Count, // maybe implement these later
                ScreenType.SMS3_REQUEST_GENRE => MusicDB.Genres.Count,
                ScreenType.SMS3_REQUEST_ARTIST => MusicDB.Artists.Count,
                ScreenType.SMS3_REQUEST_ALBUM => GetAlbumsForArtist().Count,
                ScreenType.SMS3_REQUEST_SONG => VisibleSongs.Count,
                _ => 0
            };

            if (OffsetMENU + page >= Items) return;
            OffsetMENU += page;
            RefreshScreeen();
        }

        private void TrackNext()
        {
            TaskForget(() => MusicDB.NextAsync());
        }

        private void TrackPrev()
        {
            TaskForget(() => MusicDB.PrevAsync());
        }

        private void LoadLocalPlaylists()
        {
            LocalPlaylists.Clear();
            Directory.CreateDirectory(PlaylistsRoot);
            Directory.CreateDirectory(ThemesRoot);

            foreach (var Root in new[] { PlaylistsRoot, ThemesRoot })
            {
                foreach (var Dir in Directory.GetDirectories(Root))
                {
                    var Folder = Path.GetFileName(Dir);
                    if (string.IsNullOrWhiteSpace(Folder)) continue;

                    string JSONPath = Path.Combine(Dir, "playlist.json");
                    if (!File.Exists(JSONPath)) JSONPath = Path.Combine(Dir, "tracks.json");
                    if (!File.Exists(JSONPath))
                    {
                        var any = Directory.GetFiles(Dir, "*.json").FirstOrDefault();
                        if (any == null) continue;
                        JSONPath = any;
                    }

                    try
                    {
                        var JSON = File.ReadAllText(JSONPath);
                        MusicDB.PlaylistDef? pl;

                        if (JSON.TrimStart().StartsWith("["))
                        {
                            var tracks = JsonSerializer.Deserialize<List<MusicDB.TrackRef>>(JSON, _jsonOpts) ?? new();
                            pl = new MusicDB.PlaylistDef { Name = Folder, Tracks = tracks };
                        }
                        else
                        {
                            pl = JsonSerializer.Deserialize<MusicDB.PlaylistDef>(JSON, _jsonOpts);
                            if (pl == null) continue;
                            if (string.IsNullOrWhiteSpace(pl.Name)) pl.Name = Folder;
                            pl.Tracks ??= new();
                        }

                        LocalPlaylists[pl.Name] = pl;
                    }
                    catch { }
                }
            }
        }

        private Task PlayLocalPlaylistAsync(MusicDB.PlaylistDef pl, int startIndex)
        {
            if (pl.Tracks == null || pl.Tracks.Count == 0) return Task.CompletedTask;
            startIndex = Math.Clamp(startIndex, 0, pl.Tracks.Count - 1);
            return MusicDB.PlayTrackInQueueAsync(pl.Tracks, startIndex);
        }

        private void HandleSelect(int Selected)
        {
            if (Selected < 1 || Selected > Items.Count) return;
            var selected = Items[Selected - 1];

            switch (CurrentScreen)
            {
                case ScreenType.SMS3_NOW_PLAYING:
                    if (Selected == 1)
                    {
                        PushState();
                        OffsetMENU = 0;
                        DisplayRequest();
                    }
                    break;

                case ScreenType.SMS3_REQUEST:
                    OffsetMENU = 0;
                    if (Selected == 1) { PushState(); DisplayThemes(); }
                    else if (Selected == 2) { PushState(); DisplayGenres(); }
                    else if (Selected == 3) { PushState(); DisplayArtists(); }
                    else if (Selected == 4) { PushState(); MusicDB.CurrentArtist = null; DisplayAlbums(null); }
                    else if (Selected == 5) { PushState(); OffsetMENU = 0; MusicDB.CurrentArtist = null; MusicDB.CurrentAlbum = null; CurrentSongScope = SongScope.AllSongs; DisplaySongOpts(null); }
                    break;

                case ScreenType.SMS3_REQUEST_THEME:
                    {
                        if (selected.Data is string themeName && LocalPlaylists.TryGetValue(themeName, out var pl))
                        {
                            CurrentLocalPlaylist = pl;
                            MusicDB.CurrentTheme = pl.Name;
                            PushState();
                            OffsetMENU = 0;
                            DisplayThemeOpts(pl.Name);
                        }
                        break;
                    }

                case ScreenType.SMS3_REQUEST_GENRE:
                    if (!string.IsNullOrEmpty(selected.Text))
                    {
                        PushState();
                        OffsetMENU = 0;
                        DisplayGenreOpts(selected.Text);
                    }
                    break;

                case ScreenType.SMS3_REQUEST_ARTIST:
                    if (!string.IsNullOrEmpty(selected.Text))
                    {
                        PushState();
                        OffsetMENU = 0;
                        DisplayArtistOpts(selected.Text);
                    }
                    break;

                case ScreenType.SMS3_REQUEST_ALBUM:
                    if (!string.IsNullOrEmpty(selected.Text))
                    {
                        PushState();
                        OffsetMENU = 0;
                        DisplayAlbumOpts(selected.Text);
                    }
                    break;

                case ScreenType.SMS3_REQUEST_SONG:
                    {
                        if (selected.Data is MusicDB.TrackRef tr)
                        {
                            var index = OffsetMENU + (Selected - 1);
                            TaskForget(() => MusicDB.PlayTrackInQueueAsync(VisibleSongs, index));
                        }
                        break;
                    }

                case ScreenType.SMS3_PLAY_ARTIST:
                    if (Selected == 1)
                    {
                        PushState();
                        OffsetMENU = 0;
                        DisplayAlbums(MusicDB.CurrentArtist);
                    }
                    else if (Selected == 2)
                    {
                        PushState();
                        OffsetMENU = 0;
                        MusicDB.CurrentAlbum = null;
                        CurrentSongScope = SongScope.ArtistSongs;
                        DisplaySongOpts(null);
                    }
                    else if (Selected == 5)
                    {
                        var artist = (selected.Data as string) ?? MusicDB.CurrentArtist;
                        if (!string.IsNullOrEmpty(artist))
                        {
                            TaskForget(() => MusicDB.PlayArtistAsync(artist));
                            Console.WriteLine($"[SMS3] Playing: {artist}");
                        }
                    }
                    break;

                case ScreenType.SMS3_PLAY_ALBUM_BY_ARTIST:
                    if (Selected == 1)
                    {
                        PushState();
                        OffsetMENU = 0;
                        DisplaySongOpts(MusicDB.CurrentAlbum);
                    }
                    else if (Selected == 5)
                    {
                        var album = (selected.Data as string) ?? MusicDB.CurrentAlbum;
                        if (!string.IsNullOrEmpty(album))
                        {
                            TaskForget(() => MusicDB.PlayAlbumAsync(album));
                            Console.WriteLine($"[SMS3] Playing album: {album}");
                        }
                    }
                    break;
            }
        }

        private void SendMessage(byte[] msg) => _sm.Write(msg);

        private void SendScreen(ScreenType type)
        {
            var msg = RNETSMS3.CreateTransition(ActiveController, ActiveZone, Source, (byte)type);
            SendMessage(msg);
            CurrentScreen = type;
            Thread.Sleep(90);
        }

        private void SendMenu(IEnumerable<string> items)
        {
            var padded = items.Take(7).ToList();
            int slot = padded.Count + 1;
            while (padded.Count < 7) padded.Add("------------");

            for (int i = 1; i <= 7; i++)
            {
                var text = padded[i - 1];
                var msg = RNETSMS3.CreateMenuItemV551(ActiveController, ActiveZone, Source, i, (byte)CurrentScreen, text);

                Thread.Sleep(25);
                _sm.Write(msg);
                Thread.Sleep(200);
            }
        }

        private void DisplayNowPlaying()
        {
            Nav.Clear();
            OffsetMENU = 0;

            SendScreen(ScreenType.SMS3_NOW_PLAYING);
            SendMenu(new[] { "Music" });

            Items.Clear();
            Items.Add(new MenuItem { Text = "Music" });
        }

        private void DisplayRequest()
        {
            SendScreen(ScreenType.SMS3_REQUEST);
            var items = new[] { "by Theme", "by Genre", "by Artist", "by Album", "by Song", "by Radio" };
            SendMenu(items);

            Items.Clear();
            Items.AddRange(items.Select(t => new MenuItem { Text = t }));
        }

        private void DisplayThemes()
        {
            SendScreen(ScreenType.SMS3_REQUEST_THEME);
            var slice = MusicDB.Playlists.Skip(OffsetMENU).Take(7).ToList();
            SendMenu(slice);

            Items.Clear();
            Items.AddRange(slice.Select(p => new MenuItem { Text = p, Data = p }));
        }

        private void DisplayGenres()
        {
            SendScreen(ScreenType.SMS3_REQUEST_GENRE);
            var slice = MusicDB.Genres.Skip(OffsetMENU).Take(7).ToList();
            SendMenu(slice);

            Items.Clear();
            Items.AddRange(slice.Select(g => new MenuItem { Text = g, Data = g }));
        }

        private void DisplayArtists()
        {
            SendScreen(ScreenType.SMS3_REQUEST_ARTIST);
            var slice = MusicDB.Artists.Skip(OffsetMENU).Take(4).ToList();
            var menu = new List<string>();
            menu.AddRange(slice);

            while (menu.Count < 4)
                menu.Add("------------");

            menu.Add("Artists");
            SendMenu(menu);

            Items.Clear();
            Items.AddRange(slice.Select(a => new MenuItem { Text = a, Data = a }));
        }

        private List<string> GetAlbumsForArtist()
        {
            if (!string.IsNullOrEmpty(MusicDB.CurrentArtist) && MusicDB.Albums.TryGetValue(MusicDB.CurrentArtist, out var list))
                return list.ToList();

            return MusicDB.Albums.Values.SelectMany(x => x).ToList();
        }

        private void DisplayAlbums(string? artist)
        {
            MusicDB.CurrentArtist = artist;
            SendScreen(ScreenType.SMS3_REQUEST_ALBUM);

            var albums = GetAlbumsForArtist();
            var slice = albums.Skip(OffsetMENU).Take(4).ToList();

            var menu = new List<string>();
            menu.AddRange(slice);
            while (menu.Count < 4) menu.Add("------------");
            menu.Add("Albums");

            SendMenu(menu);

            Items.Clear();
            Items.AddRange(slice.Select(a => new MenuItem { Text = a, Data = a }));
        }

        private List<MusicDB.TrackRef> GetScopedSongs()
        {
            return CurrentSongScope switch
            {
                SongScope.PlaylistSongs => (CurrentLocalPlaylist?.Tracks?.ToList() ?? new()),
                SongScope.ThemeSongs => (CurrentLocalPlaylist?.Tracks?.ToList() ?? new()),

                SongScope.AlbumSongs => !string.IsNullOrEmpty(MusicDB.CurrentAlbum) &&
                                        MusicDB.TracksByAlbum.TryGetValue(MusicDB.CurrentAlbum, out var a) ? a : new(),

                SongScope.ArtistSongs => !string.IsNullOrEmpty(MusicDB.CurrentArtist) &&
                                         MusicDB.TracksByArtist.TryGetValue(MusicDB.CurrentArtist, out var b) ? b : new(),

                _ => MusicDB.AllTracks
            };
        }

        private void DisplaySongOpts(string? album)
        {
            if (!string.IsNullOrEmpty(album))
            {
                MusicDB.CurrentAlbum = album;
                CurrentSongScope = SongScope.AlbumSongs;
            }

            SendScreen(ScreenType.SMS3_REQUEST_SONG);

            VisibleSongs = GetScopedSongs();
            var slice = VisibleSongs.Skip(OffsetMENU).Take(7).ToList();

            SendMenu(slice.Select(t => t.Title));

            Items.Clear();
            Items.AddRange(slice.Select(t => new MenuItem { Text = t.Title, Data = t }));
        }

        // These dont work and arent in the right order
        private void DisplayThemeOpts(string theme)
        {
            MusicDB.CurrentTheme = theme;
            SendScreen(ScreenType.SMS3_PLAY_ARTIST);

            var items = new[] { "View Albums", "View Songs", "", "", theme };
            SendMenu(items);

            Items.Clear();
            Items.AddRange(new[]
            {
                new MenuItem { Text = "View Albums" },
                new MenuItem { Text = "View Songs" },
                new MenuItem { Text = "" },
                new MenuItem { Text = "" },
                new MenuItem { Text = theme }
            });
        }

        private void DisplayGenreOpts(string genre)
        {
            MusicDB.CurrentGenre = genre;
            SendScreen(ScreenType.SMS3_PLAY_ARTIST);

            var items = new[] { "View Artists", "View Albums", "View Songs", "", $"{genre}" };
            SendMenu(items);

            Items.Clear();
            Items.AddRange(new[]
            {
                new MenuItem { Text = "View Artists" },
                new MenuItem { Text = "View Albums" },
                new MenuItem { Text = "View Songs" },
                new MenuItem { Text = "" },
                new MenuItem { Text = $"{genre}", Data = genre }
            });
        }

        private void DisplayArtistOpts(string artist)
        {
            MusicDB.CurrentArtist = artist;
            SendScreen(ScreenType.SMS3_PLAY_ARTIST);

            var items = new[] { "View Albums", "View Songs", "", "", $"{artist}" };
            SendMenu(items);

            Items.Clear();
            Items.AddRange(new[]
            {
                new MenuItem { Text = "View Albums" },
                new MenuItem { Text = "View Songs" },
                new MenuItem { Text = "" },
                new MenuItem { Text = "" },
                new MenuItem { Text = $"{artist}", Data = artist }
            });
        }

        private void DisplayAlbumOpts(string album)
        {
            MusicDB.CurrentAlbum = album;

            SendScreen(ScreenType.SMS3_PLAY_ALBUM_BY_ARTIST);

            var items = new[] { "View Songs", "", "", "", $"{album}" };
            SendMenu(items);

            Items.Clear();
            Items.AddRange(new[]
            {
                new MenuItem { Text = "View Songs" },
                new MenuItem { Text = "" },
                new MenuItem { Text = "" },
                new MenuItem { Text = "" },
                new MenuItem { Text = $"{album}", Data = album }
            });
        }

        private void RefreshScreeen()
        {
            switch (CurrentScreen)
            {
                case ScreenType.SMS3_REQUEST_THEME: DisplayThemes(); break;
                case ScreenType.SMS3_REQUEST_GENRE: DisplayGenres(); break;
                case ScreenType.SMS3_REQUEST_ARTIST: DisplayArtists(); break;
                case ScreenType.SMS3_REQUEST_ALBUM: DisplayAlbums(MusicDB.CurrentArtist); break;
                case ScreenType.SMS3_REQUEST_SONG: DisplaySongOpts(MusicDB.CurrentAlbum); break;
            }
        }

        private readonly object NPLock = new();

        public void SendNP(string device, string artist, string album, string title)
        {
            lock (NPLock)
            {
                _sm.Write(RNETSMS3.CreateNP(Source, 0x06, device));
                Thread.Sleep(60);

                _sm.Write(RNETSMS3.CreateNP(Source, 0x82, artist));
                Thread.Sleep(60);

                _sm.Write(RNETSMS3.CreateNP(Source, 0x84, album));
                Thread.Sleep(60);

                _sm.Write(RNETSMS3.CreateNP(Source, 0x81, title));
                Thread.Sleep(80);

                // Russound loves dropping the last packet sometimes
                _sm.Write(RNETSMS3.CreateNP(Source, 0x81, title));
                Thread.Sleep(80);
            }
        }

        private int LastBump = -1;
        private volatile bool Bumpy = false;
        private readonly Random RANdoon = new Random();

        public void StartNowPlayingPolling(int pollMs = 1500)
        {
            StopNowPlayingTESTING();
            NowPlayingCTS = new CancellationTokenSource();
            var token = NowPlayingCTS.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var st = await MusicDB.GetStatusAsync();
                        var now = st?.Now;
                        Console.WriteLine($"[SMS3] status now={(st?.Now == null ? "null" : st.Now.Artist + " - " + st.Now.Title)}");

                        if (now != null)
                        {
                            var key = $"{now.Artist}|{now.Album}|{now.Title}";

                            var Bump = Bumpy;
                            if (Bump) Bumpy = false;

                            if (Bump || !string.Equals(LastNP, key, StringComparison.Ordinal))
                            {
                                LastNP = key;

                                var device = "VLC";
                                var artist = now.Artist ?? "Unknown";
                                var album = now.Album ?? "Unknown";
                                var title = now.Title ?? "Unknown";

                                // This is really fucking stupid but russound wont accept the thing if its the same and idk how to fix so this works for now
                                if (Bump)
                                {
                                    int pick;
                                    if (LastBump < 0) LastBump = 0;

                                    do
                                    {
                                        pick = RANdoon.Next(0, 4);
                                    } while (pick == LastBump);

                                    LastBump = pick;

                                    switch (pick)
                                    {
                                        case 0: device += " "; break;
                                        case 1: artist += " "; break;
                                        case 2: album += " "; break;
                                        case 3: title += " "; break;
                                    }
                                }

                                SendNP(device, artist, album, title);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SMS3] NP poll error: {ex.Message}");
                    }

                    try { await Task.Delay(pollMs, token); }
                    catch (TaskCanceledException) { break; }
                }
            }, token);
        }

        public void StopNowPlayingTESTING()
        {
            try { NowPlayingCTS?.Cancel(); } catch { }
            NowPlayingCTS = null;
        }
    }
}