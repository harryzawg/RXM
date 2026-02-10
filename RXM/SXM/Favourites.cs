using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace RXM.Serial
{
    public static class Favourites
    {
        private static readonly string FavouritesJson = Configuration.Configuration.Config.Paths.FavouritesJson;
        private static Dictionary<string, Dictionary<string, string>> SavedChannels = new();

        static Favourites()
        {
            if (File.Exists(FavouritesJson))
            {
                // add safeguard here later because if theres no favourites it bugs out
                var json = File.ReadAllText(FavouritesJson);
                SavedChannels = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json)
                         ?? new();
            }
        }

        public static void Saved(byte[] packet, string Channel)
        {
            if (packet.Length < 4) return;
            byte zone = packet[2];
            byte keypad = packet[3];
            // packet in which the keypad sends contains the ascii for the f1/f2 saved message for some reason? weird but it works
            string ascii = System.Text.Encoding.ASCII.GetString(packet);

            if (ascii.Contains("F1 SAVED"))
            {
                Save(zone, "F1", Channel);
                Console.WriteLine($"[Favorites] F1 Saved for zone {zone:X2}");
            }
            else if (ascii.Contains("F2 SAVED"))
            {
                Save(zone, "F2", Channel);
                Console.WriteLine($"[Favorites] F2 Saved for zone {zone:X2}");
            }
        }

        public static string? GetSaved(byte Zone, byte eventId)
        {
            string fav = eventId switch
            {
                0x6F => "F1",
                0x70 => "F2",
                _ => null
            };

            if (fav != null && SavedChannels.TryGetValue(Zone.ToString("X2"), out var dict) && dict.TryGetValue(fav, out var uuid))
            {
                Console.WriteLine($"[Favourites] Switching to saved {fav} channel for zone {Zone:X2}: {uuid}");
                return uuid;
            }

            return null;
        }

        private static void Save(byte Zone, string fav, string UUID)
        {
            string RealZone = Zone.ToString("X2");
            if (!SavedChannels.ContainsKey(RealZone))
                SavedChannels[RealZone] = new Dictionary<string, string>();

            SavedChannels[RealZone][fav] = UUID;
            File.WriteAllText(FavouritesJson, JsonSerializer.Serialize(SavedChannels, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}