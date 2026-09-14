# CommStudio

CommStudio is a lightweight Windows TCP, serial and MQTT terminal. TCP/serial sessions offer reusable command rows and ASCII/Hex history; MQTT has topic subscriptions, a message-card log and saved publish messages.

## Run

Build the project using the instructions below, then open `dist\CommStudio.exe`. No installation is required on a Windows system with .NET Framework 4.8. Build outputs are not included in the repository.

Settings are saved for the current Windows user under `%LOCALAPPDATA%\CommStudio\settings.json`.

## Connections

Choose **TCP** or **Serial** using the tabs beside CommStudio in the top action row. Connection settings appear directly below the tabs. One connection can be active at a time; disconnect before switching tabs or changing port/framing/flow settings. Baud rate can change while connected. Both transports share the command rows and ASCII/Hex log, including the one-second receive grouping interval.

- **TCP:** enter a host and port, then press **Connect**.
- **Serial:** select a COM port (or type its name), baud rate, data bits, parity, stop bits and flow control, then press **Connect**. Use **↻** to refresh the port list after attaching a device. Custom baud rates can be typed; support depends on the device and driver.
- **DTR / RTS:** set these output signals before connecting if the device requires them. With RTS/CTS flow control, RTS is managed automatically. Input modem-line indicators are not included in this release.
- The selected tab and each transport's connection settings are remembered between sessions. Opening the app does not connect automatically.

Serial defaults are **9600 baud, 8 data bits, no parity, 1 stop bit, no flow control**, with DTR/RTS off. Match these settings to the connected device.

### Meter baud switching (IEC 62056-21 mode C)

Enable **IEC 62056-21: auto baud after ACK** to switch speed automatically after sending a mode C acknowledgement. This option is off by default and is remembered.

For the Luna example:

1. Connect at **300 baud**, using the data bits/parity/stop bits required by the meter.
2. Send the initial identification command and wait for its response.
3. Send **`[ACK]050[CR][LF]`** in ASCII mode (or `06 30 35 30 0D 0A` in Hex).
4. CommStudio sends the ACK at 300 baud, drains the output and observes the transmission-time guard, then changes the open port to **9600 baud**. Later commands use 9600 baud.

**Switch delay (ms)** is the minimum time from starting the ACK transmission to changing baud, default **250 ms**. It is measured from write start, not added after output drain. CommStudio also enforces the calculated frame transmission time: six bytes at 300 baud, 7E1 take 200 ms. Some USB adapters return from output drain before the physical transmission is finished; this guard avoids switching immediately on that early return. The delay can be adjusted from 0 to 1000 ms (0 still enforces the calculated frame time). A long delay can miss a fast meter response, so start at 250 ms and use the timing log to troubleshoot. See [OpenMUC's USB adapter timing guidance](https://www.openmuc.org/javadoc-j62056/org/openmuc/j62056/Iec21Port.Builder.html#setBaudRateChangeDelay(int)).

In IEC auto-baud mode, a complete identification line such as `/LUN5...` appears immediately when its CR/LF arrives. Send the ACK promptly after this line: the normal one-second log grouping delay no longer delays this handshake prompt. Other received data retains the one-second grouping rule. The TX line is recorded after the send/switch operation; the separate SYS lines report its start and elapsed switch time.

The recognized frame is exactly `ACK 0 Z Y CR LF`: `Z` codes 0–6 select 300, 600, 1200, 2400, 4800, 9600 or 19200 baud; `Y` is 0 (readout) or 1 (programming). Other messages pass through unchanged. This feature changes only baud rate; it does not implement mode E/DLMS framing negotiation or validate the meter's advertised speeds. The baud box shows the active speed while connected and returns to the session's starting speed on disconnect (including connection errors). Closing the app while connected also saves the starting speed, so a session opened at 300 baud starts at 300 again next time.

For manual changes, edit **BAUD** and click **Apply baud (connected)** or press Enter in that field. The port stays open. For a meter's immediate reply, use the automatic ACK option so the change happens without a manual click. Baud changes wait behind pending sends; a stalled transmission fails after five seconds and closes the connection.

## MQTT connections and messaging

Select a saved connection on the **MQTT** tab, or choose **Yeni Ekle**. **Connect** uses the values currently in the connection form; a new connection is saved automatically so its message history can be selected later. **Disconnect** closes or cancels the session. **Kaydet** in the connection form immediately creates or updates the profile. Unsaved edits to existing profiles are retained while switching profiles during the current session; save before closing the app to keep changes. Disconnect before changing connection settings or switching transport tabs.

The MQTT workspace has the connection form at the top, communication in the middle and publish at the bottom. Drag the horizontal dividers to resize these areas. Advanced connection settings are collapsed initially and are available by scrolling the connection form.

- In the left communication column, enter a topic filter and choose **Abone ol**. Select an existing subscription and choose **Kaldır** to unsubscribe. The right column shows incoming messages in soft blue cards on the left and outgoing messages in soft green cards on the right, with topic, timestamp, byte count, QoS and retain metadata. Connection events and errors use compact, separate text rows.
- Each connection has a persistent history under `%LOCALAPPDATA%\CommStudio\mqtt-history`. Messages are saved as they arrive or are published, preserving original payload bytes, timestamps and metadata. Selecting the connection restores its history before connecting, including after restarting the app. Renaming the connection preserves its history. **Clear** clears the selected connection's entire history, including messages hidden by a filter; other connections are unaffected. Individual **Delete** actions also persist.
- Enter text beside the 150-pixel format dropdown and press **Enter** or **Filtrele** to apply a case-insensitive filter. Plain words search topic, raw payload and displayed text; adjacent words mean **AND**. Use **OR**, **NOT**, parentheses and double-quoted phrases to combine conditions. Examples: `serialNumber:12345 AND function:heartbeat`, `NOT serialNumber:12345`, `12345 OR abcde`, `(12345 OR abcde) AND function:heartbeat`. A field condition matches the whole scalar value, so `12345` does not match `123456`. Bare field names search all JSON levels; dotted paths such as `device.serialNumber:12345` target a specific path. `topic:` and `direction:` target MQTT metadata. JSON conditions work in Plain Text, JSON and Hex views. NOT also includes entries where the field is absent. Operator priority is NOT, AND, then OR. Quote literal operators or punctuation, for example `"AND"` or `"mqtt://broker"`. The **?** button shows examples.
- Typing alone does not change the applied filter. An invalid query displays an error beside the field and preserves the previous results. Apply an empty field to show everything again. Filtering only changes visibility; hidden and newly arriving messages are still saved. With the log focused, **Page Up / Page Down** move through a screenful of content and **Home / End** jump to the top/bottom.
- Message payloads are selectable with the mouse. Use **Ctrl+C** or right-click **Copy Selection** to copy only the selection; **Copy Payload** still copies the complete payload. The text fields are read-only, so selecting text never edits the message or saved history.
- The log's top dropdown selects **Plain Text**, **JSON** or **Hex** for all existing and future messages. Its last selection is saved when closing the app and restored on reopening. Changing it also resets individual message formats. Right-click a message for **Copy Payload**, **Copy Topic**, **Delete**, or **Biçim**; the format submenu changes only that message. Copy Payload copies the original UTF-8 text in Plain Text/JSON mode and exact byte values in Hex mode. Delete affects only the local log. **Clear** removes the complete log.
- Payload bytes are preserved when switching views. JSON view adds indentation without changing values; numeric arrays stay inline, and invalid JSON stays visible as text with a short explanation. Hex displays the original bytes, including non-text data. Message cards fit their content up to a maximum of 70% of the log viewport width. Long payload lines have their own horizontal scrollbar inside each card; **Word wrap** optionally wraps them instead and is off initially. The subscription column has a fixed width. New entries follow the bottom when already there and preserve the scroll position when reading older messages.
- In **Publish**, enter the topic and message, then choose **Gönder**. Text is sent as UTF-8, without adding line endings, using QoS 0 and without retain. Subscription requests also use QoS 0.
- **Kaydet** in Publish asks for a name and stores the name, creation/update dates, topic and exact message text. Select a saved entry to fill the publish fields. **Güncelle** updates the selected entry while keeping its identity and creation date. **Sil** removes that saved entry and leaves the current editor text available.
- Reusable messages are saved immediately in `%LOCALAPPDATA%\CommStudio\mqtt-messages.json`; message contents in this file are plaintext.

MQTT 3.1.1 and 5.0 connections support TCP, TLS and WebSocket transports. For WebSockets, Host may include a path; `/mqtt` is used if no path is specified. TLS uses normal Windows certificate validation. Username/password authentication is supported; broker-specific enhanced Authentication Method exchanges are not yet implemented and are rejected with an explicit message. MQTT 5-specific fields apply only when version 5.0 is selected.

After an established connection drops, **Auto reconnect** retries at the saved interval and restores subscriptions. A rejected initial connection returns to the disconnected state. Manual disconnect cancels retries. The UI reports publish errors instead of automatically resending, to avoid unintended duplicate commands.

The form includes general connection settings, advanced MQTT options, editable key/value user properties, and Last Will settings. Advanced sections can be collapsed. Name and Host are required; optional numeric limits can be left blank. Numeric ranges follow the [MQTT 5 specification](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html).

Profiles are stored in `%LOCALAPPDATA%\CommStudio\mqtt-connections.json`. Passwords are encrypted with Windows protection for the current user, so they are not stored as plaintext or portable to another Windows account.

## Command formats

- ASCII mode sends text exactly as entered. Named control-byte tokens such as `[ACK]`, `[CR]`, `[LF]`, `[ESC]`, and `[NUL]` are converted to their byte values.
- ASCII mode also accepts byte tokens such as `[80]` or `[0x80]`.
- Hex mode accepts compact or separated bytes: `06410D`, `06 41 0D`, or `0x06 0x41 0x0D`.
- No line ending is added automatically.

Press **Enter** to send a command and **Shift+Enter** to insert a line break into a command.

## Log display

Use the **Word wrap** checkbox beside **Copy all** and **Clear**, or the matching option in the log's right-click menu, to wrap existing and new messages to the available width. Both controls stay in sync. Turning it off immediately restores unwrapped lines and horizontal scrolling. This only changes the display; message contents and copied text keep their original line endings. The choice is remembered between sessions.

- Hold **Ctrl** and use the mouse wheel over the log.
- Press **Ctrl++** or **Ctrl+-** while the log is focused.
- Press **Ctrl+0** to restore the default 9.5 pt size.
- The same controls are available from the log's right-click **Font size** submenu.

The selected size is remembered between sessions.

Drag the horizontal bar between the communication log and command section to resize the two panels. The chosen split is remembered between sessions.

Use the **Dark mode / Light mode** button in the top connection bar to switch the complete application theme. The selected theme is remembered between sessions.

Dark mode uses neutral grays inspired by Notepad++, with lighter gray log and command fields, light text, and soft blue/green/red log accents.

Light mode uses a shared light gray background for the window and panels, white log and input fields, and subtle separators instead of outer panel frames.

## Build

Run the included PowerShell script:

```powershell
.\build.ps1
```

The resulting executable is written to `dist\CommStudio.exe`.

MQTTnet 4.3.7.1207 and its MIT license are embedded in the executable; no separate DLL is needed to run the app. See `vendor/MQTTnet` for the pinned package, license and provenance. If the existing executable is open, build separately with `./build.ps1 -OutputDirectory ./dist/mqtt-preview`. Set `COMMSTUDIO_TEST_APP` to that executable's absolute path to test it with `./tests/run-tests.ps1`.

To regenerate the Windows icon from `assets\CommStudio-source.png`, run:

```powershell
.\tools\generate-icon.ps1
```
