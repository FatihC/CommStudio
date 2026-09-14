# CommStudio

A lightweight Windows terminal for **TCP, serial and MQTT**. Send reusable commands, inspect ASCII/Hex traffic, subscribe to MQTT topics and keep message history across sessions. Includes light/dark themes, adjustable panels and word wrapping.

See the **[full user guide](USER_GUIDE.md)** for detailed settings, examples and troubleshooting notes.

## Build and run

Requires **Windows x64 with .NET Framework 4.8**. From the project folder, run:

```powershell
.\build.ps1
.\dist\CommStudio.exe
```

The result is a portable executable; no installation is needed. Build outputs are excluded from the repository. MQTTnet 4.3.7.1207 and its MIT license are embedded, so no separate DLL is required. See [dependency details](vendor/MQTTnet/README.md).

## TCP and serial

Choose a transport tab, enter its connection settings and press **Connect**. Disconnect before switching tabs or changing port/framing/flow settings; baud rate can also change while connected. Settings are remembered; startup does not connect automatically.

| Transport | Setup |
| --- | --- |
| TCP | Enter the host and port. |
| Serial | Select or type a COM port; set baud, data bits, parity, stop bits and flow control. **↻** refreshes ports. |

Serial defaults are **9600 baud, 8N1, no flow control**, with DTR/RTS off. Match the device's settings. TCP and serial share command rows and an ASCII/Hex log; incoming data is normally grouped after one second of silence.

### Send commands

| Mode | Accepted input |
| --- | --- |
| ASCII | Text, control tokens such as `[ACK]`, `[CR]`, `[LF]`, and byte tokens such as `[80]` or `[0x80]`. |
| Hex | `06410D`, `06 41 0D`, or `0x06 0x41 0x0D`. |

**Enter** sends; **Shift+Enter** inserts a line break. No line ending is added automatically.

### Meter baud switching

Enable **IEC 62056-21: auto baud after ACK** for mode C meters. For example, connect at **300 baud**, request identification, then promptly send `[ACK]050[CR][LF]` to switch to **9600 baud** after transmission. Default switch delay: **250 ms**. This changes baud only; it does not negotiate mode E/DLMS framing. For manual changes, edit **BAUD** and choose **Apply baud (connected)**. See [timing and ACK frames](USER_GUIDE.md#meter-baud-switching-iec-62056-21-mode-c).

## MQTT

Supports **MQTT 3.1.1 and 5.0** over TCP, TLS and WebSockets, with username/password authentication, Last Will and advanced options. On the **MQTT** tab:

1. Select a saved connection or **Yeni Ekle**. Enter Name, Host and connection settings, then **Connect**. New connections save automatically; use **Kaydet** to persist edits to existing profiles.
2. Enter a topic filter and choose **Abone ol** to subscribe; **Kaldır** unsubscribes.
3. In **Publish**, enter a topic and payload, then **Gönder**. Messages use UTF-8, QoS 0, no retain and no added line endings. Subscription requests also use QoS 0.
4. Save publish messages with **Kaydet**, update with **Güncelle**, or remove with **Sil**.

Message cards show topic, timestamp and delivery metadata. Choose **Plain Text**, **JSON** or **Hex** without changing payload bytes. Select text to copy, or right-click for copying, format and deletion options.

Apply filters with **Enter** or **Filtrele**. Queries support words, JSON fields, dotted paths, `topic:`, `direction:`, **AND**, **OR**, **NOT** and parentheses; for example, `serialNumber:12345 AND function:heartbeat`. **?** provides examples. An empty filter restores all messages; invalid queries preserve previous results.

History persists per connection, including filtered-out messages. **Clear** deletes that connection's entire history. **Auto reconnect** restores subscriptions after an established connection drops; manual disconnect cancels retries.

## Display and local data

Use **Word wrap**, draggable dividers and **Dark mode / Light mode** to adjust the workspace. For the TCP/serial log, **Ctrl+mouse wheel** or **Ctrl++ / Ctrl+-** changes font size; **Ctrl+0** resets it.

Settings, MQTT profiles, histories and saved messages live under `%LOCALAPPDATA%\CommStudio`. Profile passwords use Windows encryption for the current user; saved publish messages are plaintext. See the [full guide](USER_GUIDE.md) for storage files, filter semantics, protocol limits and build/test options.
