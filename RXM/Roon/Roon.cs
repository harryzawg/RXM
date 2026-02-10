using RXM.SXM;
using RXM.Utils;
using RXM.API;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RXM.Roon
{
    public class Station
    {
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
    }

    public static class Roon
    {
        private static readonly HttpClient _http = new HttpClient();
        public static List<Station> Stations { get; private set; } = new();
        private static bool AreStationsLoaded = false;
        private static int Index = 0;
        private static int WantedIndex = 0;
        private static CancellationTokenSource? RotationToken;
        public static int CurrentIndex => Index;
        private static int RotationVer = 0;
        private static bool IsPaused = false;

        public static async Task Load()
        {
            if (AreStationsLoaded) return;
            try
            {
                var response = await _http.GetStringAsync("http://localhost:3000/radio/stations");
                var list = JsonSerializer.Deserialize<List<JsonElement>>(response) ?? new List<JsonElement>();

                Stations.Clear();
                foreach (var item in list)
                {
                    string title = item.GetProperty("title").GetString() ?? "N/A";
                    Stations.Add(new Station { Name = title, Key = title });
                }

                AreStationsLoaded = true;
                Console.WriteLine($"[Roon] Loaded {Stations.Count} stations");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roon] Couldn't load stations: {ex.Message}");
            }
        }

        public static void PlayNext(Display display = null)
        {
            if (!AreStationsLoaded || Stations.Count == 0) return;

            WantedIndex = (WantedIndex + 1) % Stations.Count;
            SendNextPrev(display, "/radio/next");
        }

        public static void PlayPrevious(Display display = null)
        {
            if (!AreStationsLoaded || Stations.Count == 0) return;

            WantedIndex = (WantedIndex - 1 + Stations.Count) % Stations.Count;
            SendNextPrev(display, "/radio/prev");
        }

        private static void SendNextPrev(Display display, string url)
        {
            display?.SendDisplay(1, Truncate.TruncateText(Stations[WantedIndex].Name, Configuration.Configuration.Config.Display.TextLength));
            Console.WriteLine($"[Roon (Next/Prev)] Sending station {WantedIndex} which should be {Stations[WantedIndex].Name}");

            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await _http.GetStringAsync($"http://localhost:3000{url}");
                    var obj = JsonSerializer.Deserialize<JsonElement>(response);
                    if (obj.TryGetProperty("index", out var indexProp))
                    {
                        Index = indexProp.GetInt32();
                        Console.WriteLine($"[Roon (Next/Prev)] Server returned {Index} which is {Stations[Index].Name}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Roon (Next/Prev)] Couldn't send station {WantedIndex}: {ex.Message}");
                }
            });
        }

        public static void Pause(Display display = null)
        {
            IsPaused = true;
            RotationToken?.Cancel();
            display?.SendDisplay(1, "ROON PAUSED");
            Console.WriteLine("[Roon] Paused");
            _ = _http.GetStringAsync("http://localhost:3000/radio/pause");
        }

        public static void Play(Display display = null)
        {
            if (!IsPaused) return;
            IsPaused = false;
            Console.WriteLine("[Roon] Playing");
            _ = _http.GetStringAsync("http://localhost:3000/radio/play");
            StartRotation(display);
        }

        public static void StartRotation(Display display)
        {
            RotationToken?.Cancel();
            RotationVer++;
            int CurrentVersion = RotationVer;

            RotationToken = new CancellationTokenSource();
            var token = RotationToken.Token;

            _ = Task.Run(async () =>
            {
                int step = 0;

                while (!token.IsCancellationRequested)
                {
                    if (IsPaused)
                    {
                        display?.SendDisplay(1, "ROON PAUSED");
                        Console.WriteLine("[Roon (Rotate)] Roon paused");
                        await Task.Delay(1000, token);
                        continue;
                    }
                    try
                    {
                        var np = await GetNowPlaying();
                        if (np == null)
                        {
                            Console.WriteLine("[Roon (Rotate)] No now playing data, will retry (Is the extension enabled in your roon settings?)");
                            await Task.Delay(1000, token);
                            continue;
                        }


                        if (CurrentVersion != RotationVer)
                        {
                            Console.WriteLine("[Roon (Rotate)] Bad rotation wil lstop");
                            break;
                        }

                        string text = step switch
                        {
                            //0 => np?.Station ?? "N/A",
                            0 => (Index >= 0 && Index < Stations.Count) ? Stations[Index].Name : "N/A",
                            1 => np?.Title ?? "N/A",
                          //  2 => np?.Artist ?? "N/A",
                            _ => np?.Station ?? "N/A"
                        };

                        byte field = step == 2 ? (byte)0x09 : (byte)0x08;

                        Console.WriteLine($"[Roon (Rotate)] Sending: {text} (type: {step})");
                        display.SendDisplay(1, Truncate.TruncateText(text, Configuration.Configuration.Config.Display.TextLength));
                        display.SendField(1, field, Truncate.TruncateText(text, Configuration.Configuration.Config.Display.TextLength));

                        int delay = step switch
                        {
                            0 => 4000,
                            1 => 10000,
                           // 2 => 5000,
                            _ => 3000
                        };

                        //step = (step + 1) % 3;
                        step = (step + 1) % 2;


                        await Task.Delay(delay, token);
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine("[Roon (Rotate)] Cancelled roration");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Roon (Rotate)] Noo nooooo rotation: {ex.Message}");
                        await Task.Delay(1000, token);
                    }
                }
            });
        }

        public static async Task<(string Artist, string Title, string Station)?> GetNowPlaying()
        {
            try
            {
                var response = await _http.GetStringAsync("http://localhost:3000/radio/playing");
                if (string.IsNullOrWhiteSpace(response)) return null;

                var obj = JsonSerializer.Deserialize<JsonElement>(response);

                static string ReadStr(JsonElement o, string name)
                {
                    if (o.ValueKind != JsonValueKind.Object) return "";
                    if (!o.TryGetProperty(name, out var p)) return "";
                    if (p.ValueKind == JsonValueKind.String) return (p.GetString() ?? "").Trim();
                    return "";
                }

                var artist = ReadStr(obj, "artist");
                var title = ReadStr(obj, "title");
                var station = ReadStr(obj, "station");

                if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(station))
                    return null;

                return (artist, title, station);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roon (NP)] NP error: {ex.Message}");
                return null;
            }
        }
        public static string NextStationName(string direction)
        {
            if (!AreStationsLoaded || Stations.Count == 0)
                return "N/A";

            if (direction.ToLower() == "up")
                Index = (Index + 1) % Stations.Count;
            else if (direction.ToLower() == "down")
                Index = (Index - 1 + Stations.Count) % Stations.Count;

            return Stations[Index].Name;
        }
    }
}
