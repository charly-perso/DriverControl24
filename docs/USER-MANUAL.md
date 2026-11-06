# User manual — DriverControl24 for REAPER

🇬🇧 English · 🇫🇷 [Version française](MANUEL-UTILISATEUR.md)

This manual describes **everything the driver does with the Digidesign Control|24 surface in REAPER**: which button, fader or encoder controls what.

It complements the original *Control 24 Surface Guide* (written for Pro Tools): button names are the ones printed on the console. **Only the functions described here are handled by the driver**; a button that is not mentioned has no action in REAPER.

> Installation, `.ini` file configuration and debugging: see the [README](https://github.com/charly-perso/DriverControl24/blob/master/README.md).

---

## Contents

1. [Basics](#1-basics)
2. [Home view: faders, encoders, mute, solo, select](#2-home-view-faders-encoders-mute-solo-select)
3. [Moving between tracks (banks)](#3-moving-between-tracks-banks)
4. [Recording and monitoring](#4-recording-and-monitoring)
5. [Transport](#5-transport)
6. [Sends](#6-sends)
7. [The Pan/Send view of a track](#7-the-pansend-view-of-a-track)
8. [Inserts and plugins](#8-inserts-and-plugins)
9. [Plugin parameters](#9-plugin-parameters)
10. [Inputs, outputs and Master](#10-inputs-outputs-and-master)
11. [Automation](#11-automation)
12. [Jog wheel, navigation and zoom](#12-jog-wheel-navigation-and-zoom)
13. [Edit tools](#13-edit-tools)
14. [Numeric keypad, markers and regions](#14-numeric-keypad-markers-and-regions)
15. [Groups, windows, undo and save](#15-groups-windows-undo-and-save)
16. [Displays, counter and meters](#16-displays-counter-and-meters)
17. [Driver configuration](#17-driver-configuration)
18. [Troubleshooting](#18-troubleshooting)
19. [Quick reference of REAPER actions used](#19-quick-reference-of-reaper-actions-used)

---

## 1. Basics

### 1.1 How it works

The driver connects the Control|24 (over Ethernet) to REAPER:

- the console's **faders**, **encoders** and **buttons** control REAPER;
- the console's **LEDs**, **displays** (track names, values), **meters** and **counter** reflect REAPER's state, including when you act with the mouse.

The driver starts automatically with REAPER (`Autostart` setting in the `.ini` file).

### 1.2 Modifier keys

Four buttons in the "PROTOOLS SHORTCUTS" section act as modifiers. This manual calls them:

| In this manual | Button on the console |
|---|---|
| **SHIFT** | SHIFT / ADD |
| **OPT** | OPT (ALT) / ALL |
| **CTL** | CTL / CLUTCH |
| **CTRL (⌘)** | the "(CTRL)" ⌘ button |

A modifier is **held down** during the action (button + modifier); it does not "latch".

### 1.3 Terms

- **Strip**: one of the 24 channels (fader, encoder, 4-character display, SELECT / SOLO / MUTE / REC ARM buttons…).
- **Encoder (vpot)**: the rotary knob under each display; it also has a push switch (SWITCH ACTIVE).
- **Bank / page**: a group of 24 tracks shown on the console.
- **View**: what the 24 strips represent at a given moment (tracks, sends, inserts, plugin parameters…).
- **Steady / blinking LED**: the state is "on" (steady) or "partial / pending" (blinking), depending on the function.
- **Temporary message**: some actions show a short text on the displays; the console then returns to its normal display.

### 1.4 Getting back to normal

- **ESC/CANCEL** (main section, above SCRUB/SHUTTLE) sends the **Escape** key to REAPER.
- In the Sends, Inserts and Inputs/Outputs views, the **ASSIGN** button and the **ESC/CANCEL buttons of the Assignment section** confirm or cancel (see §6, §8, §10).

---

## 2. Home view: faders, encoders, mute, solo, select

The **Home** view is the start-up view: the 24 strips show 24 consecutive REAPER tracks.

| Item | Action |
|---|---|
| **Fader** (motorized) | Track volume, in dB. Also follows changes made in REAPER. |
| **Strip display** | Track name. |
| **Encoder** | Track pan. A ring of 15 LEDs shows the position. |
| **MUTE** | Mutes / unmutes the track. If the track is in Touch/Write/Latch automation, the mute is written to the mute envelope. |
| **SOLO** | Solo in place of the track (additive). |
| **SELECT** | Selects / deselects the track. **DEFAULT + SELECT** resets this track's fader to **0 dB**. |
| **AUTO** | The track's automation mode (see §11). |
| **Meter** | Track level (see §16). |

### FLIP

**FLIP** swaps the roles of faders and encoders: faders control pan and encoders control volume. The button blinks while FLIP is active; press **FLIP** or **ESC/CANCEL (Assignment)** to leave it. FLIP also exists in the Sends and Plugin parameters views (see below).

### SOLO SAFE and SOLO CLEAR

- **SOLO CLEAR**: clears all solos in the project (tracks in "solo safe" keep their protection). The LED blinks as long as at least one track is soloed.
- **SOLO SAFE**: blinking button = "solo safe" mode is active. Press **SOLO** on one or more tracks to protect / unprotect them (a "solo safe" track is not cut when other tracks are soloed). Press **SOLO SAFE** again to return to normal solo.

---

## 3. Moving between tracks (banks)

| Button | Action |
|---|---|
| **BANK LEFT / BANK RIGHT** | Previous / next page: the previous (or next) 24 tracks of the project. Active in the Home, Sends and Plugin parameters views. |
| **NUDGE** (held) | Puts the jog wheel in "track selection" mode: turn the wheel to select the previous/next track in REAPER. **On release**, the console jumps to the page containing the first selected track. |
| **F1** | LED on: REAPER's mixer follows the banks (it scrolls to the first displayed track). Saved in the project. |
| **F2** | LED on: at each bank change, the first track of the bank is selected and the Arrange view shows it. Saved in the project. |

---

## 4. Recording and monitoring

| Button | Action |
|---|---|
| **REC ARM** (per strip) | Arms / disarms the track for recording. **Blinking** LED: armed; **steady** LED: armed and REAPER is recording. |
| **MASTER REC** | Blinks when at least one track is armed. If blinking: **disarms all tracks**. Otherwise: **arms all tracks** that have a recording input set (tracks without an input are skipped). |
| **REC SAFE** | Enables "record safe" mode: the strips' **REC ARM** buttons become protection switches (the track is set to the "disabled" recording mode in REAPER; the LED stays on). Press **REC SAFE** again to return to normal mode. |
| **MONITOR** | Cycles through three input-monitoring states for the whole project (state saved in the project): <br>• LED off: monitoring disabled (armed tracks are temporarily disarmed); <br>• LED steady: input monitoring on; <br>• LED blinking: "automatic" monitoring (tape-machine style). <br>When going back to steady/blinking, the previously armed tracks are re-armed. |

### Quick Punch (punch-in recording)

Three recording modes, shown by the **QUICK PUNCH** LED:

| QUICK PUNCH LED | Mode |
|---|---|
| Off | Normal recording |
| Steady | Auto-punch within the **time selection** |
| Blinking | Auto-punch within the **selected items** |

- **QUICK PUNCH**: switches between normal recording and auto-punch.
- **CAPTURE**: when auto-punch is active, switches between "selection" and "items".

The LED also follows changes made in REAPER.

---

## 5. Transport

### 5.1 Play, record, stop

| Button | Action |
|---|---|
| **PLAY** | Play; pressing during playback **pauses**, pressing while paused resumes. |
| **REC** then **PLAY** | Press **REC** and **PLAY** together to start recording. Pressing PLAY during recording pauses it. |
| **STOP** | Stop. |
| **FWD / REW** (held) | Fast forward / rewind while the button is held. |
| **GO TO START / GO TO END** | Cursor to the start / end of the project. |
| **IN / OUT** | Sets the start / end of the **time selection** at the cursor position. The LEDs show whether a start / end exists. |
| **LOOP PLAY** | Turns loop playback on / off. |
| **LOOP REC** | Remembers that recording should loop: when you start recording, the loop is enabled; it is restored when you stop. |

The PLAY / STOP / REC LEDs follow REAPER's real state (PLAY blinks when paused).

### 5.2 FWD / REW with modifiers: markers and regions

These actions run **on release** of FWD or REW:

| Combination | Action |
|---|---|
| **OPT + FWD / REW** | Cursor to the next / previous **marker**. |
| **OPT + CTL + FWD / REW** | Sets the selection start at the cursor, goes to the next/previous marker and sets the selection end there: you select **the interval between two markers**. |
| **CTRL (⌘) + FWD / REW** | Cursor to the **start of the next / previous region**. |
| **CTRL (⌘) + SHIFT + FWD / REW** | Cursor to the **end** of the next / previous region. |
| **CTRL (⌘) + CTL + FWD / REW** | Goes to the next / previous region **and selects it** (time selection = the region). The selection is kept when you release CTL. |

### 5.3 Pre-roll and post-roll

**PRE ROLL** and **POST ROLL** add a lead-in before and a tail after the time selection.

| Action | Result |
|---|---|
| **PRE ROLL** / **POST ROLL** | Turns the function on / off (LED on = active). |
| **SHIFT + PRE ROLL** (or POST ROLL) | **Set the duration**: the button blinks, a selection of the pre-roll (or post-roll) duration appears and the jog wheel changes it. Press the button again to **confirm**: the duration is saved in the project (2 s by default) and the function turns on. |

With pre-roll/post-roll active and an existing **time selection**, **PLAY** starts `pre-roll duration` before the selection start and playback stops `post-roll duration` after its end.

### 5.4 EXT TRANS — generate plugin parameters

**EXT TRANS** writes the FX parameter sections into `ReaperDriverControl24.ini` (message "Writing FxParams sections…"). Use it to **add** the parameters of new plugins; parameters already in the file are kept. See §9 and §17.

---

## 6. Sends

The 5 buttons **SEND A/F, B/G, C/H, D/I, E/J** show a given send for **all tracks** at once.

| Action | Result |
|---|---|
| **SEND A/F … E/J** | Shows send **A … E** on all tracks (Sends view). Pressing again returns to the Home view. |
| **SHIFT + SEND …** | Shows send **F … J** (second page of 5 sends). |
| **CTRL (⌘) + SEND …** | Resets the order of send A … E on all tracks. **CTRL + SHIFT + SEND …**: sends F … J. (Message "Send X are reset for all tracks.") |

In the Sends view:

- **Encoder**: send level; **fader**: track volume.
- **FLIP**: faders control the send level, encoders its pan.
- **PRE/POST**: switches the send to pre- or post-fader ("Pre" / "Post" shown).
- **SEND MUTE**: send-mute mode (buttons act on the send's mute). In FLIP mode, each strip's **MUTE** button mutes the send.
- **ASSIGN**: changes the sends' destination (see below).

### Assigning a send (ASSIGN)

1. Press **ASSIGN** (the button and the Assignment section's ESC/CANCEL buttons blink).
2. Turn a strip's encoder to browse the available destinations.
3. Confirm with **ASSIGN**; **ESC/CANCEL (Assignment)** cancels without changing anything.

---

## 7. The Pan/Send view of a track

A strip's **PAN/SEND** button opens the detailed view of **that track**: the strips show the pan ("L<>R"), then, for each send: send name, destination name, level and pre/post state. Sends are shown in **pages of 5** (A–E then F–J); the **CHANNEL BAR SCROLL** buttons (arrows on either side of the channel bar) change page. Press **PAN/SEND** again to return.

Same principles as §6 for level, mute and send assignment.

---

## 8. Inserts and plugins

### 8.1 EQ, DYN and INSERTS

Each strip has three buttons: **EQ**, **DYN** and **INSERTS**. Their LEDs show whether the track has plugins of that type.

| Button | Action |
|---|---|
| **EQ** | Opens the parameters of the track's **first equalizer plugin** (press again: next EQ plugin). |
| **DYN** | Same for **dynamics** plugins. |
| **INSERTS** | Opens the track's **Inserts view** (§8.2). |
| **EDIT/BYP** (global) | LED on: **EQ** and **DYN** become **bypass** buttons (all plugins of that type on the track; steady LED = all bypassed, blinking = partial bypass). |
| **INS/BYP** (global) | Same for **INSERTS** (all of the track's plugins). |

Which plugins count as "EQ" or "DYN" is defined in the configuration file (`[EQ]` and `[DYN]` sections, §17).

### 8.2 Inserts view

A track's Inserts view shows **8 plugin slots** per page (**2 pages**, i.e. 16 plugins), each using 3 strips. Use the **CHANNEL BAR SCROLL** buttons to change page.

- Each plugin's short name (8 characters, editable in the configuration app) is shown; **lowercase: active, UPPERCASE: bypassed**; "NoInsert": empty slot.
- **First button of the block**: selects the plugin; **pressing again** on the already selected plugin opens its **parameters** (§9).
- **Second button of the block**: bypass of the selected plugin ("Byp: On" / "Byp: Off").
- **MASTER BYPASS**: enables / bypasses **all** the track's plugins.

#### Adding, replacing or removing a plugin (ASSIGN)

1. Press **ASSIGN**.
2. Turn a slot's encoder to browse the plugin list ("NoInsert" + plugins marked *visible* in the configuration + plugins already present); press the encoder button to choose.
3. Press **ASSIGN** again: the changes are **applied in REAPER** (plugins removed / added). **ESC/CANCEL (Assignment)**: cancels.

### 8.3 Copy / paste settings

- **COPY SETTINGS FROM CHANNEL**: copies the selected plugin's parameters (the LED blinks).
- **PASTE SETTING TO CHANNEL**: pastes into a selected **identical plugin**. Otherwise: message "Not same Fx choose to paste settings !".

---

## 9. Plugin parameters

When a plugin is open (from EQ, DYN or INSERTS), the 24 strips show its **parameters**, in pages of 24. The plugin window opens in REAPER; **PLUG-IN** (WINDOW section) hides / shows it.

| Item | Action |
|---|---|
| **Encoder** | Parameter value. "Switch" parameters show On / Off and use the SWITCH ACTIVE button. |
| **FLIP** | Parameters move to the **faders**. |
| **CHANNEL BAR SCROLL** | Previous / next parameter page (the arrow blinks if another page exists). |
| **BANK LEFT / RIGHT** | Move between tracks. |
| **MASTER BYPASS** | Bypasses / enables the open plugin. |
| **COMPARE** | Compares two sets of settings **A / B** of the plugin (steady LED = A, blinking = B). |
| **AUTO/SELECT** | LED on: the encoder buttons are used to **choose which parameters to automate** (see §11). |
| **INSERTS/PARAM** (blinking) | Returns to the Inserts view. |

### Choosing the displayed parameters

Order, short names (4 characters), long names (8 characters, over 2 strips), text separators and the visibility of each parameter are set in the **configuration app** (§17). To **add** the parameters of a new plugin: add it in REAPER, then press **EXT TRANS** (§5.4).

---

## 10. Inputs, outputs and Master

From the Home view, three buttons change what the strips represent:

| Button | View |
|---|---|
| **INPUT** | Shows each track's **recording input**. |
| **OUTPUT** | Shows each track's **outputs**. |
| **MASTER FADERS** | Shows the **Master** track. |

In the INPUT and OUTPUT views, **ASSIGN** lets you change the routing with the strip's encoder (same principle as §6: turn to choose, encoder button to select, **ASSIGN** to confirm, **ESC/CANCEL (Assignment)** to cancel). For outputs, several destinations can be added or removed. Press the view's button again (or ESC/CANCEL) to go back.

---

## 11. Automation

### 11.1 A track's automation mode

Each strip's **AUTO** button sets the track's automation mode. The LEDs show the mode (Trim, Read, Touch, Write, Latch, Off).

| Action | Result |
|---|---|
| **AUTO** | Moves to the next mode, in this order: Write → Touch → Latch → Trim → Read → Off, then back to Write. |
| **WRITE / TOUCH / LATCH / TRIM / READ / OFF** (AUTOMATION section, held) **+ AUTO** | Directly sets that mode on the track. |
| **CTRL (⌘) + AUTO** | Opens / closes the track's envelopes in REAPER. |
| **SHIFT + AUTO** | Enters **automation navigation** on this track: only this track is selected, all its envelopes are shown, and the arrows and jog wheel choose the envelope (see §12). The AUTO LED blinks. Press again (or SHIFT + AUTO on another track) to leave; the envelopes are hidden. |

### 11.2 Arming envelopes

The **FADER, PAN, MUTE, SEND LEVEL, SEND MUTE, PLUG-IN** buttons (ENABLE column) turn the matching automation on / off for tracks that have an automation mode. If a track does not yet have the requested envelope, a message ("Arm Env") lists the tracks concerned.

### 11.3 Write to start / end / all

**WRITE AUTOMATION TO** section:

| Button | REAPER action |
|---|---|
| **START** | Action 42014 |
| **END** | Action 42015 |
| **ALL** | Action 42017 (whole project); with **SHIFT**: action 42013 (selection) |

### 11.4 AUTO SUSPEND

**AUTO SUSPEND** suspends automation globally (REAPER action 40908, LED kept in sync).

---

## 12. Jog wheel, navigation and zoom

### 12.1 SHUTTLE: two jog modes

The **SHUTTLE** button switches between two modes:

| SHUTTLE | Jog wheel | Numeric keypad |
|---|---|---|
| **LED on** (default) | Moves the cursor, zooms, selects… (§12.2) | Entry of values / markers / calculations (§14) |
| **LED off** ("Tab" mode) | Sends **Tab** keys to the foreground application: used to move from field to field in a dialog | The keypad keys are sent to the keyboard (useful for typing in REAPER's dialogs) |

### 12.2 The jog wheel in SHUTTLE mode

With no edit tool active, depending on the modifiers held:

| Modifiers | Action |
|---|---|
| (none) | Moves the **edit cursor**. With **GRID** on, the cursor snaps to the grid; otherwise the step depends on the turning speed and zoom. **CTRL (⌘)** held = 10× finer step. |
| **CTL** | Moves the cursor **and selects** (time, items and envelope points passed over). |
| **SHIFT + CTL** | **Additive** selection (adds instead of replacing). |
| **SHIFT** | **Horizontal zoom**. |
| **SHIFT + OPT** | **Vertical zoom**. |
| **OPT** | Selects the previous / next track (in automation navigation: the previous / next envelope). |
| **OPT + CTRL (⌘)** | Extends the track selection. |

When an **edit tool** is active (§13), the wheel acts on the item or selection (move, trim, slide the content…).

### 12.3 NAV, ZOOM, SEL ADJ and the arrows

The **NAV**, **ZOOM** and **SEL ADJ** buttons (above the navigation cross) choose the role of the 4 buttons **UP / DOWN / PREVIOUS / NEXT**:

| Mode | UP | DOWN | PREVIOUS | NEXT |
|---|---|---|---|---|
| **NAV** | Scroll up | Scroll down | Scroll left | Scroll right |
| **ZOOM** | Vertical zoom in | Vertical zoom out | Horizontal zoom in | Horizontal zoom out |
| **SEL ADJ** | — | — | Cursor to the selection **start** | Cursor to the selection **end** |
| (none lit) | **↑** key | **↓** key | **←** key | **→** key |

- **SEL ADJ** is only available when a time selection exists (otherwise the message "There is no selection avaiblable !" appears). The wheel then adjusts the **point** (start or end) where the cursor is.
- In automation navigation (§11.1), UP / DOWN choose the envelope.

### 12.4 Zoom presets

| Button | Action |
|---|---|
| **ZOOM PRESET 1** | Zooms out to the whole project and toggles minimum track height. |
| **ZOOM PRESET 2** | Cycles through track heights (vertical zoom). |
| **ZOOM PRESET 3** | Toggles between "zoom to selection" and "zoom out to project". |

### 12.5 GRID, SPOT

- **GRID**: turns REAPER's **snap** on / off (LED kept in sync).
- **SPOT**: turns the project **lock** on / off. **SHIFT + SPOT**: locks / unlocks the **selected items**.

---

## 13. Edit tools

The **SELECT, GRAB, TRIM, SHUFFLE, SLIP, PENCIL** buttons (EDIT TOOLS / EDIT MODE sections) are mutually exclusive. They set what the **jog wheel** (§12.2) does to the selected item.

| Button | Jog wheel effect |
|---|---|
| **SELECT** | "Item selection" switch (LED saved in the project). Off: deselects all items. |
| **GRAB** | Adjusts the selected item (grow / shrink); the time selection is set to the item. |
| **TRIM** | Trims the item's start / end. |
| **SHUFFLE** | Moves the item. |
| **SLIP** | Moves the item's inner **content** (slip edit). |
| **PENCIL** | **Stretch markers** mode: **CTL** adds a stretch marker at the cursor position, the wheel moves to the nearest marker. |
| **MIDI TOOLS** | Shows / hides REAPER's MIDI window; **SHIFT**: opens the item in the built-in MIDI editor. |

**GRAB** and **TRIM** require **exactly one selected item**: otherwise the messages "No items selected !" or "Too many items selected !" appear.

### Edit functions

| Button | Action | With SHIFT |
|---|---|---|
| **CUT** | Cut (time selection, items or envelope points depending on focus) | — |
| **COPY** | Copy | — |
| **PASTE** | Paste | Insert time, then paste |
| **DELETE** | Delete items / tracks / envelope points depending on focus | — |
| **SEPARATE** | Split items at the selection or cursor | **Glue** the items |
| **DUPLICATE** | Duplicate the items (requires a selection) | Duplicate the selected **area** |
| **REPEAT** | Asks for a number (keypad, **ENTER** to confirm) then duplicates that many times | Same on the selected area |

---

## 14. Numeric keypad, markers and regions

### 14.1 Entry

When the keypad is in **entry** mode (SHUTTLE on), it feeds the driver's dialogs (shown on the console). **CLR** erases (backspace), **ENTER** confirms, **ESC/CANCEL** cancels. A 1-minute timer exits the mode automatically (message "Too Late — Exit Numpad Mode").

### 14.2 Markers ("/" key) and regions ("*" key)

Press **/** to work on **markers**, **\*** for **regions**. Then:

| Key | Markers | Regions |
|---|---|---|
| (digits + **ENTER**) | Go to marker no. N | Go to the start of region no. N |
| **=** | **Set**: puts marker no. N at the cursor | Puts region no. N on the **time selection** (selection required) |
| **+** | **Add** a marker at the cursor (ENTER to confirm) | **Add** a region on the selection (ENTER to confirm) |
| **−** | **Delete** marker no. N | **Delete** region no. N |

Messages confirm the result ("Added marker : 3", "Region 2 is setted"…) or report an error ("… is not an existing marker Nb").

### 14.3 Calculator ("=" key)

When a numeric field is active in REAPER (in Tab mode, SHUTTLE off): **=** copies the field's value; choose the operator **+ − × ÷**, type a number, **ENTER**: the result is pasted into the field. **ESC/CANCEL** cancels.

---

## 15. Groups, windows, undo and save

### 15.1 Track groups

**GROUPS** section:

| Button | Action |
|---|---|
| **CREATE** | Creates a group (media / razor editing) with the selected tracks. |
| **ENABLE** | Asks for the group **number** (1 to 128, keypad + ENTER) and enables / disables it. |
| **EDIT** | Opens REAPER's group settings. |
| **DELETE** | Removes the selected tracks from all their groups. |
| **SUSPEND** | Suspends / resumes all groups (LED kept in sync). |

### 15.2 Windows

**WINDOW** section: **MIX** (mixer), **TRANS** (transport), **STATUS** (REAPER action 40240), **MEM-LOC** (marker/region manager) show / hide the matching window, and **PLUG-IN** the selected plugin's. The LEDs follow the windows' state.

### 15.3 UNDO / SAVE

| Button | Action |
|---|---|
| **UNDO** | Undo (the LED shows that an action can be undone). |
| **CTRL (⌘) + UNDO** | **Redo**. |
| **SHIFT + UNDO** | Shows the **undo history** (action 40072). |
| **SAVE** | Saves the project. The LED **blinks** as long as the project is modified and unsaved. |
| **CTL + SAVE** | Sets **autosave**: type the minutes on the keypad (0 = disabled) then ENTER; **ESC/CANCEL** cancels. The setting is saved in the project. |

---

## 16. Displays, counter and meters

| Item | Function |
|---|---|
| **SHOW VALUES** | Temporarily shows the **values** (volume/pan/parameter) on the strip displays. |
| **SHOW CHANNEL NAMES** | Temporarily shows the track **names**. |
| **SHOW INFO** | Recalls the last information text shown on the text line. |
| **FINE ADJ ENCODERS** | Turns encoder **velocity sensitivity** on / off (message "Velocity sensible : On/Off"). |
| **COUNTER MODE** | Changes the counter unit: hours:minutes:seconds.ms → timecode (frames) → feet.frames → bars.beats (tempo and time signature come from REAPER). |
| **Meters** | Level of each track. |
| **METER SELECT: PRE FADER / POST FADER / INPUT** | Chooses the metering: pre-fader or post-fader (a REAPER setting; these two choices **disarm all tracks**), or input (**arms all tracks** so you can see the input). |
| **CLEAR PEAKS** | Clears the peak indicators (REAPER action 40527). |

---

## 17. Driver configuration

The configuration lives in the **`ReaperDriverControl24.ini`** file, next to the REAPER plugins. Edit it with the **Control24 Config** app (local web page, `http://127.0.0.1:5119`) or by hand.

### General settings (`[Driver]` section)

| Key | Purpose |
|---|---|
| `Autostart` | Starts the driver when REAPER loads the plugin. |
| `NetworkInterface` | Network interface connected to the Control|24. `TestWithoutC24` simulates a console (to test without hardware). |
| `VSDebug` | `True`: REAPER does not launch the driver; it is started from Visual Studio (developers). |

### Plugins and parameters (Control24 Config app)

- **Plugins**: visibility of each plugin in the assignment list (§8.2), 8-character **short name** (forbidden characters: `|` `=` `[` `]`; the name must be unique).
- **FX parameters**: for each plugin, **order**, **short name** (4 characters) and **long name** (shown over 2 strips), **visibility**, **text separators**. A preview shows the layout on the console (24 strips per page). Non-ASCII characters may not display correctly on the displays.
- **File & backups**: a copy of the file is made before each save (30 are kept) and can be restored.

To add the parameters of a new plugin: add it in REAPER, then press **EXT TRANS** on the console (parameters already present are kept).

---

## 18. Troubleshooting

| Symptom | What to check |
|---|---|
| The driver does not start with REAPER | Check `Autostart=True` and `VSDebug=False` in the `.ini`. The message "No network interface specified" means `NetworkInterface` is empty. |
| "Control24 initialization timeout. Check hardware connection." | The console does not respond: check that it is powered on, the Ethernet cable and the chosen network interface. |
| No response from the console on macOS | Check that the **ChmodBPF** package is installed and that you restarted (see the README). |
| A plugin does not appear in the assignment list | It is not marked *visible* in the configuration app, or is unknown to the file: start the driver, then press **EXT TRANS**. |
| A plugin's parameters are missing | Add the plugin in REAPER, then press **EXT TRANS**. |

---

## 19. Quick reference of REAPER actions used

To find an action in REAPER's action list, search for its number (the "Command ID" column).

| Console function | REAPER action(s) |
|---|---|
| Play / pause / stop / record | 1007, 1008, 1016, 1013 |
| Fast forward / rewind | 40085, 40084 |
| Project start / end | 40042, 40043 |
| Selection start / end (IN / OUT) | 40625, 40626 |
| Loop (LOOP PLAY) | 1068 |
| Auto-punch: normal / selection / items | 40252, 40076, 40253 |
| Disarm / arm all tracks | 40491, 40490 |
| Pre-/post-fader peaks | 42076 |
| Clear peaks | 40527 |
| Snap (GRID) | 1157 |
| Lock (SPOT) / selected items | 1135, 40687 |
| Scroll up / down / left / right (NAV) | 40138, 40139, 40140, 40141 |
| Vertical zoom in/out | 40111, 40112 |
| Horizontal zoom in/out | 1012, 1011 |
| Zoom project / selection | 40295, 40031 |
| Undo / redo / history | 40029, 40030, 40072 |
| Save | 40026 |
| Cut / copy / paste / insert / delete | 41384, 41383, 42398, 41748, 40697 |
| Split / glue | 40061 or 40012, 42432 |
| Duplicate items / area | 41295, 41296 |
| Groups: create / remove / settings / suspend | 42578, 42579, 40772, 40771 |
| Group n enable/disable | 42510 + n |
| MIX / TRANS / STATUS / MEM-LOC windows | 40078, 40259, 40240, 40326 |
| Write automation: start / end / selection / all | 42014, 42015, 42013, 42017 |
| Suspend automation | 40908 |
| MIDI editor | 40716, 40153 |
| Stretch marker | 41842 |
