# Deck monitor specification

This document fixes the scope and safety rules for live system monitors in RELYR.

## Scope

- Monitors are available only from the Action library while the Deck editor is open.
- They are never offered to the main keyboard, Space, CapsLock, mouse, taskbar, macro, or gesture assignment screens.
- A monitor occupies one ordinary slot in the user's current Deck layout. RELYR never creates, resizes, or rearranges a layout when a monitor is assigned.
- Normal Action buttons and monitors can be mixed in any layout, including 1x18 and 18x1.
- A dedicated Deck-monitor drag payload is used. Keyboard drop targets cannot accept that payload.
- Dropping a monitor replaces the complete previous contents of that Deck slot and uses the same five-second undo transaction as an Action drop.
- Dropping a normal Action onto a monitor slot replaces the monitor.

## Monitor library

- CPU/RAM usage, CPU/GPU temperature, GPU usage and GPU memory, fan speed, storage usage, physical-disk read/write throughput, network upload/download/latency/status, clock/date/uptime, battery, volume, microphone, brightness, Wi-Fi, Bluetooth, and overall system status.
- Values are sampled by one shared service at no more than once per second. A Deck with many monitor tiles must not create one timer per tile.
- Disk throughput is sampled from the physical-disk `_Total` counter on every one-second monitor tick so it follows Task Manager's disk view more closely than a five-second logical-volume sample.
- Temperature and fan sensors run through the isolated hardware helper. Integrated GPU memory falls back to shared memory; a genuinely unavailable reading displays `—`, never a fabricated `0M`.
- Wi-Fi and Bluetooth show actual cached radio `ON`/`OFF` state. Bluetooth enumeration and events stay off the UI and input threads.
- Unsupported or unavailable values display `—`; RELYR must never fabricate a reading.
- Hardware values that require a privileged third-party driver are not silently enabled or installed.

## Battery state

- Charging: percentage plus a lightning symbol and charging accent.
- Connected and full: percentage plus a plug symbol.
- Discharging: percentage with the normal battery symbol.
- Low battery: warning accent below the low-battery threshold.
- Unknown or no battery: `—` and a neutral state.

## Interaction

- Brightness and audio monitors open a compact control inside the existing opaque Deck window. No transparent or full-screen top-level helper window may be created.
- Brightness and volume use an in-place slider. Microphone exposes mute state and a level control when supported.
- Wi-Fi and Bluetooth open their corresponding Windows settings/Quick Settings surface when direct radio control is unavailable.
- The editor preview never changes a system setting; clicking there continues to select the Deck slot for editing.
- Sliders and thumbs retain their own mouse capture. Pressing one must never begin movement of the Deck window.

## Appearance

- Monitor cards use the existing RELYR theme resources and remain readable in light and dark modes.
- Metrics use distinct semantic accents and compact, live visualizations: flowing lines for utilization/temperature/latency, moving columns for throughput, dots for state/memory, and gauges for levels.
- Labels stay short, numeric text uses tabular alignment, and semantic warning colors are limited to actual warning states.
- Live updates do not resize controls or move surrounding slots.
- Motion is restrained and governed by RELYR's Animation setting. Disabling motion never changes hit testing or the reported value.

## Failure isolation

- Sampling, value formatting, UI refresh, and control commands are independently exception-contained.
- Closing a Deck unsubscribes its views. With no subscribers, the sampling timer stops.
- A provider failure affects only its own reading and cannot close RELYR, block input, or create a click-intercepting layer.
- Existing input hooks and layer execution paths are not changed by monitor rendering.
