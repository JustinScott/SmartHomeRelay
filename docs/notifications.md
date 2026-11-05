# Notifications on the PC

> This is the Notifications feature in [spec section 5.6](../spec.md), milestone 9.

Home Assistant can show Windows notifications on the PC: a title and message, optionally an image, a link that opens when you click the notification, and up to 5 link buttons. They show as **Smart Home Relay** and stay in the notification center for a day.

The feature is on by default (`features.notifications.enabled` in `settings.json`).

## Simple: the notify entity

The PC's device has a **Notification** entity (for example `notify.box1_notification`). Use it like any notify entity:

```yaml
action: notify.send_message
target:
  entity_id: notify.box1_notification
data:
  message: The washer is done
```

Home Assistant's MQTT notify entities send only the message, so a `title` here is ignored. For a title, an image, or buttons, use the JSON below.

## Full: JSON on the notification topic

Publish JSON to `shr/{device id}/commands/notification` (the topic the original app used, so existing automations keep working):

```yaml
action: mqtt.publish
data:
  topic: shr/BOX1/commands/notification
  payload: >
    {
      "Title": "Doorbell",
      "Message": "Someone is at the front door",
      "HeroImageURL": "http://192.168.1.10:8123/api/camera_proxy/camera.front_door?token={{ state_attr('camera.front_door', 'access_token') }}",
      "ActivationURL": "http://192.168.1.10:8123/lovelace/cameras",
      "Buttons": [
        { "Label": "Open cameras", "URL": "http://192.168.1.10:8123/lovelace/cameras" }
      ]
    }
```

The same JSON also works as the `message` of `notify.send_message`.

| Field | What it does |
|---|---|
| `Title`, `Message` | The text. At least one is needed. |
| `HeroImageURL` | An image shown across the top. PNG, JPEG or GIF, up to 10 MB, downloaded within 10 seconds (`imageMaxBytes`, `imageDownloadTimeoutSeconds`). If it can't be downloaded, the notification shows without it. |
| `ActivationURL` | Opened in your browser when you click the notification. |
| `Buttons` | Up to 5, each with a `Label` and a `URL` opened in your browser. |
| `Audio` | Reserved; ignored. |

Field names ignore letter case (`heroImageUrl` works too).

## Rules

- Links and images must be full `http` or `https` URLs. File paths, network shares and other link types are ignored (and logged), and the notification still shows. Smart Home Relay never opens files named in a message.
- The PC must be able to reach the image URL; a Home Assistant camera image needs a URL with its access token, as above.
- `EventID` (Frigate) is no longer supported. Send `HeroImageURL` and `ActivationURL` instead.
- Notifications arrive in order; if many arrive at once, the oldest waiting ones are dropped (logged).

## If nothing appears

- Check **Settings → System → Notifications** in Windows: notifications must be on, and **Smart Home Relay** must be allowed. With Do Not Disturb on, they go straight to the notification center.
- The Smart Home Relay log (tray menu → **Open logs folder**) says why a field was ignored or an image wasn't shown.
