# REA-Radio-DJ manual

[Nederlands](HANDLEIDING.md) · **English**

REA-Radio-DJ has two parts:

- **The engine** plays the broadcast. It runs as a separate program on the studio PC and keeps playing when the studio
  is closed, restarts or hangs. A small guard starts the engine again by itself after a crash.
- **The studio** is the screen you control the engine with: program log, players, library, mixer, DJ desk and the
  rest. Several studios can work with one engine at the same time, also on another PC.

This manual has two parts:

- **[Part 1 — In the studio](#part-1--in-the-studio)** is for presenters: broadcasting, choosing songs, talking,
  callers, the DJ desk.
- **[Part 2 — Administration](#part-2--administration)** is for the technician and the administrator: installing,
  setting up, planning, hardware and reliability.

> REA-Radio-DJ is in development. Parts not yet tested with real equipment or with the provider are listed in
> [WERKLIJST.md](WERKLIJST.md) and [TESTPLAN.md](TESTPLAN.md) (Dutch).

The studio can be in Dutch, English, German or Spanish: **Settings › Appearance › Language**. The names of buttons and
menus in this manual are the English ones.

---

# Part 1 — In the studio

## 1. Getting started

### Opening the studio

Start **REA-Radio-DJ Studio** from the Start menu. The studio connects to the engine on this PC. If the engine is not
running, choose **Studio › Start engine**.

The first time, the **setup assistant** opens: station name, colour style, music folder and sound cards. It is later
under **Studio › Setup assistant…**.

### Signing in

If the administrator has created users, you sign in with your **name and PIN**: click the name at the top right, or
choose **Studio › Sign in…**. That way the as-run shows who broadcast what.

- **Lock studio** (Ctrl+L): the broadcast carries on, but the studio closes until someone enters a PIN. Handy when you
  step away for a moment.
- **Sign out**: under **Studio**.
- Too many wrong PINs: your name is then blocked for a while. An administrator can lift that at once.

### The screen

At the top are the **workspaces**. Each workspace remembers its own layout.

| Workspace | For |
|---|---|
| **Live** | Presenting: on-air bar, library, decks, program log, cart wall and mixer |
| **DJ** | Mixing by hand on the DJ desk |
| **Automation** | Watching calmly while the log runs by itself |
| **Production** | Library, cue editor, voice tracking, voices and AI presenter |
| **Planning** | Clocks and day planning, ads, news, log import and data sources |
| **Reporting** | As-run, music report, stream, hardware and control |

Working with windows:

- Drag any window by its tab: next to another one, below it, or as a tab behind it.
- **Floating** (menu) opens a window on its own, also on a second screen.
- **Worksheets** (menu) makes an extra studio window, for instance for a second or third screen.
- **Studio › Go to window…**: type part of the name (log, cart, library, dj) and press Enter.
- **Layout** (menu): save, load or reset a layout. **Place window…** puts a window exactly where you want it, also when
  it is lost.

## 2. The on-air bar

The bar at the top always shows what is happening:

- **ON AIR**: what is playing now, with **Intro** (time until the vocals start: that is how long you can talk),
  **Mix over** and **REMAINING**.
- **SAY**: the presenter text for this item (for instance the sponsor line with an ad), large on screen.
- **OVERLAY**: an overlay playing over the music, with a stop button.
- A **level meter** of the programme: orange from −9 dBFS, red from −3.

The buttons:

| Button | Key | What it does |
|---|---|---|
| **Next** | F5 | Starts the next item of the log |
| **Auto / Semi / Manual** | F6 | The play mode (see below); F6 turns automation on or off |
| **Skip** | F7 | Skips the item that is ON AIR |
| **Mic** | F8 | Microphone open or closed; the music goes down while it is open |
| **Stop** | F9 | Hold the button for 1 second to stop automation; F9 stops at once |

### The three play modes

- **AUTO**: the program log runs entirely by itself.
- **SEMI**: the log runs by itself; you step in with jingles, your own songs, the microphone or an AI break. If your own
  song plays on a player, the log waits until it is done.
- **MANUAL**: you start every item, with Next, PLAY or a fader that opens.

## 3. The program log

The program log is what is going to play, from top to bottom. The item ON AIR is at the top.

**Adding something:**

- From the library: **Enter** = as next, **Ctrl+Enter** = at the end. Or drag the song to its place in the log.
- From Explorer (a file on disk): **As next** or **At the end**. Such single files go into the category *Single files*,
  which is not part of the rotation.
- An **AI break**: Ctrl+B, or **Studio › AI break as next**.

**Per line:**

| Button | What it does |
|---|---|
| ⤒ | As next |
| ↑ ↓ | One place up or down |
| ⧉ | Copy right after this |
| ⊞ | Unfold a container: its items become separate lines at this place |
| ⇄ | Edit the transition to this item (see [Transitions](#editing-transitions)) |
| ✕ | Out of the log; for a container the whole block is skipped |

**Menu ⋯:**

- **Plan again** throws away the clock items and plans again. What you added yourself stays.
- **Print log (PDF)…** or **Log as CSV…**: the coming log with the expected start times.

A **container** is a block (for instance an ad break) that is one line in the log.

### Editing transitions

Click ⇄ next to an item, or open the **Transitions** window. At the top you see the outgoing item, below it the next one.

- Drag the lower item sideways to choose where it starts.
- Drag the points of the yellow volume lines; **double-click** adds a point, **right-click** removes it.
- In the middle lane you can lay a jingle or sweeper over the transition.
- **Fade out**: 0 (play to the end), 1, 3 or 6 seconds.
- **▶ Listen** plays the transition on the PFL. **Save** puts it on the item; **Remove own** takes it off again.

An own transition only counts as long as these two items follow each other.

## 4. The library

### Searching

Just start typing. Capitals, accents and small typos do not matter. Press **?** for all the ways to search.

Filters can be combined with words, for example: `is:mag jaar:198 duur:<4:00 queen`

| Filter | Example | Meaning |
|---|---|---|
| `bpm:` | `bpm:120-128` | tempo |
| `jaar:` | `jaar:198` | year (here the eighties) |
| `duur:` | `duur:<3:30` | length |
| `energie:` | `energie:4-5` | your own estimate of the energy |
| `is:mag` | | only what may be scheduled now |

The filter words are Dutch in every language (`jaar` = year, `duur` = length, `energie` = energy, `mag` = allowed).

- **Filters ▾**: category, decade, sorting and quick filters.
- **Save…**: saves the search as a list. A saved list always shows the current state.
- **Fields ▾**: choose which data is shown under the title. **Large rows** is for a touch screen.

### Keys in the list

| Key | What it does |
|---|---|
| Enter | As next in the log |
| Ctrl+Enter | At the end of the log |
| Space | Preview on the PFL |
| Esc | Stop the PFL, or clear the search box |
| Ctrl / Shift | Choose more songs |
| Ctrl+Shift+C | The selection onto the clipboard |
| Ctrl+Z | Undo the last change in the library |

### Right-click on a song

As next or at the end of the log, in standby 1 or 2, PFL, as overlay, **Edit cues**, **On / off** (no longer schedule),
switch music ↔ jingle, and **Make stems** for the DJ decks.

If there is a sign next to a song, point at it: it tells **why this song is not being scheduled now** (for instance the
same artist too recently).

### The detail panel

Click **Details** for the panel next to the list. It has:

- the waveform (click to preview from that point);
- **Needs attention**: for instance too quiet, clipping or a low bitrate;
- the tags (artist, title, album, genre, year, language, mood, rating, energy) with **Save tags**;
- **Presenter text**: what you say with this item. It is shown large in the on-air bar while the item plays;
- the cover (**Choose cover…**, **Remove cover**) and how often and when the song was broadcast.

## 5. Players

### Live decks

In the Live workspace the log plays on deck A and B:

- **⏭ Next**: starts B now and mixes over.
- **Quick out**: fades A out quickly and on to B.
- **■**: stops automation, with a fade.
- **Out of the log**: removes this item from the log.

A deck that is ON AIR has a signal bar. If it stays dim, you see **no sound**.

### Standby players

Players for your own songs next to the log, with a **stack**:

- **Load selection** loads the chosen library song.
- **+ stack** puts the selection behind the current item.
- **Start / pause**, **Stop** (back to the beginning), **Eject**.
- Click in the waveform to seek; scroll to zoom in and out.

Shift+F5 and Shift+F6 start or pause player 1 and 2.

### PFL (preview)

The PFL plays only in the headphones, never on air. Space in the library starts it, Esc stops it. Ctrl+P = play or
pause.

### Cart walls

Two cart walls (Cart wall 1 and 2) of 4 pages with 16 buttons.

- Drag a song or jingle onto a button, or choose **Assign selection from library** with a right-click.
- The right-click menu also has: **Repeat (loop)**, **Overlay on/off** (automation goes down), **Colour**,
  **Cue editor** and **Clear**.
- F10 stops all carts.

### Overlays

An overlay plays on top of automation, and the music goes down while it runs. Choose **Overlay** in the library, or
**Play selection as overlay** in the Overlays window. Shift+F9 stops all overlays.

### Clipboard

A parking place for songs. Drag songs onto it from the library, the log, a deck or a player, or put the selection on it
with Ctrl+Shift+C. Later you drag them back to the log, a player, a deck or the cart wall.

**As container in the log** puts everything on the clipboard as one line in the log, as next or at the end.

## 6. Talking

- **Mic** (F8) opens the microphone. The music then goes down (the administrator sets how much).
- The **Intro** counter in the on-air bar counts down until the vocals start: that is how long you can talk over the
  intro.
- If you want to say something word for word with an item, put it as **presenter text** in the library (detail panel).

## 7. Voice tracking

With voice tracking you record your link ahead, exactly between two songs. Open the **Voice tracking** window
(Production workspace):

1. Choose the **place in the log**: between which two items you talk.
2. Set the **lead-in**: how many seconds of the end of the previous item you hear.
3. **START**: the end of the previous item plays on the PFL and the recording starts.
4. **NEXT**: the next item starts now. You then talk over the intro.
5. **STOP**: the recording is done.
6. **▶ Listen (PFL)**, and then **Put in the log**, **Record again** or **Discard**.

On air your voice begins where you started talking, and the music then goes down. The next item starts at the moment
you pressed NEXT. The two items around the voice track stay in place when the log is planned again. You hear
everything on the PFL; nothing goes to the broadcast.

**From home:** open `https://<engine>:8443/voicetrack/` in the browser (or through 🎙 on the remote page), sign in, and
record with the microphone of your laptop or phone. The recording goes into the log in the same way.

## 8. Callers and reporters

The **Connections** window has two VoIP channels. The other side hears the broadcast, but not themselves.

**Calling someone:**

1. Choose a **station** from the address book at a channel and click **Use**, or type a number and click **Call**.
2. Listen first with **PFL**.
3. Click **On air** when the caller may go on air. In the mixer that is the fader of VoIP 1 or 2.
4. **Hang up** when you are done.

**Being called:**

An incoming call rings on the first free channel. Choose **Answer** or **Decline**.

**Fixed line** (for instance a reporter with a codec or another studio): choose the station with the fixed line and
click **Use**, or fill in port, address and codec and click **Open fixed line**.

**Recording:**

- **REC** records what comes in, as WAV in `Opnamen\Gesprekken`.
- With **Record calls** on, that happens with every call by itself.

A warning at a channel means: no audio, more than 2 % loss, or a buffer that often runs empty.

## 9. The DJ desk

**DJ** workspace: decks with waveforms and beat lines, a mixer and a crossfader.

### On air and back

- **PFL only**: the DJ desk does not go on air and automation is audible. This is how you practise or prepare.
- **DJ ON AIR**: what you mix goes on air. Automation runs along silently.
- **↩ Hand back to automation**: the desk goes off air and automation is audible at once, at the point where it is now.
- **Follow the broadcast on deck A**: deck A plays along with what automation broadcasts, so you can take over
  seamlessly. Deck A must be empty for that.
- **2 decks / 4 decks**: with four decks the decks become more compact.

### A deck

| Part | What it does |
|---|---|
| **Cue** | Playing: back to the cue point and wait. Stopped: the cue point goes to this place |
| **Start** | Start or pause |
| **Sync** | Takes over the tempo of the other playing deck and lines up the beats |
| **− / +** | Brake or push a little to line up the beats |
| **Tempo fader** | Down is faster; the range is 8, 12 or 50 %. **0 %** sets the own tempo back |
| **Key lock** | Faster or slower without changing the pitch |
| **HOT CUES** | Empty: remembers this place. Filled: jumps there. Right-click: clear |
| **LOOP** | **In** and **Out** make a loop; ÷2 and ×2 halve or double it; a number = a loop of that many beats from here |
| **ROLL** | Hold down: a short loop. On release the song carries on as if it had kept playing |
| **EFFECT** | Echo, Flanger or Reverb on the beat; ◀ ▶ for the length, the knob for the mix, **FX ON** |
| **STOP** | **Brake** (run down to a standstill) or **Backspin**; then the deck pauses |
| **Censor** | Hold to mask a word: the song plays backwards while held and then carries on where it would have been |
| **STEMS** | **Vocals**, **Drums**, **Bass** and **Other** on or off separately |

Loading a song: drag it from the library or the clipboard onto a deck.

**Stems:** when you load a song on a deck, the stems are made in the background. That takes about a third of the
length of the song. Next to the buttons it shows how far it is. Then you can, for instance, turn the vocals off for an
instrumental version. It can also be done ahead: in the library, right-click › **Make stems**.

### The mixer

- Per deck: **Gain**, **Filter** (left leaves only the lows, right only the highs), **High**, **Mid** and **Low** with
  **Kill**, the channel fader and **PFL** (the deck in the headphones, also with the fader closed).
- A knob is turned by dragging or with the mouse wheel; double-click sets it to the middle.
- **EQ 0** resets equaliser and filter.
- **CROSSFADER**: left decks A and C, right B and D. **Sharp** keeps both sides open until just before the end.
- **HEADPHONES · CUE ↔ MASTER**: left only the decks with PFL, right only the mix of the desk.
- **Quantize**: hot cues, loops and rolls fall on the beat.
- **Limiter**: the mix never goes above −1 dBFS, also not in the recording.
- **● Record mix**: records what the desk mixes, on and off air. After stopping, the mix is in the library under
  Recordings.

### Automix and sampler

- **Automix (DJ)**: drag songs into the list and turn **AUTOMIX** on. The list plays itself on deck A and B (or all
  four).
  - Transitions: **Beatmix** (tempo and beat matched), **Fade**, **Cut** (on the beat), **Echo** and **Filter**; ◀ ▶
    makes the transition shorter or longer.
  - **Mix now** starts the transition at once.
  - **Channel faders**: automix uses the channel faders instead of the crossfader, so the motor faders of a control
    surface move along.
- **Sampler (DJ)**: a cart wall that plays through the DJ desk. Off air you hear it in the headphones, on air on the
  broadcast. With Quantize a pad starts on the next beat.

## 10. The AI presenter

- **AI break as next** (Ctrl+B) puts a link by the AI presenter in the log. It is written and voiced ahead; the
  broadcast never waits for it.
- With **Approve script first** on, you check every script: **✓ Approve and voice** or **↻ Write again**. If it is not
  approved in time, a jingle takes its place.
- **Messages to read out** (AI presenter window): for instance a birthday greeting, with a validity and a number of
  times.
- **On-air language**: next to the presenter's name you choose the language the presenter speaks on air (Dutch,
  English, German or Spanish). The language model writes in it, the templates and the words around the news and the
  weather follow it, and a Windows voice in that language is picked when the voice profile names none. Choose a voice
  that speaks the language.

## 11. Cutting from the broadcast

The **Cut from the broadcast** window puts a piece of the broadcast recording as an item in the library, for instance
the interview of a moment ago. Choose **Last 5 min**, **10 min**, **30 min** or **Hour**, or fill in a start and end
time. Give a title and click **Cut to the library**. The piece goes with the jingles into the category Recordings and
may run over the whole hour.

This only works when the broadcast is being recorded (Settings › Recording).

### Emergency recording

When something unexpected happens (a caller, a live performance, a reporter calling in), click **● Emergency
recording** in the **Recordings** window. Everything that comes in is recorded, each channel separately: the
microphone, the inputs and the VoIP channels. Click again to stop. What had sound goes into the library (jingles,
category Opnamen); channels that stayed silent are dropped. The command `RECORD TOGGLE` does the same, so a hotkey,
an own button or a hardware button works too. After three hours the emergency recording stops by itself.

## 12. Clock, messages and alarms

- The **Time** window shows the studio clock and counters: ON AIR, INTRO, NEXT, CLOCK and FIXED TIME. With ⚙ you choose
  the clock style, the size and the flash colour at the end of an item.
- The **Messages** window shows notices and alarms. Watch especially for:
  - **Silence on air**: it has been silent too long. Depending on the setting the engine itself starts automation or
    an emergency category. Check whether something is stuck.
  - **Sound card volume changed**: Windows or another program has turned a card down or muted it.
  - **News skipped**: there was no current news, so the whole block was skipped (and no opener plays either).

## 13. Remote

On a phone or tablet in the network, open `http://<engine>:8600/remote/` (or over https on port 8443):

- **Monitor**: what is playing, what is coming, the level meter and the latest notices.
- **Control**: start next, skip, automatic or live assist, microphone, stop carts.
- **Carts**: the cart walls as big buttons.

Watching is always possible. For an action the page asks for your name and PIN, or the control password. The page
follows the language of the browser; `?lang=en` (or `nl`, `de`, `es`) chooses another one.

## 14. Hotkeys

| Key | What it does |
|---|---|
| F5 | Next |
| F6 | Automation on or off |
| F7 | Skip |
| F8 | Microphone open or closed |
| F9 | Stop |
| F10 | Stop all carts |
| Shift+F5 / Shift+F6 | Player 1 / 2 start or pause |
| Shift+F9 | Stop all overlays |
| Ctrl+B | AI break as next |
| Ctrl+P | PFL play or pause |
| Ctrl+L | Lock studio |

The administrator can change these. The overview is in **Studio › Control and hotkeys…**.

---

# Part 2 — Administration

## 1. Installing

### With the setup

Download `REA-Radio-DJ-<version>-setup.exe` from the release on GitHub. .NET does not need to be installed separately.

- The setup installs for the current user, without administrator rights, in `%LocalAppData%\Programs\REA-Radio-DJ`.
- The engine keeps database, music and recordings next to itself, in `engine\data` and `engine\media`.
- A desktop shortcut and starting the engine at sign-in are choices in the setup.
- A newer setup installs over the old one. On uninstalling (Windows Settings › Apps), `engine\data` and
  `engine\media` stay.
- A newer release appears as a button at the bottom right of the studio. Nothing is downloaded by itself.

### The first start

The **setup assistant** prepares the most important things in four steps:

1. the name of the station;
2. the colour style of the studio;
3. the folder with music and the folder with jingles (each subfolder becomes a category);
4. the sound card for the broadcast and the one for the PFL.

Choose a **different card** for the PFL than for the broadcast, otherwise the listener hears your previewing.

### Where things are

| What | Where |
|---|---|
| Database, settings, clocks, planning | `engine\data` (the data folder) |
| Music and jingles (default) | `engine\media\music` and `engine\media\jingles` |
| Broadcast recordings, call recordings | the recordings folder (Settings › Recording) |
| Stems for the DJ decks | `engine\data\stems` (at most 20 GB) |
| Separation model for stems | `engine\data\models\htdemucs.onnx` |
| Basic engine settings | `appsettings.json` next to the engine |

Settings you make in the studio are kept by the engine in its own database. They win over `appsettings.json`.

The engine's icon in the notification area (next to the clock) has a menu in the language of Windows; set
`ReaRadioDj:Language` (`nl`, `en`, `de`, `es`) in `appsettings.json` for another one.

## 2. Keeping the engine running

- **Settings › System › Start the engine with its guard when I sign in on this PC**: after a restart of the PC the
  station then plays without anyone doing anything.
- **Restart engine** (System): the guard starts it again at once; what is ON AIR carries on.
- The guard also restarts the engine after a crash or hang, and it carries on where the log was.
- Under **Latest notices from the guard** you see when that happened.

## 3. Settings

**Studio › Settings…** After changing, click **Save**. **Read again** throws your changes away. With users switched on,
this window is for administrators only.

### Station

- Name of the station, and the folders with music and jingles. Use the path as the engine PC sees it. After changing,
  the library is read again.
- **Play mode when the engine starts**: AUTO, SEMI or MANUAL.
- **Program log under the faders**: music on player A/B, jingles on the cart wall, AI breaks on the AI presenter, each
  with its own fader. Off: everything plays on the automation channel.

### Sound

- **Sound cards** for the broadcast and the PFL, with a test tone. Mind: the test tone for the broadcast also goes on
  air and to the stream.
- **Limiter and loudness on air**: the limiter keeps peaks below the ceiling. The loudness (EBU R128) is shown at the top
  next to the meter.
- **ASIO, sample rate and buffer**: ASIO goes straight to the driver of a studio card. This takes effect after a
  restart of the engine. If ASIO fails, the engine falls back to the Windows card and reports it. Use the sample rate of
  the card and the mixing desk. If the sound stutters, make the buffer larger.
- **Microphone**: the input and the channel (mono when the microphone is on one input of a pair), and how much the music
  goes down under the microphone and under an overlay.
- **Working mode**:
  - *Software*: the engine mixes.
  - *Mixing desk*: each channel with its own output goes at full level to the desk. Fader, mute and ducking under the
    microphone then do not apply, because the desk does that.
- **Output per mixer channel**: give a channel an output of its own (1/2, 3/4, 5/6 or 7/8 of a card with more channels).
  Such a channel is then no longer in the broadcast, the stream and the silence guard.

### Mixing

- **Loudness on air**: −16 LUFS is usual for a stream, −23 for broadcasting.
- **Automatic cue points**: the thresholds with which the engine looks for the start, mix point and end. Cue points you
  set by hand stay.
- **Transitions**: the longest overlap, the fade on Next or Skip, and the music under a voice.
- **Soft start at a fixed time**: a soft news or ad block waits for the end of the item, but no longer than this. Then
  the block starts at once and the item fades out in 2 seconds.
- **Transitions per type and category**: for instance from jingle to song without overlap, or from Classical to
  anything with 6 seconds. The first matching line counts. Keep them with **Save rules**.
- **Play length per type and category**: for instance jingles at most 30 seconds, or ads shorter than 60 seconds always
  play to the end. Keep with **Save play length**.

### Recording

- **Record the broadcast**: one MP3 per clock hour, with the microphone in it.
- **Record the microphone separately**, optionally straight into the library (category Recordings).
- **Record only the talking**: as a listening check. Only the parts with sound while the microphone or an input is
  open, with half a second before the start.
- Quality, retention period and folder. 128 kbit/s is about 58 MB per hour and well over 500 GB per year; 64 kbit/s is
  half. The retention period also applies to the call recordings.
- A single channel can be recorded with **REC** in the mixer.

### Scheduled recordings

**Recordings › Scheduled recordings** window: record an input or an internet stream at fixed times, for instance the
regional news at 12:00 every weekday.

- Name, start time, number of minutes and the days.
- **Source**: a mixer channel (microphone, an input, VoIP) or **Internet stream** with an address (`http://…`). An
  MP3 stream is kept as it comes in; a channel is recorded before the fader, so also when it is not on air.
- **Replace previous**: every new recording gets the same name. The item in the library stays the same, so a clock,
  the planner or the cart wall always has the latest edition ready. Without it every recording gets the date in front.
- **Record now** starts it at once (to try it). If the engine started in the middle of a recording time, it records
  the rest.
- The recording goes to the jingles, category Opnamen. A failed recording is reported in Messages (and by mail if you
  choose). The commands `RECORD START <name>` and `RECORD STOP <name>` work from outside.

### Catch-up and podcast

**Recordings › Catch-up and podcast** window: after each broadcast of a programme the engine cuts it from the
program recording (which must be on).

- Name, from and to (an end before the start runs past midnight), the days and how many episodes are kept.
- Two minutes after the end the episode is ready, with a podcast feed. The engine serves the feed itself at
  `http://<engine>:8600/podcast/<id>/feed.xml`; the address is shown with the programme.
- **Copy folder** and **web address**: also put the episodes and the feed in a folder of your web server (a share,
  for instance). Old episodes disappear there too, but only those of this programme.
- **Publish the last broadcast now** makes the episode of the last broadcast at once (again).
- Mind the rights: music in a podcast or on demand is usually not covered by the licence for broadcasting. Check with
  your collecting societies, or only publish spoken-word programmes.

### Monitoring

- **Silence on air**: threshold, how long, and what happens then: notify, start automation, or play something from an
  **emergency category**. Choose a duration longer than the longest silence in your own programme. The first half
  minute after starting does not count.
- **Sound card volume**: every two seconds the engine checks whether Windows or another program turned a card down or
  muted it. With **Put a changed card back at once** it puts it back itself. If you changed something on purpose, click
  **Current state is normal**. ASIO does not count.

### Messages by mail

**Settings › Messages by mail**: important messages outside the studio too.

- Mail server, port (587 with STARTTLS, or 25), user name and password. The password is stored encrypted and never
  sent back. Port 465 does not work.
- Sender and recipients (separated by a comma).
- **Mail about**: silence on air (and sound again), the backup studio taking over or handing back, a failed backup,
  an ASIO card that drops out, a placeholder without sound, a failed scheduled recording and any other alarm.
- **At most one mail per topic per … minutes**: what happens in between goes along in the next mail.
- **Send test mail** saves first and then sends one mail to the recipients.

### Rotation

How automation chooses songs: the same artist only again after so many songs, the same song only again after so many
songs, and how many items the log plans ahead. The clocks and rules themselves are in the Planner.

### Stream

The Icecast server, the source password, the bitrate, genre and website, and **Start the stream as soon as the engine
starts**. A running stream only takes over new data after you stop and start it again in the Stream window. Extra
servers are set in the Stream window (see [Stream and titles](#11-stream-and-titles)).

### Spotify and AcoustID

- **Spotify** only uses data (wish list, album, year, ISRC, cover), never sound. Make an app in the Spotify dashboard
  (tick Web API) and fill in Client ID and Client Secret.
- **AcoustID** recognises songs by their sound, also when the tags are missing. Register an application at
  acoustid.org and fill in the API key. Only the fingerprint goes to AcoustID, never the sound. This needs `fpcalc`
  (`scripts\fetch-fpcalc.ps1`).

### Remote

- **The engine can be reached on the network**: for a studio on another PC, the browser studio and the remote app.
  This works after a restart of the engine. Then also open the ports in the firewall (see
  [Ports](#ports-and-firewall)).
- **Https** uses the station's own certificate, on port 8443. Install it once on a phone:
  - **Android**: Settings › Security › More › Encryption and credentials › Install certificate › CA certificate.
  - **iPhone**: open the address in Safari, and turn it on under Settings › General › About › Certificate Trust
    Settings.
- **Remote control**: the address and the TCP and OSC port for the command bus, and the **password**. Set a password
  when you listen on the whole network. Without a password and without users, another computer can only watch.
- **Remote controls with their own rights**: a tablet or button box that may only do part, for instance only the cart
  wall. It gets a key of its own or is recognised by its fixed address.

### Appearance and This studio

- **Appearance** applies to this computer only:
  - the **language** of the studio (Dutch, English, German or Spanish);
  - style (Studio dark, Night, Light, High contrast), accent colour, corners, buttons and waveform;
  - **Size of everything** and **Touch mode**, for a touch screen or a screen at a distance;
  - **Calm colours**, and the colours per item type.
- **This studio**: the address of the engine this studio connects to, and whether the studio starts the engine when it
  is not running yet.

The tabs **System**, **Backup studio**, **Central library** and **Users** come further on under
[Reliability](#13-reliability), [Several studios](#14-several-studios) and [Users](#12-users-and-rights).

## 4. Managing the library

### Folders and shares

In the library: **Manage** › **Folders and shares**.

- **Add folder**: a folder on this PC, another disk or a share (`\\server\music`). Choose whether it holds music or
  jingles, and optionally a fixed category. Empty means: each subfolder becomes a category.
- **Watch**: new and changed files are processed at once. Moved files are found back, with their cues and history.
- On import the engine measures BPM and beat grid, key, loudness, the curve and the intro and outro markers, and it
  checks the quality.
- Removing a folder keeps the songs with all their data. If a song comes back later, it is recognised.
- A folder that **cannot be reached** (share gone) keeps its songs; they come back as soon as the folder is there again.

**Replacement locations**: if a file is not at its path (share gone, other drive letter, other PC), the engine looks for
the same file under the replacement folder, for instance `Y:\Music → \\nas\music`. **Convert for good** changes the
paths in the library permanently.

### Categories

Under **Manage › Categories and item**: add a category (music or jingle), move the selection, or turn the selection on
or off. A song that is turned off is no longer scheduled.

### Cue editor

Open the **Cue editor** (right-click › Edit cues, or **Load selection**):

- Drag the markers: cue in, intros, outro, mix, cue out and hook. With ⌖ you put a marker at the cursor.
- Scroll wheel = zoom, Shift+scroll = move, double-click = everything.
- **▶ from cursor** listens on the PFL.
- **Automatic** throws away your own points and determines them again. **Undo** and **Save**.
- **Events in the song**: at a point in the song quieter or louder for a while (**+ Volume at cursor**), or a command
  (**+ Command at cursor**, for instance `CART 1 3`). It goes off every time the song plays on air.

### Tags, covers and filling in data

- In the detail panel: **Save tags**, **Markers in the file** (cues, beat grid and BPM), **All tags in the file**, and
  choosing or removing a cover.
- For a selection: **Tags into the files** writes everything the library knows into the files.
- **Data sources › Metadata**: looks up missing album, year, ISRC, label, genre, composer and cover. Nothing changes
  until you choose **Apply ticked**, and then only empty fields are filled. **Fill in automatically when a song plays for
  the first time** does it in the background.
- **Data sources › Databases**: an external database through ODBC as a source for the library, with a query. **Test the
  query** and **Import now**.

### Stems

For the stems on the DJ decks the model must be in `engine\data\models\htdemucs.onnx`. Stems are made on the processor,
with a few threads at low priority, so the broadcast does not suffer. Never let them run on the graphics card: that once
silenced the broadcast. Stems are kept in `engine\data\stems`, up to 20 GB. After that, the stems unused the longest go
first. A song of 2.5 minutes takes about 110 MB.

## 5. Planning

**Planning** workspace, **Planner** window.

### Clocks

A clock is an hour: a list of slots played from top to bottom. If the hour lasts longer, the clock starts again at the
top. Every new hour starts at the top.

- **New clock**, a name and a colour.
- **+ music**: a slot from a category. **+ jingle**: a jingle category or one fixed item. **+ AI presenter**: an AI
  break.
- A slot can also be a **playlist** (M3U, PLS or XSPF), also when another program writes that list. Only files from the
  library play; **Check** shows how many those are.

### Week grid

**Paint with clock**: choose a clock and click the hours. Right-click fills a whole row or column; **Whole week** fills
everything. After changing clocks or the grid: **Plan log again**.

The **Day** and **Week** tabs show what is planned, with today's fixed times and streams.

### Fixed times

Something at a fixed time, for instance a tune on the full hour:

- Hours (`*` = every hour, or for instance `7-9,12,17-19`), minute and second.
- What: a category or the selection from the library.
- **Hard**: interrupts to the second. **Soft**: after the current item. **Overlay**: on top, the music goes down.

Material (also an AI break) is prepared 3 minutes ahead. If it is too late, it is skipped and never played later.

### Rules

For instance: at most 2 in a row from the same category, or Hard rock may not follow Classical directly. If a rule
stops an item, that slot is **skipped**, or **postponed** (tried once more after the next item). A new hour starts
clean.

### External streams

An Icecast or SHOUTcast address that is watched in a time window. When the stream is live, it goes ON AIR and takes over
the programme. When the stream stops or the window ends, the programme carries on. **Stop live stream** takes a running
stream off at once.

### Log import

**Log import** window, for a log from an external music planner:

1. Under **Profiles**, make a profile: where each field is (column, or from which character and how long), the time
   format, and if needed a path replacement when the planner sees the music in another place.
2. Under **Import**: choose profile, date and log file, then **Check** and **Import**.
3. Under **Hours** you see which hours play the imported log instead of their clock. If an hour runs out, the clock
   fills it up.

**Export library…** gives the planner a CSV with all songs and jingles.

## 6. Ads

**Ads** window:

- **Campaigns**: advertiser, campaign name, from and to, hours, number per day and at most per hour, and the spots
  (they take turns).
- **Breaks**: an ad break at a fixed minute, with opener, closer and a longest length. **Test: what goes in?** shows it.
- The breaks are filled 3 minutes ahead:
  - first the campaigns furthest behind on their number per day;
  - never two spots of the same advertiser in one break;
  - never longer than the break may last.

  If no campaign needs a spot, the whole break drops out, opener and closer too.
- **Proof of broadcast**: CSV per broadcast, an overview per advertiser and campaign, or JSON for linking.

**Placeholders** (**Placeholders** window): plan a spot while the sound is still to come. Give the advertiser,
title, length, when the sound must be there at the latest and optionally the campaign. The placeholder is an item in
the library and can also go in a clock or the cart wall. As long as the sound is not there it is skipped; an ad break
reports that. A day before you get a warning, and an alarm if it is not there in time. The sound comes in with
**Choose sound…**, or by putting the file under the name shown in the placeholder folder. From then on it is an
ordinary item, everywhere it was already planned.

An ad break is best set to **Soft**: it then comes after the current song.

## 7. News and weather

**News** window. A news block has hours, a minute, an opener and a closer, and one of these sources:

- **A file** that comes into a folder (for instance from a news service), at most so many minutes old.
- **RSS headlines**, read out as they are. Nothing is made up.
- **AI news**: the language model rewrites national and regional sources into a bulletin with sources. If it mentions
  something that is not in the source (for instance a number), the headlines are read out as they are.
- **Weather report** for a chosen place.

Choose a voice and optionally a **bed underneath**. With **Trial: write text** and **Trial with voice (PFL)** you test
a block without broadcasting. The examples *Short regional news (:30)*, *News on the full hour* and *Weather report*
are a good start.

The news and the weather speak the AI presenter's on-air language (see below); the words around them ("The news at…",
"That was the news.") follow it too.

A block is prepared 3 minutes ahead. If there is no current news, the whole block is skipped with a notice in Messages.

## 8. The AI presenter and voices

### Language model and persona

**AI presenter** window, under **Settings: language model and persona**:

- **AI presenter on**, a name, the **on-air language** next to it, and the language model. Use **Ollama on this PC** or
  another OpenAI-compatible address. Empty means: fixed template texts.
- Length, variation, persona, and a **music bed** (a category with beds under the voice).
- **Write trial script**: the language model writes a text. Nothing is voiced or broadcast.
- **Weather, news, messages and outro**: per break the presenter mentions at most one thing. An **outro** jingle can
  come right after each break.
- **Personas per daypart**: per daypart a presenter of its own with name, character, voice, length and music bed.
- **Creative presenter**: links the records together and now and then touches on a subject. You set the tone (serious,
  funny, nice, edgy), and where the subjects come from: your own subjects, online subjects from the station, or built-in
  subjects. Nothing hurtful, no politics and no made-up news.

The instructions to the language model are Dutch; with another on-air language they say to write in that language and
give examples in it. The persona text can stay Dutch.

### Voice server (Higgs)

Higgs TTS runs as a separate server in WSL with an NVIDIA graphics card. Under **Voice server (Higgs)** are the address
and the start command, and **Start voice server automatically with the engine**. Loading the model takes a few minutes;
until then the backup voice speaks. The start command can only be changed on the studio PC itself. The installation is
in the README.

> Mind: preferably do not run the voice server on the same PC as the engine if that graphics card also does the display
> or other tasks. Heavy work on the graphics card can make the PC stutter.

### Voices

**Voices** window:

- **VOICES PER ROLE**: which voice the AI presenter, the newsreader and the weather use.
- **Add standard voices**: five Higgs voices with a character of their own, plus the Dutch Windows voice as a backup.
- **+ Higgs voice**, **+ Windows voice** or **+ Online voice** (ElevenLabs or Azure; they cost money per character and
  send the text to the service). Each voice can have a **backup voice**.
- **Clone voice**: a short clean recording (4 to 40 seconds) with the exact text. Without recorded written consent a
  cloned voice is not used.
- **My own voice**: a presenter records their own voice, chooses for which roles it may be used and gives consent.
  **Remove my voice** withdraws that again.
- **Online voices and check (Whisper)**: listen back to every break and compare it with the script. Below a minimum
  score it is voiced again, and after failed attempts a jingle may take its place. Whisper listens for the on-air
  language unless another one is set.

## 9. Mixer and routing

**Mixer and routing** window. Each channel has a fader, **M** (mute), **REC** (record to WAV), **AIR** (to the
broadcast) and ⚙ for the **channel strip**:

- gain, balance, equaliser (low, mid, high) and compressor;
- automatic level control;
- an effect;
- VST effects (VST 2, 64-bit). Use **Search plug-ins** first.

**Inputs** (line in, a second microphone, a test tone) come into the mixer as a channel and start closed. With **AIR**
you put them through.

The **Outputs** window shows the programme large: peak, loudness (short and integrated), true peak and the limiter. Per
channel there is a meter with OPEN, CLOSED or MIC OPEN, readable from the other side of the studio.

## 10. Setting up connections (VoIP)

### SIP account

In the Connections window, **SIP account**:

- User and number, login name and password. The password is stored encrypted and never sent back.
- Registrar, domain, proxy, outbound proxy and STUN server.
- **Codecs** in order of preference. The first one the other side also knows is used:

  | Codec | Quality | Use |
  |---|---|---|
  | `opus` | 48 kHz, studio quality over the internet | LUCI Live, Comrex and other codecs |
  | `g722` | 16 kHz wideband | HD telephony |
  | `pcma` / `pcmu` | G.711, 8 kHz | ordinary phone; `pcma` is common in Europe |
  | `l16` | uncompressed, 48 kHz | only if the other side can do it; well over 0.8 Mbit/s |

- **Incoming on**: *auto* (the first free channel) or *none* (decline incoming calls).
- **Register** every so many seconds, and **Keep-alive** to keep the NAT router open.

### Address book and signals

- **Address book**: fixed stations. *sip* = a number to call; *rtp* = a fixed line with its own port, address and port
  of the other side, codec and buffer.
- **Signals**: commands when a channel rings, is connected or goes quiet again, for instance a light. In the command
  `{kanaal}` becomes voip1 or voip2, and `{nr}` 1 or 2.
- **Record calls**: record every call automatically as soon as sound comes in.
- The command bus knows `VOIP 1 DIAL <number>`, `VOIP 1 STATION <name>`, `ANSWER`, `HANGUP`, `ONAIR`, `PFL` and
  `RECORD`.

## 11. Stream and titles

**Stream** window:

- **Start stream** and **Stop stream**.
- **Extra stream servers**, each with its own MP3 stream and quality:
  - Icecast: `host:port/mount`;
  - SHOUTcast v1: `host:port`;
  - SHOUTcast v2: `host:port,sid`.
- **Title**: in a format of your own, with `{artiest}`, `{titel}`, `{categorie}`, `{type}`, `{bron}`, `{duur}`,
  `{tijd}`, `{datum}` and `{station}` (artist, title, category, type, source, duration, time, date, station). The title
  goes to the stream, to a text file (for the website or RDS) and to a web address. Only the log changes the title; the
  cart wall and overlays do not.
- **Program log as text**: one file per day in a folder, with a retention period.

## 12. Users and rights

**Settings › Users**:

- **New user**: name, role, PIN (4 to 12 digits) and optionally a **last day** (for an intern or stand-in). The roles
  are presenter, technician and administrator. With users switched on, the settings are for administrators only.
- Someone who stops is better disabled (**May sign in** off) than deleted. Their name then stays with their broadcasts.
- **Block after wrong PINs**: after so many attempts that name cannot sign in for a while. **Lift** undoes a block at
  once.
- There is always an administrator without an end date.
- **PIN lost and no administrator left**: start the engine on that PC with `--users-off`. Until the next start nothing
  is checked then.

## 13. Reliability

### Backup

**Settings › System › Backup**: one file with all settings, clocks, planning, events, cart walls, hardware, ads, news,
presenters and the cue points of the library. The audio files are not in it; keep those separately.

- **Make backup…** and **Restore backup…**. Restoring replaces everything; what was there is kept in a folder in the
  data folder.
- **Make a backup automatically every day**: at a fixed time, into a folder (preferably another disk or a NAS), with a
  retention period. If the PC was off, the backup follows as soon as the engine runs again.

### The database on an upgrade

When a new version starts for the first time, the engine checks the database before it updates it, and makes a copy
in `data\Database voor upgrade` (the last three are kept). Messages then says it worked. If the database is damaged,
the engine does not touch it and does not start: a window says what is wrong, and the same text is in
`data\DATABASE-PROBLEEM.txt`. Keep the previous version running or restore a backup.

You can also check beforehand: `REA-Radio-DJ.exe --check-db` in the engine's folder. To update a damaged database
anyway, start once with `--db-upgrade-anyway` (at your own risk; a copy is made first).

### Backup studio

**Settings › Backup studio**: two engines that keep an eye on each other, each with its own sound card to the
transmitter or the stream.

- Set one as **main** and the other as **backup**, each with the address of the other engine. Both must be reachable on
  the network (Remote › Engine on the network).
- Every few seconds the backup asks the main studio how it is doing. If it does not answer for too long (or it is
  silent there, if you choose so), the backup starts its own automation. Optionally it also starts the stream.
- When the main studio plays again, the backup hands the broadcast back: by itself after the chosen time, or with
  **Hand back to the main studio**. **Take over now** starts the backup at once.
- While handing back, both play for a moment; let the mixing desk or the stream server pick the right one.
- **Commands on taking over and handing back**: for instance a notice or a light.
- If the backup uses http while the main studio is not on the network, the backup reports that.

### Ports and firewall

**Settings › System › Ports** shows which ports the engine uses: in or out, this PC only or the network. The engine
itself opens nothing in the firewall. Below it is a PowerShell command that opens only the ports needed; run it as
administrator.

| Port | For |
|---|---|
| 8600 | the engine (http): studio, web and remote app |
| 8443 | the engine (https) |
| 8601 | command bus over TCP |
| 8602 | command bus over OSC (UDP) |
| 8603 | command bus over UDP |
| 5060 | SIP (UDP) |
| 5004 and 5006 | RTP of VoIP 1 and 2 (default) |

### System log

**Settings › System › System log of the engine**: errors and warnings of the engine itself. Only turn on **Detailed
logging** when you are looking for a problem.

## 14. Several studios

### Central library

**Settings › Central library**:

- One engine is the **source**. It imports and measures music and jingles: cues, loudness, waveform, BPM and key. Give
  the source a control password and turn on **Engine on the network**.
- The other engines are **studio**. They take over the library with everything measured, through the address and the
  control password of the source.
- If a studio can reach the file at the source's location (a share), it plays it from there. Otherwise it first fetches
  it to its own PC. With **Fetch all files to this PC** a studio keeps playing also when the network fails.
- How often a song was played stays per studio. Own folders work as usual next to it.

### A studio on another PC

Turn on **Engine on the network** on the engine, and in the studio on the other PC set the address of the engine under
**Settings › This studio**. If there are no users, give the studio the control password through the environment
variable `REA_ENGINE_KEY`.

## 15. Control and hardware

### The command bus

Everything you can do in the studio can also be done with a text command: `NEXT`, `AUTO OFF`, `CART 1 5`,
`MIC TOGGLE`, `VOLUME log 80`, `STREAM START`, `DJ A STEM VOCALS OFF`… The full overview is in **Studio › Control and
hotkeys…** (Commands tab, with **Run** to try).

The same commands work over:

- **HTTP**: `POST /api/command` with `{"text":"NEXT"}`, or a web address per button: `/api/do/next`.
- **TCP** (8601): one command per line; with a password first `AUTH <password>`.
- **OSC** (8602): for instance `/rea/next` or `/rea/cart/1/5`.
- **UDP** (8603): one command per packet.
- **`rea-cmd`** next to the engine, for instance in a task scheduler.
- **Fixed times** in the planner, with the action *Run command*.

### Hotkeys and events

In **Control and hotkeys**:

- **Hotkeys**: a key (F5, Ctrl+B, Ctrl+Alt+1) to a command. With **everywhere** it also works when another program is
  in front. Then choose a combination other programs do not use.
- **Events**: on an event (an item starts, the microphone opens, the stream drops) the engine runs a command. In it
  `{artiest}`, `{titel}`, `{type}`, `{categorie}` and `{station}` are filled in. For instance: microphone open →
  `OSC 192.168.1.20:8000 /onair 1`.
- **Own buttons** (Own buttons window): buttons on the screen, each with a name, colour and command.

### Hardware

**Hardware** window:

- **Devices**: MIDI, serial ports, Livewire GPIO and Ember+. The engine keeps every active device connected and tries
  again every 5 seconds when the connection drops.
- **Buttons**: a signal from a device (a note, controller, GPI, Ember+ parameter or line of text) to a command on on and
  on off. Press a button on the device and click it under **Latest signals** to take it over.
- **Lights**: a state (microphone open, ON AIR, VoIP ringing…) to an output: a MIDI note, a Livewire GPO, an Ember+
  parameter or text on a serial port. Try it with **Test on** and **Test off**.
- **Mackie Control** (for instance the iCON V1-M, with an extension such as the V1-X):
  - eight strips follow the mixer channels;
  - the fader is the volume, with a motor that follows the studio;
  - the knob is the balance;
  - MUTE mutes, REC records, and SELECT puts the channel to air;
  - the display shows name and position, and the meters the level.

  In DJ mode the surface switches to the DJ layout by itself. The **transport buttons** are off by default, so a button
  pressed by accident does not stop the broadcast.
- **iCON V1-M: touch buttons**: **Write layout to the panel** puts REA's own layout into the surface, with the name of
  what each button does. Close iMap first.

### DJ controller

**DJ controller** window: choose the MIDI port and click **Add controller**. At a function, press **Learn** and move the
knob, fader or jog wheel. **Reverse** turns the direction around. **Keep** makes it a preset for the same controller in
another place.

## 16. Reporting

- **As-run**: what was really broadcast, with who was signed in. **Export CSV…**.
- **Music report (Buma/Stemra · Sena)**: every broadcast of a song, with date, time, duration, performer, title,
  composer, label and ISRC. Also as an **overview per song**, as CSV or PDF.
- **Broadcast statistics**: the share of music in airtime, songs per hour, the most played songs and artists, the
  rotation per category, and songs not played for a long time.
- **Proof of broadcast** for ads: in the Ads window.

## 17. Troubleshooting

| What you see | What you do |
|---|---|
| The studio says the engine cannot be reached | **Studio › Start engine**. If it runs on another PC, check the address under Settings › This studio |
| No sound comes out | Check under Settings › Sound which card the broadcast uses, and try the **Test tone**. Check in Messages whether Windows turned the card down |
| The listener hears the PFL too | The PFL and the broadcast are on the same card. Choose another card for the PFL |
| Silence alarm in Messages | Check what is ON AIR and whether the log is running (AUTO). If needed, start with Next (F5) |
| The sound stutters | Make the buffer larger (Settings › Sound). Check whether something heavy is running on the PC |
| A share is gone | The songs are kept and come back as soon as the share is there again. If needed, use a replacement location |
| A song is never scheduled | Point at the sign in the library: it says why. Check whether the song is on |
| The news was skipped | There was no current news (file too old, no headlines). Check in Messages which source it was |
| An AI break does not come | Check whether the AI presenter is on and the voice server is running (AI presenter window). If a voice fails, the backup voice speaks |
| The AI presenter speaks the wrong language | Check the on-air language next to the presenter's name, and choose a voice that speaks that language |
| Stems: "the separation model is missing" | Put `htdemucs.onnx` in `engine\data\models` and restart the engine |
| A caller is hard to understand | Look at the measurements at the channel (loss, buffer). Make the buffer larger, or choose another codec |
| PIN lost and no administrator left | Start the engine with `--users-off` and make a new PIN |
| The engine does not start after an upgrade | Read `data\DATABASE-PROBLEEM.txt`. Keep the previous version running or restore a backup |
| No mail arrives | **Send test mail** in Settings › Messages by mail; also look in the junk folder. Port 465 does not work: use 587 |
| A scheduled recording is empty | Check that the source gives sound (the channel in the mixer, or the stream address in a browser) |
| A window is lost | **Layout › Place window…**, or **Restore default layout** |
