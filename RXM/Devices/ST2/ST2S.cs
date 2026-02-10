using RXM.Serial;
using RXM.SXM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// This is stupid and realllly should be reworked
// Most of this only works on touch units
namespace RXM.Devices.ST2
{
    public class Preset
    {
        // if no preset use this
        public string ChannelName { get; set; } = "------";
        public string ChannelUUID { get; set; } = "";
        // channel api doesn't return categories
        public string Category { get; set; } = "N/A";
    }

    public class PresetManager
    {
        private readonly Dictionary<int, Preset[]> Banks = new();
        private readonly string Presets;
        private static readonly JsonSerializerOptions JSONOpts = new() { WriteIndented = true };
        public int CurrentBank { get; set; } = 1;

        public PresetManager(string PresetsJson)
        {
            Presets = PresetsJson;
            for (int i = 1; i <= 6; i++)
            {
                Banks[i] = new Preset[6];
                for (int j = 0; j < 6; j++)
                    Banks[i][j] = new Preset { ChannelName = "------" };
            }

            LoadPresets();
        }

        public void SavePreset(int BANK, int Slot, string Name, string UUID, string Cat = "N/A")
        {
            if (BANK < 1 || BANK > 6 || Slot < 1 || Slot > 6)
                return;

            Banks[BANK][Slot - 1] = new Preset
            {
                ChannelName = string.IsNullOrWhiteSpace(Name) ? "------" : Name,
                ChannelUUID = UUID ?? "",
                Category = string.IsNullOrWhiteSpace(Cat) ? "N/A" : Cat
            };

            SavePresets();
        }

        public Preset? GetPreset(int BANK, int Slot)
        {
            if (BANK < 1 || BANK > 6 || Slot < 1 || Slot > 6)
                return null;

            return Banks[BANK][Slot - 1];
        }

        public Preset[] GetBankPresets(int bank)
        {
            if (bank < 1 || bank > 6)
                return Array.Empty<Preset>();

            return Banks[bank];
        }

        public void NextBank() => CurrentBank = (CurrentBank % 6) + 1;
        public void PrevBank() => CurrentBank = (CurrentBank == 1) ? 6 : (CurrentBank - 1);

        public static void EnsurePresetsJson(string Path)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(Path))
                    return;

                var banks = new Dictionary<int, Preset[]>();
                for (int bank = 1; bank <= 6; bank++)
                {
                    var slots = new Preset[6];
                    for (int i = 0; i < 6; i++)
                    {
                        slots[i] = new Preset
                        {
                            ChannelName = "------",
                            ChannelUUID = "",
                            Category = "N/A"
                        };
                    }
                    banks[bank] = slots;
                }

                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(banks, opts);
                File.WriteAllText(Path, json);

                Console.WriteLine($"[Sirius (Presets) Created first presets: {Path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sirius (Presets)] Failed to create presets: {ex.Message}");
            }
        }

        private void LoadPresets()
        {
            try
            {
                if (!File.Exists(Presets))
                    return;

                var JSON = File.ReadAllText(Presets);
                var Loaded = JsonSerializer.Deserialize<Dictionary<int, Preset[]>>(JSON, JSONOpts);
                if (Loaded == null)
                    return;

                foreach (var kvp in Loaded)
                {
                    if (kvp.Key < 1 || kvp.Key > 6)
                        continue;

                    var Array = kvp.Value ?? System.Array.Empty<Preset>();
                    var FixedArray = new Preset[6];

                    for (int i = 0; i < 6; i++)
                    {
                        FixedArray[i] = (i < Array.Length && Array[i] != null)
                            ? Array[i]!
                            : new Preset { ChannelName = "------" };

                        if (string.IsNullOrWhiteSpace(FixedArray[i].ChannelName))
                            FixedArray[i].ChannelName = "------";

                        FixedArray[i].Category ??= "N/A";
                        FixedArray[i].ChannelUUID ??= "";
                    }

                    Banks[kvp.Key] = FixedArray;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sirius (Presets)] Error loading: {ex.Message}");
            }
        }

        private void SavePresets()
        {
            try
            {
                var Directory = Path.GetDirectoryName(Presets);
                if (!string.IsNullOrEmpty(Directory))
                    System.IO.Directory.CreateDirectory(Directory);

                var JSON = JsonSerializer.Serialize(Banks, JSONOpts);
                File.WriteAllText(Presets, JSON);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sirius (Presets)] Error saving: {ex.Message}");
            }
        }
    }

    public static class DisplayStuff
    {
        private static readonly object NPLock = new();
        private static readonly byte[] Channel = new byte[] { 0x07 };
        private static readonly byte[] Category = new byte[] { 0x43 };
        private static readonly byte[] Artist = new byte[] { 0xF1, 0x7D };
        private static readonly byte[] Title = new byte[] { 0xF1, 0x7E };
        private static readonly byte[] BankHeader = new byte[] { 0x49 };
        private static readonly byte[] M1 = new byte[] { 0x4A };
        private static readonly byte[] M2 = new byte[] { 0x4B };
        private static readonly byte[] M3 = new byte[] { 0x4C };
        private static readonly byte[] M4 = new byte[] { 0x4D };
        private static readonly byte[] M5 = new byte[] { 0x4E };
        private static readonly byte[] M6 = new byte[] { 0x4F };
        private static readonly byte[] Confirm = new byte[] { 0xF1, 0x37 };
        private const byte Text = 0x20;
        private const byte UI = 0x10;
        private const byte NP = 0x1C;
        private const byte Presets = 0x1D;
        private const byte Save = 0x00;

        private static byte[] TextPacket(int Source, byte[] Field, byte Screen, byte Command, string Text)
        {
            Text = SafeText(Text, 64);
            Text = Pad(Text);

            byte[] TextB = Encoding.ASCII.GetBytes(Text);

            var Payload = new List<byte>(Field.Length + 1 + TextB.Length);
            Payload.AddRange(Field);
            Payload.Add(Screen);
            Payload.AddRange(TextB);

            byte PayloadLength = (byte)((Payload.Count + 3) & 0x7F);

            var packet = new List<byte>(64)
            {
                0xF0,
                0x7D, 0x00,
                0x79, 0x00, 0x7D,
                (byte)((Source - 1) & 0x7F),
                0x00,
                0x02, 0x01, 0x01, 0x02, 0x01, 0x01,
                0x00, 0x00,
                0x01, 0x00,
                PayloadLength, 0x00, Command
            };

            packet.AddRange(Payload);

            packet.Add(0x00);
            packet.Add(0x00);
            packet.Add(0xF7);

            packet[^2] = Checksum(packet);

            return packet.ToArray();
        }

        private static byte Checksum(List<byte> data)
        {
            int sum = 0;
            for (int i = 0; i < data.Count - 2; i++)
                sum += data[i];

            sum += (data.Count - 2);
            return (byte)(sum & 0x7F);
        }

        private static string SafeText(string? Text, int Max)
        {
            if (string.IsNullOrEmpty(Text))
                return "";

            var safe = new string(Text.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
            return (safe.Length > Max) ? safe.Substring(0, Max) : safe;
        }

        private static string Pad(string Text)
        {
            const int Width = 16;
            if (string.IsNullOrEmpty(Text))
                return new string(' ', Width);

            if (Text.Length >= Width)
                return Text;

            return Text + new string(' ', Width - Text.Length);
        }

        public static void SendNP(this SerialConnection Serial, int Source, string Channel, string Artist, string Title)
        {
            lock (NPLock)
            {
                Serial.Write(TextPacket(Source, DisplayStuff.Channel, NP, Text, Channel ?? ""));
                Thread.Sleep(25);

                Serial.Write(TextPacket(Source, Category, NP, Text, "N/A"));
                Thread.Sleep(25);

                Serial.Write(TextPacket(Source, DisplayStuff.Artist, NP, Text, Artist ?? ""));
                Thread.Sleep(25);

                Serial.Write(TextPacket(Source, DisplayStuff.Title, NP, Text, Title ?? ""));
                Thread.Sleep(25);
            }
        }

        public static void SendChannelandCat(this SerialConnection Serial, int Source, string Name, string Cat)
        {
            // Page 4 has a channel and category selection but we dont have categories so
            lock (NPLock)
            {
                Serial.Write(TextPacket(Source, Channel, NP, Text, Name ?? ""));
                Thread.Sleep(25);

                Serial.Write(TextPacket(Source, Category, NP, Text, Cat ?? "N/A"));
                Thread.Sleep(25);
            }
        }

        public static void SendPresets(this SerialConnection Serial, int Source, PresetManager PresetManager)
        {
            lock (NPLock)
            {
                int Num = PresetManager.CurrentBank;
                var BankPresets = PresetManager.GetBankPresets(Num);

                var BankName = $"BANK{Num}";

                Serial.Write(TextPacket(Source, BankHeader, Presets, Text, BankName));
                Thread.Sleep(60);

                byte[][] Fields = new byte[][]
                {
                    M1, M2, M3,
                    M4, M5, M6
                };

                for (int i = 0; i < 6; i++)
                {
                    string Name = (i < BankPresets.Length && BankPresets[i] != null)
                        ? (BankPresets[i].ChannelName ?? "------")
                        : "------";

                    if (string.IsNullOrWhiteSpace(Name))
                        Name = "------";

                    Serial.Write(TextPacket(Source, Fields[i], Presets, Text, Name));
                    Thread.Sleep(60);
                }
            }
        }

        public static void SendSaved(this SerialConnection serial, int source, int presetNumber)
        {
            lock (NPLock)
            {
                string message = $"M{presetNumber} SAVED";
                serial.Write(TextPacket(source, Confirm, Save, UI, message));
            }
        }
    }

    public sealed class SXM
    {
        private readonly SerialConnection Serial;
        private readonly int Source;
        private readonly PresetManager PresetManager;
        private readonly Func<int, Task> Tune;
        private CancellationTokenSource? SongPoll;
        private int NPGen = 0;

        public Display? Display { get; private set; }
        public bool InPresetMode { get; private set; }
        public int Index { get; private set; }
        public string CurrentUUID { get; private set; } = "";
        private string LastSong = "";

        public SXM(SerialConnection Serial, int Source, PresetManager PresetManager, Func<int, Task> Tune)
        {
            this.Serial = Serial;
            this.Source = Source;
            this.PresetManager = PresetManager;
            this.Tune = Tune;
        }

        public void SetDisplay(Display display) => Display = display;
        public void OnChannelShown(int Index)
        {
            if (Index < 0 || Index >= ChannelInfo.Channels.Count)
                return;

            this.Index = Index;

            var Channel = ChannelInfo.Channels[Index];
            CurrentUUID = Channel.UUID;
            StopNP();

            Serial.SendChannelandCat(Source, Channel.Name, "N/A");
            StartNP(Channel.Name, Channel.UUID);
        }

        private void StopNP()
        {
            Interlocked.Increment(ref NPGen);

            try { SongPoll?.Cancel(); } catch { }
            SongPoll = null;
            LastSong = "";
        }

        private void RestartNP()
        {
            if (Index < 0 || Index >= ChannelInfo.Channels.Count)
                return;

            var ch = ChannelInfo.Channels[Index];
            Serial.SendChannelandCat(Source, ch.Name, "N/A");
            StartNP(ch.Name, ch.UUID);
        }

        private void StartNP(string Name, string UUID)
        {
            StopNP();
            SongPoll = new CancellationTokenSource();
            var token = SongPoll.Token;

            int Gen = Interlocked.Increment(ref NPGen);
            _ = Task.Run(async () =>
            {
                await Task.Delay(750, token);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (Gen != Volatile.Read(ref NPGen))
                            return;

                        if (!string.Equals(CurrentUUID, UUID, StringComparison.Ordinal))
                            return;

                        await Task.Delay(2000, token);

                        var songs = await ChannelInfo.GetSongInfo(UUID);
                        if (!songs.TryGetValue(Name, out var song))
                            continue;

                        var key = $"{song.Artist}|{song.Title}";
                        if (string.IsNullOrWhiteSpace(key) || key == LastSong)
                            continue;

                        LastSong = key;
                        if (Gen != Volatile.Read(ref NPGen)) return;
                        if (!string.Equals(CurrentUUID, UUID, StringComparison.Ordinal)) return;

                        Serial.SendNP(Source, Name, song.Artist, song.Title);
                    }
                    catch (TaskCanceledException) { break; }
                    catch { }
                }
            }, token);
        }

        public void Refresh()
        {
            if (Index < 0 || Index >= ChannelInfo.Channels.Count)
                return;

            var channel = ChannelInfo.Channels[Index];

            _ = Task.Run(async () =>
            {
                try
                {
                    var Songs = await ChannelInfo.GetSongInfo(channel.UUID);
                    if (Songs.TryGetValue(channel.Name, out var song))
                        Serial.SendNP(Source, channel.Name, song.Artist, song.Title);
                    else
                        Serial.SendChannelandCat(Source, channel.Name, "N/A");
                }
                catch { }
            });
        }

        public void SendBank()
        {
            Serial.SendPresets(Source, PresetManager);
        }

        // This is stupid but it kinda breaks while sending all
        public void EnterPresetMode()
        {
            InPresetMode = true;
            StopNP();
            PresetManager.CurrentBank = 1;
            SendBank();
        }

        public void ExitPresetMode()
        {
            InPresetMode = false;
            _ = Tune(Index).ContinueWith(_ =>
            {
                // Only restart if we’re still not in presets
                if (!InPresetMode)
                    RestartNP();
            });
        }

        public void NextBank()
        {
            PresetManager.NextBank();
            SendBank();
        }

        public void PrevBank()
        {
            PresetManager.PrevBank();
            SendBank();
        }

        public void SavePreset(int Slot)
        {
            if (Index < 0 || Index >= ChannelInfo.Channels.Count)
                return;

            var Channel = ChannelInfo.Channels[Index];

            PresetManager.SavePreset(PresetManager.CurrentBank, Slot, Channel.Name, Channel.UUID, "N/A");
            StopNP();
            InPresetMode = true;
            Serial.SendSaved(Source, Slot);
            SendBank();
        }

        public void PlayPreset(int Slot)
        {
            var Preset = PresetManager.GetPreset(PresetManager.CurrentBank, Slot);
            if (Preset == null || string.IsNullOrEmpty(Preset.ChannelUUID))
                return;

            int Channel = ChannelInfo.Channels.FindIndex(c => c.UUID == Preset.ChannelUUID);
            if (Channel < 0)
                return;

            StopNP();
            _ = Tune(Channel).ContinueWith(_ =>
            {
                Index = Channel;
                CurrentUUID = ChannelInfo.Channels[Channel].UUID;
                RestartNP();
            });
        }

        public void Mem(int Dir)
        {
            var Memory = BuildMemory();
            if (Memory.Count == 0)
                return;

            var UUID = ChannelInfo.Channels[this.Index].UUID;
            int pos = Memory.IndexOf(UUID);
            if (pos < 0)
                pos = -1;

            pos += Dir;

            if (pos >= Memory.Count)
                pos = 0;
            if (pos < 0)
                pos = Memory.Count - 1;

            var New = Memory[pos];
            int NewIndex = ChannelInfo.Channels.FindIndex(c => c.UUID == New);
            if (NewIndex < 0)
                return;

            Index = NewIndex;
            _ = Tune(NewIndex);
        }

        private List<string> BuildMemory()
        {
            var List = new List<string>();
            var Seen = new HashSet<string>();

            for (int BANK = 1; BANK <= 6; BANK++)
            {
                var Presets = PresetManager.GetBankPresets(BANK);
                foreach (var p in Presets)
                {
                    if (p == null) continue;
                    if (string.IsNullOrWhiteSpace(p.ChannelUUID)) continue;
                    if (!Seen.Add(p.ChannelUUID)) continue;

                    List.Add(p.ChannelUUID);
                }
            }

            return List;
        }

        public async Task HandleFavourite(byte Zone, byte Event)
        {
            var Saved = Favourites.GetSaved(Zone, Event);
            if (string.IsNullOrEmpty(Saved))
                return;

            int SavedIndex = ChannelInfo.Channels.FindIndex(c => c.UUID == Saved);
            if (SavedIndex < 0)
                return;

            await Task.Delay(50);
            await Tune(SavedIndex);
        }

        public void Stop()
        {
            SongPoll?.Cancel();
        }
    }
}
