using RXM.SXM;
using RXM.Utils;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RXM.Devices.VCD
{
    // emulates a cd changer using a python server that has the music in folders
    public static class VCDChanger
    {
        private static readonly HttpClient Http = new()
        {
            BaseAddress = new Uri(Configuration.Configuration.Config.PC.BaseUrlVCD)
        };

        private static readonly StringBuilder InputBuffer = new();
        private static CancellationTokenSource? InputTimeout;
        private static CancellationTokenSource? RotationToken;
        private static int RotationVer;
        public static bool IsTyping = false;
        private static int CurrentDisc = 1;
        private static readonly Random Random = new();

        public static void HandleKey(byte Event, Display display)
        {
            if (Event >= 0x01 && Event <= 0x09)
            {
                AddDigit(Event, display);
                return;
            }

            if (Event == 0x0A)
            {
                AddDigit(0, display);
                return;
            }

            if (Event == 0x11)
            {
                Commit(display);
                return;
            }

            switch (Event)
            {
                case 0x2F:
                    _ = PlayAboveOrDownDisc(1, display);
                    return;
                case 0x30:
                    _ = PlayAboveOrDownDisc(-1, display);
                    return;
                case 0x2E:
                    _ = PlayRandomDisc(display);
                    return;
            }

            switch (Event)
            {
                case 0x1C:
                    _ = SafePost("/media/seekF?seconds=3");
                    return;
                case 0x1D:
                    _ = SafePost("/media/seekB?seconds=3");
                    return;
            }

            switch (Event)
            {
                case 0x1A: _ = SafePost("/media/play"); break;
                case 0x1E:
                case 0x1B: _ = SafePost("/media/pause"); break;
                case 0x0E: _ = SafePost("/media/next"); break;
                case 0x0F: _ = SafePost("/media/prev"); break;
            }
        }

        private static void AddDigit(int digit, Display display)
        {
            IsTyping = true;
            RestartTimeout(display);
            InputBuffer.Append(digit);
            if (InputBuffer.Length >= 4)
            {
                string last4 = InputBuffer.ToString().Substring(InputBuffer.Length - 4);
                if (last4 == "0000")
                {
                    InputBuffer.Clear();
                    display.SendDisplay(2, "DISC-0000 (Reset)");
                    return;
                }
            }
            if (InputBuffer.Length > 4)
                InputBuffer.Remove(0, InputBuffer.Length - 4);

            _ = Update(display);
        }

        private static async Task Update(Display display)
        {
            string padded = InputBuffer.ToString().PadLeft(4, '0');
            string album = "N/A";
            if (InputBuffer.Length > 0)
            {
                try
                {
                    int Disc = int.Parse(InputBuffer.ToString().PadLeft(4, '0'));
                    var JSON = await Http.GetStringAsync($"/disc/{Disc}");
                    var Jsonobj = JsonSerializer.Deserialize<JsonElement>(JSON);
                    album = Jsonobj.GetProperty("album").GetString() ?? "N/A";
                }
                catch
                {
                    album = "N/A";
                }
            }

            display.SendDisplay(2, Truncate.TruncateText($"DISC-{padded} ({album})",
                Configuration.Configuration.Config.Display.TextLength));
        }

        private static async void Commit(Display display)
        {
            InputTimeout?.Cancel();
            int DiscID;
            if (InputBuffer.Length == 0)
            {
                DiscID = 0000;
            }
            else
            {
                DiscID = int.Parse(InputBuffer.ToString().PadLeft(4, '0'));
            }

            InputBuffer.Clear();
            IsTyping = false;

            await PlayDisc(DiscID, display);
        }

        private static async Task PlayDisc(int discId, Display display)
        {
            CurrentDisc = discId;
            try
            {
                var json = await Http.GetStringAsync($"/disc/{discId}");
                var obj = JsonSerializer.Deserialize<JsonElement>(json);
                string album = obj.GetProperty("album").GetString() ?? "N/A";

                display.SendDisplay(2, Truncate.TruncateText($"DISC-{discId:0000} ({album})",
                    Configuration.Configuration.Config.Display.TextLength));

                await SafePost($"/disc/{discId}/play");
                StartRotation(display);
            }
            catch
            {
                display.SendDisplay(2, "N/A");
            }
        }

        private static async Task PlayAboveOrDownDisc(int delta, Display display)
        {
            try
            {
                var json = await Http.GetStringAsync("/discs");
                int TotalDiscs = JsonSerializer.Deserialize<int>(json);

                int NextDisc = CurrentDisc + delta;
                if (NextDisc < 1) NextDisc = TotalDiscs;
                if (NextDisc > TotalDiscs) NextDisc = 1;

                await PlayDisc(NextDisc, display);
            }
            catch
            {
                display.SendDisplay(2, "N/A");
            }
        }

        private static async Task PlayRandomDisc(Display display)
        {
            try
            {
                var json = await Http.GetStringAsync("/discs");
                int TotalDiscs = JsonSerializer.Deserialize<int>(json);

                int Randomdisc = Random.Next(1, TotalDiscs + 1);
                await PlayDisc(Randomdisc, display);
            }
            catch
            {
                display.SendDisplay(2, "N/A");
            }
        }

        private static void RestartTimeout(Display display)
        {
            InputTimeout?.Cancel();
            InputTimeout = new CancellationTokenSource();
            var token = InputTimeout.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(8000, token);
                    Commit(display);
                }
                catch (TaskCanceledException) { }
            });
        }

        private static void StartRotation(Display display)
        {
            RotationToken?.Cancel();
            RotationVer++;
            int version = RotationVer;

            RotationToken = new CancellationTokenSource();
            var token = RotationToken.Token;

            _ = Task.Run(async () =>
            {
                int step = 0;

                while (!token.IsCancellationRequested && version == RotationVer)
                {
                    try
                    {
                        if (IsTyping)
                        {
                            await Task.Delay(500, token);
                            continue;
                        }

                        var json = await Http.GetStringAsync("/media/nowplaying");
                        var obj = JsonSerializer.Deserialize<JsonElement>(json);

                        string text = step switch
                        {
                            0 => obj.GetProperty("album").GetString() ?? "N/A",
                            1 => obj.GetProperty("artist").GetString() ?? "N/A",
                            _ => obj.GetProperty("title").GetString() ?? "N/A"
                        };

                        display.SendDisplay(2, Truncate.TruncateText(text,
                            Configuration.Configuration.Config.Display.TextLength));

                        step = (step + 1) % 3;
                        await Task.Delay(6000, token);
                    }
                    catch
                    {
                        await Task.Delay(1000, token);
                    }
                }
            });
        }

        private static async Task SafePost(string url)
        {
            try
            {
                var resp = await Http.PostAsync(url, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VCD] POST to {url} not goood bad bad: {ex}");
            }
        }
    }
}
