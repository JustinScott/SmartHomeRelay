# Media player (text-to-speech and media on the PC)

> This is the Media player feature in [spec section 5.9](../spec.md), milestone 8.

Turns the PC into a `media_player` entity in Home Assistant. It shows what Windows' own media controls show (Spotify, browsers, most players) and controls it: play/pause, next, previous, seek, volume and mute. The PC also appears wherever Home Assistant asks which player to use, so you can send it text-to-speech, sounds from the media browser, or `media_player.play_media` from automations. While those play, other apps are turned down and come back afterwards.

Home Assistant's built-in MQTT integration has no media player type, so this uses a community add-on, **MQTT Media Player**, installed through HACS.

```
Home Assistant ── MQTT Media Player add-on ──MQTT──▶ Smart Home Relay ──▶ Windows media session
        ▲                                                   │
        └──────── downloads the TTS / media file ◀──────────┘   (from HA's own address)
```

## Tested versions

| Component | Tested version | Notes |
|---|---|---|
| MQTT Media Player ([bkbilly/mqtt_media_player](https://github.com/bkbilly/mqtt_media_player)) | 2026.10.1 | Git tag `2026.10.1` (its `manifest.json` says 2026.10.0). Other versions may use different settings. |

Before updating the add-on in HACS, check this table.

## Before you start: about the add-on

The add-on is third-party Python code that runs inside Home Assistant with full access to it. It is small (about 500 lines, no extra packages) and was reviewed at the tested version, but you should still:

- Read its code if you can, and install only the tested version.
- Turn off automatic updates for it in HACS and update deliberately.

## 1. Install the add-on

1. In Home Assistant open **HACS** and search for **MQTT Media Player**. If it isn't listed, add it as a custom repository: **⋮ → Custom repositories**, URL `https://github.com/bkbilly/mqtt_media_player`, type **Integration**.
2. Open it, choose **Download**, and pick version **2026.10.1**.
3. Add this line on its own to `configuration.yaml` (for example with the File editor app), at the top level, not indented under another section:

   ```yaml
   mqtt_media_player:
   ```

   The add-on's own documentation doesn't mention this, but without it Home Assistant never starts the add-on, so it never sees the player. (The add-on only starts listening from its setup code, which Home Assistant runs only for integrations that are in `configuration.yaml` or already have an entry.)
4. Check the configuration (**Developer tools → YAML → Check configuration**), then **Restart Home Assistant**. A quick reload isn't enough.

There's nothing else to set up in the add-on. After that it creates the player when Smart Home Relay announces it.

## 2. Check Home Assistant's local address

For text-to-speech and the media browser, Home Assistant gives the PC a link to the audio on its own address, and the PC downloads it from there.

1. In Home Assistant: **Settings → System → Network → Home Assistant URL**. The **Local network** address must be one the PC can reach, for example `http://192.168.1.10:8123`.
2. Check it on the PC: open that address in a browser. You should see the Home Assistant login page.

If that address is `https` with a certificate Windows doesn't trust (self-signed), the download fails. Smart Home Relay never skips certificate checks. Use the `http` local address, or a certificate Windows trusts.

## 3. Turn on the media player in Smart Home Relay

Open `settings.json` (tray menu → **Settings**). The `mediaPlayer` section under `features` is added automatically, turned off. Set `enabled` to `true` and save. It starts right away; no restart needed.

```json
"mediaPlayer": {
  "enabled": true,
  "interruption": "duck",
  "duckLevelPercent": 20,
  "maxDownloadBytes": 20971520,
  "downloadTimeoutSeconds": 15,
  "positionIntervalSeconds": 5
}
```

| Setting | Default | Meaning |
|---|---|---|
| `interruption` | `duck` | What happens to other audio while Home Assistant's audio plays. `duck`: other apps are faded down and back up afterwards. `pause`: what's playing is paused and resumed afterwards. |
| `duckLevelPercent` | `20` | With `duck`: other apps play at this percentage of their own volume. `0` silences them. |
| `maxDownloadBytes` | 20 MB | Largest file that will be played. |
| `downloadTimeoutSeconds` | `15` | Longest the download may take. |
| `positionIntervalSeconds` | `5` | How often the playback position is sent while playing. |

## 4. Check it

1. In Home Assistant, a new device named after the PC plus "Media Player" (for example **BOX1 Media Player**) has the player. It's a separate device from the PC's other sensors and buttons because the add-on creates its own; give both the same area. The add-on also adds an **MQTT Media Player** entry under **Settings → Devices & services**.
2. Play something on the PC (Spotify, YouTube in a browser). The player shows the title and artist, and play/pause from Home Assistant works.
3. Try text-to-speech: **Developer tools → Actions**, choose `tts.speak`, pick your TTS entity, set **Media player** to the PC, enter a message, and run it. The music gets quieter, the message plays, and the music comes back.

## Good to know

- **One at a time.** A new sound stops the one playing. The other apps stay lowered between back-to-back sounds.
- **Your changes win.** If you change an app's volume while it's lowered, Smart Home Relay leaves it where you put it.
- **Interrupted?** If Smart Home Relay closes or the PC sleeps while a sound plays, the lowered apps get their volume back the next time it starts or the PC wakes.
- **Not lowered:** apps that take exclusive control of the audio device, and Windows' own system sounds.
- **Volume and mute** on the player are the PC's main volume, the same as the Audio feature's slider and mute switch.
- **Only audio** from `http` or `https` links is played. Local files and other link types are refused.

## Turning it off or removing it

1. Set `features.mediaPlayer.enabled` to `false`. The player then shows **Off** with no track.
2. The add-on keeps its player even when Smart Home Relay withdraws it. To remove it from Home Assistant: **Settings → Devices & services → MQTT Media Player**, then **⋮ → Delete** on the PC's entry. It won't come back unless you turn the feature on again.
3. To remove the add-on itself: delete those entries, then remove it in HACS and restart Home Assistant.

## Troubleshooting

| Symptom | What to do |
|---|---|
| No media player in Home Assistant | Check that the add-on is installed, `mqtt_media_player:` is in `configuration.yaml`, and Home Assistant was restarted (step 1). If you changed `mqtt.discoveryPrefix` from `homeassistant`, the log says so: the add-on only listens on `homeassistant`, so Smart Home Relay always publishes the player there. |
| Player shows nothing while music plays | The app must show up in Windows' media controls (the volume flyout's media panel). Some apps don't use them. |
| TTS or media doesn't play | Open the log (tray menu → **Open logs folder**). "Could not download the audio from …" names the host and the reason: check step 2 (address, certificate, firewall). "not audio/*" means the link wasn't an audio file. |
| Other apps stayed quiet | Restart Smart Home Relay; it restores the saved levels. If you'd changed them meanwhile, set them in Windows' volume mixer. |

## Security notes

- Only `http` and `https` links are played, after a size, time, and type check. Smart Home Relay never opens local files or paths named in a message.
- The links Home Assistant sends carry a signature, so the log only ever shows the host name.
- Anything that can publish to Smart Home Relay's MQTT command topics can play sounds on the PC, the same as for the other buttons and switches. Keep your broker's login private.
