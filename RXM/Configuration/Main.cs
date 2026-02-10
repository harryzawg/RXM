using System;
using System.IO;

namespace RXM.Configuration
{
    public static class Configuration
    {
        public static AppConfig Config { get; } = new AppConfig();
    }

    public class AppConfig
    {
        public SerialConfig Serial { get; set; } = new();
        public PathsConfig Paths { get; set; } = new();
        public PcConfig PC { get; set; } = new();
        public DisplayConfig Display { get; set; } = new();
        public VLCConfig VLC { get; set; } = new();
    }

    public class SerialConfig
    {
        #if DEBUG
                public string Port { get; set; } = "COM6";
                public int BaudRate { get; set; } = 19200;
        #else
                public string Port { get; set; } = "COM3";
                public int BaudRate { get; set; } = 19200;
        #endif
        }

    public class DisplayConfig
    {
        // max length is 37, and theres 2 dots so do 35
        public int TextLength { get; set; } = 35;
        public int ChannelDelay { get; set; } = 4000;
        public int ArtistDelay { get; set; } = 5000;
        public int TitleDelay { get; set; } = 5000;
    }

    public class PathsConfig
    {
        public string ChannelJson { get; set; } = "XM_CHANNELS.json";
        public string FavouritesJson { get; set; } = "Favourites.json";
        public string PresetsJson { get; set; } = Path.Combine(AppContext.BaseDirectory, "SiriusXM", "presets.json");
    }

    public class PcConfig
    {
        public string BaseUrliBridge { get; set; } = "http://10.0.0.44:3100/";
        public string BaseUrlVCD { get; set; } = "http://10.0.0.44:5000/";
    }

    public class VLCConfig
    {
        public int Volume { get; set; } = 100;
    }
}
