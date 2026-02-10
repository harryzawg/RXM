using RXM.Configuration;
using System;
using System.Collections.Generic;

namespace RXM.Serial
{
    public class Button : EventArgs
    {
        public byte EventId { get; }
        public string EventName { get; }
        public string SourceName { get; }
        public byte Zone { get; }
        public byte[]? Packet { get; }
        public DateTime Timestamp { get; }

        public Button(byte ID, string Name, string Source, byte zone, byte[]? packet)
        {
            EventId = ID;
            EventName = Name;
            SourceName = Source;
            Zone = zone;
            Packet = packet;
            Timestamp = DateTime.Now;
        }
    }

    public class RnetDetector
    {
        private readonly SerialConnection _SM;
        public event EventHandler<Button>? ButtonPressed;
        private static readonly Dictionary<byte, string> Events = RNETConfig.RNET.Events;
        private static readonly Dictionary<int, string> Sources = RNETConfig.RNET.Sources;

        public RnetDetector(SerialConnection serialManager)
        {
            _SM = serialManager;
            _SM.DataReceived += OnDataReceived;
        }

        private void OnDataReceived(object? sender, byte[] packet)
        {
            if (packet.Length < 14) return;
            byte Type = packet[7];
            if (Type != 0x05) return;

            int? source = null;
            byte Zone = packet[2];
            byte Keypad = packet[3];
            byte FavZone = packet.Length > 17 ? packet[17] : (byte)0;

            if (packet[6] >= 0x70 && packet[6] <= 0x75)
            {
                source = packet[6] - 0x70;
            }
            else if (Zone == 0x7D && Keypad <= 0x05)
            {
                source = Keypad;
            }

            byte Id;
            int levels = packet[8];
            int EventPOS = 8 + levels + 4;
            if (EventPOS >= packet.Length) return;
            Id = packet[EventPOS];
            if (Id == 0x00) return;

            string SourceStr = source.HasValue
                ? Sources.GetValueOrDefault(source.Value, $"Source {source.Value + 1}")
                : "Unknown Source";

            if (Zone == 0 && (SourceStr == "SiriusXM")) return;
            if (SourceStr == "Unknown Source") return;
            if (Id == 0xF1) return;
            Console.WriteLine($"[Event (Source)] Source: {SourceStr}, Event ID: 0x{Id:X2}, Zone: {FavZone}");

            if (Events.ContainsKey(Id))
            {
                string Eveent = Events[Id];
                ButtonPressed?.Invoke(this, new Button(Id, Eveent, SourceStr, FavZone, packet));
            }
        }
    }
}
