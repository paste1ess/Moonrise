using DiscordRPC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Windows.Media.Playback;

namespace Moonrise.Services
{
    internal interface IDiscordRpcService : IDisposable
    {
        void SetPresence(string title, string artist);
        void ClearPresence();
    }

    internal class DiscordRpcService : IDiscordRpcService, IDisposable
    {
        private static readonly string discordAppId = "1508917641530708039";
        private static SettingsService settings => SettingsService.Instance;

        private DiscordRpcClient client;
        private PlaybackService playback => PlaybackService.Instance;

        public DiscordRpcService()
        {
            client = new(discordAppId);
            client.OnReady += (sender, msg) =>
            {
                Debug.WriteLine("Connected to discord with user {0}", msg.User.Username);
                Debug.WriteLine("Avatar: {0}", msg.User.GetAvatarURL(User.AvatarFormat.WebP));
                Debug.WriteLine("Decoration: {0}", msg.User.GetAvatarDecorationURL());
            };

            client.Initialize();

            settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DiscordRpcEnabled")
            {
                if (settings.DiscordRpcEnabled)
                {
                    var currentTrack = playback.CurrentTrack;
                    if (currentTrack != null) SetPresence(currentTrack.Title, currentTrack.Artist);
                } else
                {
                    ClearPresence();
                }
            }
        }

        public void SetPresence(string title, string artist)
        {
            if (!settings.DiscordRpcEnabled) return;
            client.SetPresence(new RichPresence()
            {
                Details = title,
                State = artist,
                StatusDisplay = StatusDisplayType.State,
                Type = ActivityType.Listening,
                Timestamps = BuildTimestamps()
            });            
        }

        private Timestamps BuildTimestamps()
        {
            return new Timestamps()
            {
                Start = DateTime.UtcNow.AddSeconds(-playback.CurrentTrackTime.TotalSeconds),
                End = DateTime.UtcNow.AddSeconds((playback.CurrentTrack?.Duration.TotalSeconds ?? 0) - playback.CurrentTrackTime.TotalSeconds)
            };
        }

        public void ClearPresence()
        {
            client.ClearPresence();
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
