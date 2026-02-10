using RXM.Serial;
using System.Collections.Generic;
using System.Linq;

namespace RXM.SXM
{
    public class Display
    {
        private readonly SerialConnection _serial;
        private readonly object _lock = new();

        public Display(SerialConnection serial)
        { _serial = serial; }

        // sends a message to a UNO-TS2(d) Display as well as a UNO-S2 keypad.
        public void SendDisplay(int source, string text)
        {
            text = text.Length > 40 ? text[..40] : text;
            var packet = new List<byte>
            {
                0xF0,0x7D,0x00,0x79,0x00,0x7D,0x00,0x00,
                0x02,0x01,0x01,0x02,0x01,0x01,0x00,0x00,
                0x01,0x00,0x00,0x00,(byte)(0x10+source),0x00,0x00
            };
            foreach (var c in text) packet.Add((byte)c);
            packet.Add(0x00);
            packet[18] = (byte)(packet.Count - 20);
            packet.Add((byte)((packet.Sum(b => b) + packet.Count) & 0x7F));
            packet.Add(0xF7);

            lock (_lock)
            {
                _serial.Write(packet.ToArray());
            }
        }

        // i dont even think this works, but it should send just a display message to a single zone
        public void SendDisplayZone(int source, int zone, string text)
        {
            text = text.Length > 40 ? text[..40] : text;
            var packet = new List<byte>
            {
                0xF0, 0x7D, 0x00, 0x79, 0x00, 0x7D, 0x00, 0x00,
                0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, (byte)(0x10 + source), 0x00, 0x00
            };

            foreach (var c in text) packet.Add((byte)c);
            packet.Add(0x00);

            packet[18] = (byte)(packet.Count - 20);
            packet[6] = (byte)(zone & 0xFF);

            packet.Add((byte)((packet.Sum(b => b) + packet.Count) & 0x7F));
            packet.Add(0xF7);

            lock (_lock)
            {
                _serial.Write(packet.ToArray());
            }
        }

        // sends a message to a UNO-S2 Keypad
        public void SendField(int source, byte field, string text)
        {
            text = text.Length > 40 ? text[..40] : text;
            var packet = new List<byte>
            {
                0xF0,0x7D,0x00,0x79,0x00,0x7D,0x00,0x00,
                0x02,0x01,0x01,0x02,0x01,0x01,0x00,0x00,
                0x01,0x00,0x00,0x00,(byte)(0x20+source),field,0x00
            };
            foreach (var c in text) packet.Add((byte)c);
            packet.Add(0x00);
            packet[18] = (byte)(packet.Count - 20);
            packet.Add((byte)((packet.Sum(b => b) + packet.Count) & 0x7F));
            packet.Add(0xF7);

            lock (_lock)
            {
                _serial.Write(packet.ToArray());
            }
        }
    }
}
