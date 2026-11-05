using System.Text;
using System.Text.Json;
using SmartHomeRelay.Core.Entities;
using SmartHomeRelay.Core.Mqtt;

namespace SmartHomeRelay.Core.Features.MediaPlayer;

/// <summary>
/// The discovery config in the format of the MQTT Media Player add-on, tested version 2026.10.1 (spec 5.9).
/// The add-on infers its features from which command topics are present. Deterministic for snapshot tests.
/// </summary>
public static class MediaPlayerConfig
{
    /// <summary>Builds the config.</summary>
    public static string Build(CustomComponentTopics topics)
    {
        ArgumentNullException.ThrowIfNull(topics);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("name", topics.Device.Name);

            json.WriteStartObject("availability");
            json.WriteString("topic", topics.Availability);
            json.WriteString("payload_available", MqttTopics.Online);
            json.WriteString("payload_not_available", MqttTopics.Offline);
            json.WriteEndObject();

            json.WriteString("state_state_topic", topics.State(MediaPlayerKeys.State));
            json.WriteString("state_title_topic", topics.State(MediaPlayerKeys.Title));
            json.WriteString("state_artist_topic", topics.State(MediaPlayerKeys.Artist));
            json.WriteString("state_album_topic", topics.State(MediaPlayerKeys.Album));
            json.WriteString("state_duration_topic", topics.State(MediaPlayerKeys.Duration));
            json.WriteString("state_position_topic", topics.State(MediaPlayerKeys.Position));
            json.WriteString("state_volume_topic", topics.State(MediaPlayerKeys.Volume));
            json.WriteString("state_mute_topic", topics.State(MediaPlayerKeys.Mute));
            json.WriteString("state_mediatype_topic", topics.State(MediaPlayerKeys.MediaType));

            json.WriteString("command_volume_topic", topics.Command(MediaPlayerKeys.Volume));
            json.WriteString("command_mute_topic", topics.Command(MediaPlayerKeys.Mute));
            json.WriteString("command_play_topic", topics.Command(MediaPlayerKeys.Play));
            json.WriteString("command_pause_topic", topics.Command(MediaPlayerKeys.Pause));
            json.WriteString("command_playpause_topic", topics.Command(MediaPlayerKeys.PlayPause));
            json.WriteString("command_next_topic", topics.Command(MediaPlayerKeys.Next));
            json.WriteString("command_previous_topic", topics.Command(MediaPlayerKeys.Previous));
            json.WriteString("command_playmedia_topic", topics.Command(MediaPlayerKeys.PlayMedia));
            json.WriteString("command_seek_topic", topics.Command(MediaPlayerKeys.Seek));

            topics.WriteDevice(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
