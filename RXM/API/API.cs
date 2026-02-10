using RXM.Serial;
using RXM.SXM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// used for russound.zawg.ca
namespace RXM.API
{
    public class ZoneState
    {
        public int ZoneID { get; set; }
        public bool Power { get; set; }
        public int Source { get; set; }
        public int Volume { get; set; }
    }

    public class RNETApi
    {
        private HttpListener _listener;
        private SerialConnection _serial;
        private static ZoneState[] Zones = Enumerable.Range(0, 6)
            .Select(i => new ZoneState { ZoneID = i + 1, Power = false, Source = 1, Volume = 0 })
            .ToArray();

        public class NowPlayingResponse
        {
            public object Roon { get; set; }
            public object SiriusXM { get; set; }
        }

        public RNETApi(SerialConnection serial)
        {
            _serial = serial;
            _serial.DataReceived += OnSerialData;

            _listener = new HttpListener();
            _listener.Prefixes.Add("http://*:8080/");
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                Console.WriteLine("Server hosting on port 8080");
                Task.Run(() => ListenLoop());
            }
            catch (Exception ex) { Console.WriteLine($"[API (Error)] {ex.Message}"); }
        }

        private async Task Poll()
        {
            for (int i = 1; i <= 6; i++)
            {
                var pkt = PacketBuilder.GetStatus(i);
                _serial.Write(pkt);
                await Task.Delay(150);
            }
        }

        private void OnSerialData(object sender, byte[] packet)
        {
            if (packet.Length < 25) return;
            if (packet[13] != 0x07) return;

            int zoneIdx = packet[12];

            if (zoneIdx >= 0 && zoneIdx < 6)
            {
                if (packet.Length > 22)
                {
                    bool pwr = packet[20] == 0x01;
                    int src = packet[21] + 1;
                    int vol = packet[22] * 2;
                    var z = Zones[zoneIdx];
                    if (z.Power != pwr || z.Source != src || z.Volume != vol)
                        Console.WriteLine($"[Api (Update)] Zone {zoneIdx + 1}: Pwr={pwr} Src={src} Vol={vol}");

                    z.Power = pwr;
                    z.Source = src;
                    z.Volume = vol;
                }
            }
        }

        private async Task ListenLoop()
        {
            while (_listener.IsListening)
            {
                try { var ctx = await _listener.GetContextAsync(); _ = HandleRequest(ctx); } catch { }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            string resp = "{}";
            int code = 200;
            var req = ctx.Request;

            try
            {
                if (req.Url.AbsolutePath == "/zones")
                {
                    await Poll();
                    resp = JsonSerializer.Serialize(Zones);
                }
                else if (req.Url.AbsolutePath == "/control" && req.HttpMethod == "POST")
                {
                    using var r = new StreamReader(req.InputStream);
                    var cmd = JsonSerializer.Deserialize<ControlCmd>(await r.ReadToEndAsync());

                    if (cmd != null)
                    {
                        byte[] p = null;
                        switch (cmd.type.ToLower())
                        {
                            case "power": p = PacketBuilder.SetPower(cmd.zone, cmd.value == 1); break;
                            case "source": p = PacketBuilder.SetSource(cmd.zone, cmd.value); break;
                            case "volume": p = PacketBuilder.SetVolume(cmd.zone, cmd.value); break;
                        }

                        if (p != null)
                        {
                            _serial.Write(p);
                            resp = "{\"message\":\"ok\"}";
                            await Task.Delay(100);
                            _serial.Write(PacketBuilder.GetStatus(cmd.zone));
                        }
                    }
                }
                else if (req.Url.AbsolutePath == "/nowplaying")
                {
                    object roon = null;
                    try
                    {
                        var np = await RXM.Roon.Roon.GetNowPlaying();
                        if (np != null)
                        {
                            roon = new
                            {
                                Artist = np.Value.Artist,
                                Title = np.Value.Title,
                                Station = np.Value.Station
                            };
                        }
                    }
                    catch { }
                    object sxm = null;
                    try
                    {
                        var index = RXM.Program.Index;
                        var channel = ChannelInfo.Channels[index];

                        var songs = await ChannelInfo.GetSongInfo(channel.UUID);
                        if (songs.TryGetValue(channel.Name, out var song))
                        {
                            sxm = new
                            {
                                Channel = channel.Name,
                                Artist = song.Artist,
                                Title = song.Title
                            };
                        }
                    }
                    catch { }
                    resp = JsonSerializer.Serialize(new NowPlayingResponse
                    {
                        Roon = roon,
                        SiriusXM = sxm
                    });
                }
                else if (req.Url.AbsolutePath.StartsWith("/channel") && req.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(req.InputStream);
                    var cmd = JsonSerializer.Deserialize<ChannelCmd>(await reader.ReadToEndAsync());
                    if (cmd == null)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"message\":\"Invalid\"}"));
                        ctx.Response.Close();
                        return;
                    }
                    object NowPlaying = null;
                    switch (cmd.source.ToLower())
                    {
                        case "siriusxm":
                            await Program.ChannelLock.WaitAsync();
                            try
                            {
                                if (cmd.direction == "up")
                                    Program.Index = (Program.Index + 1) % ChannelInfo.Channels.Count;
                                else
                                    Program.Index = (Program.Index - 1 + ChannelInfo.Channels.Count) % ChannelInfo.Channels.Count;

                                _ = Task.Run(async () => await Program.StartChannel(Program.Index, new Display(_serial)));
                            }
                            finally { Program.ChannelLock.Release(); }

                            var channel = ChannelInfo.Channels[Program.Index];
                            var songs = await ChannelInfo.GetSongInfo(channel.UUID);
                            songs.TryGetValue(channel.Name, out var song);
                            var HasAsong = songs.TryGetValue(channel.Name, out var xmsong);
                            NowPlaying = new
                            {
                                Source = "SiriusXM",
                                Channel = channel.Name,
                                Artist = HasAsong ? song.Artist : "N/A",
                                Title = HasAsong ? song.Title : "N/A"
                            };
                            break;

                        case "roon":
                            NowPlaying = new
                            {
                                Source = "Roon",
                                Station = RXM.Roon.Roon.NextStationName(cmd.direction),
                                Artist = RXM.Roon.Roon.NextStationName(cmd.direction),
                                Title = "N/A"
                            };

                            await Task.Delay(200);
                            if (cmd.direction == "up")
                                RXM.Roon.Roon.PlayNext();
                            else
                                RXM.Roon.Roon.PlayPrevious();
                            break;

                        default:
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"message\":\"Bad source\"}"));
                            ctx.Response.Close();
                            return;
                    }

                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message = "ok", NowPlaying }));
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
                else
                {
                    code = 404; resp = "{\"message\":\"Not Found\"}";
                }
            }
            catch (Exception ex) { code = 500; resp = $"{{\"message\":\"{ex.Message}\"}}"; }

            byte[] b = Encoding.UTF8.GetBytes(resp);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = b.Length;
            await ctx.Response.OutputStream.WriteAsync(b, 0, b.Length);
            ctx.Response.Close();
        }

        class ControlCmd { public int zone { get; set; } public string type { get; set; } public int value { get; set; } }
        class ChannelCmd { public string source { get; set; } = ""; public string direction { get; set; } = ""; }

        private static class PacketBuilder
        {
            public static byte[] GetStatus(int zone) => Wrap(new List<byte> { 0x00, 0x00, 0x7F, 0x00, 0x00, 0x70, 0x01, 0x04, 0x02, 0x00, (byte)(zone - 1), 0x07, 0x00, 0x00 });

            public static byte[] SetPower(int zone, bool on) => Wrap(new List<byte> { 0x00, 0x00, 0x7F, 0x00, 0x00, 0x70, 0x05, 0x02, 0x02, 0x00, 0x00, 0xF1, 0x23, 0x00, (byte)(on ? 1 : 0), 0x00, (byte)(zone - 1), 0x00, 0x01 });

            public static byte[] SetSource(int zone, int src) => Wrap(new List<byte> { 0x00, 0x00, 0x7F, 0x00, (byte)(zone - 1), 0x70, 0x05, 0x02, 0x00, 0x00, 0x00, 0xF1, 0x3E, 0x00, 0x00, 0x00, (byte)(src - 1), 0x00, 0x01 });

            public static byte[] SetVolume(int zone, int vol) => Wrap(new List<byte> { 0x00, 0x00, 0x7F, 0x00, 0x00, 0x70, 0x05, 0x02, 0x02, 0x00, 0x00, 0xF1, 0x21, 0x00, (byte)(vol / 2), 0x00, (byte)(zone - 1), 0x00, 0x01 });

            private static byte[] Wrap(List<byte> body)
            {
                int sum = 0xF0;
                foreach (var b in body) sum += b;
                int count = body.Count + 1;
                sum += count;
                body.Add((byte)(sum & 0x7F));
                body.Add(0xF7);
                body.Insert(0, 0xF0);
                return body.ToArray();
            }
        }
    }
}