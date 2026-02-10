using LibVLCSharp.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RXM.SXM
{
    public static class VLC
    {
        private static LibVLC? _libVLC;
        private static MediaPlayer? Player;
        private static Media? CurrentMedia;
        private static readonly object Lock = new();
        private static CancellationTokenSource? WatchCTS;

        public static void Init()
        {
            if (_libVLC != null) return;

            Core.Initialize();
            _libVLC = new LibVLC(new[]
            {
              //  "--quiet",
             //   "--no-xlib",
                "--http-reconnect",
                "--network-caching=3000",
                "--live-caching=3000"
            });

            Player = new MediaPlayer(_libVLC);
            Player.EndReached += (_, __) => OnStreamDied("EndReached");
            Player.EncounteredError += (_, __) => OnStreamDied("EncounteredError");
        }

        private static volatile string? LastURL;
        private static volatile int LastVol = 100;

        public static void Play(string url, int volume = 100)
        {
            Init();

            lock (Lock)
            {
                LastURL = url;
                LastVol = volume;
                Player!.Stop();
                CurrentMedia?.Dispose();
                CurrentMedia = null;

                var media = new Media(_libVLC!, url, FromType.FromLocation);

                media.AddOption(":network-caching=3000");
                media.AddOption(":http-reconnect");
                media.AddOption(":tcp-reconnect");

                CurrentMedia = media;

                Player.Media = CurrentMedia;
                Player.Volume = volume;
                Player.Play();

                StartWatchdog();
            }
        }

        public static void Stop()
        {
            lock (Lock)
            {
                WatchCTS?.Cancel();
                WatchCTS = null;

                Player?.Stop();
                CurrentMedia?.Dispose();
                CurrentMedia = null;
            }
        }

        public static void Pause()
        {
            lock (Lock)
            {
                Player?.Pause();
            }
        }

        private static void OnStreamDied(string why)
        {
            StartWatchdog(Force: true);
        }

        private static void StartWatchdog(bool Force = false)
        {
            if (WatchCTS != null && !WatchCTS.IsCancellationRequested) return;

            WatchCTS = new CancellationTokenSource();
            var token = WatchCTS.Token;

            _ = Task.Run(async () =>
            {
                if (!Force)
                    await Task.Delay(1500, token).ConfigureAwait(false);

                int attempt = 0;

                while (!token.IsCancellationRequested)
                {
                    bool isPlaying;
                    lock (Lock)
                    {
                        isPlaying = Player != null && Player.IsPlaying;
                    }

                    if (isPlaying) return;

                    var url = LastURL;
                    if (string.IsNullOrWhiteSpace(url)) return;

                    int Delay = Math.Min(20000, 1000 * (1 << Math.Min(attempt, 4)));

                    try
                    {
                        Play(url, LastVol);
                    }
                    catch
                    {
                    }

                    attempt++;
                    await Task.Delay(Delay, token).ConfigureAwait(false);
                }
            }, token);
        }
    }
}
