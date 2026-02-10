using System.Collections.Generic;

namespace RXM.Configuration
{
    public static class RNETConfig
    {
        public static RNET RNET { get; } = new RNET();
    }

    public class RNET
    {
        public Dictionary<byte, string> Events { get; set; } = new()
        {
            // Keypad and source
            { 0x2F, "Track Forward" },
            { 0x30, "Track Reverse" },
            { 0x6E, "Pause" },
            { 0x73, "Play" },
            { 0x6D, "Stop" },
            { 0x0E, "Channel Up" },
            { 0x0F, "Channel Down" },
            { 0x1C, "Track Forward KEYPAD / Search Forward" },
            { 0x1D, "Track Reverse KEYPAD / Search Rewind" },

            // favourites
            { 0x6F, "Fav 1" },
            { 0x70, "Fav 2" },

            // Extra source buttons
            { 0x1A, "Play" },
            { 0x1E, "Pause" },
            { 0x1B, "Stop" },

            // Touchscreen buttons (VCD)
            { 0x01, "1" },
            { 0x02, "2" },
            { 0x03, "3" },
            { 0x04, "4" },
            { 0x05, "5" },
            { 0x06, "6" },
            { 0x07, "7" },
            { 0x08, "8" },
            { 0x09, "9" },
            { 0x0A, "0" },
            { 0x11, "Enter" },
            { 0x2E, "Random Disc" },

            // i bridge
            { 0x2A, "Prev (iBridge) (ST2 Prev Bank)" },
            { 0x29, "Next (iBridge) (ST2 Next Bank)" },
            { 0x27, "Display" },
            { 0x5A, "Select 1" },
            { 0x5B, "Select 2" },
            { 0x5C, "Select 3" },
            { 0x5D, "Select 4" },
            { 0x5E, "Select 5" },
            { 0x5F, "Select 6" },
            { 0x12, "Back (iBridge)" },
            { 0x67, "Cursor Left" },
            { 0x68, "Cursor Right" },
            { 0x6A, "Letter Down" },
            { 0x69, "Letter Up" },

            // st2s/tuner
            { 0x3C,"M1 Save" },
            { 0x3D,"M2 Save" },
            { 0x3E,"M3 Save" },
            { 0x3F,"M4 Save" },
            { 0x40,"M5 Save" },
            { 0x41,"M6 Save" },
            { 0x75, "M1" },
            { 0x76, "M2" },
            { 0x77, "M3" },
            { 0x78, "M4" },
            { 0x79, "M5" },
            { 0x7A, "M6" },
        };

        public Dictionary<int, string> Sources { get; set; } = new()
        {
            { 0, "SiriusXM" },
            { 1, "Roon" },
            { 2, "iPod" },
            { 3, "AirPort EXP" },
            // { 4, "Computer" },
            //{ 5, "Sat2" }
        };
    }
}
