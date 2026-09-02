# Diagnostics and crash reporting for Early Access

Window-layout persistence is local and does not require diagnostics to be
enabled. No profile name, save identifier, world seed, free-form player text, or
absolute filesystem path should enter diagnostic events.

## Privacy boundary

The Early Access options screen should explain diagnostic collection in plain
language and provide an immediately effective opt-out. Collection may default to
enabled only where the distribution and applicable law permit that model. The
setting must be checked before an event is recorded, queued, or transmitted.

Use a random 128-bit installation secret stored in local settings. Derive the
server-facing pseudonymous token as SHA-256 over a versioned product namespace
and that secret. Do not derive it from a Steam ID, operating-system account,
profile name, hardware serial number, or other external identity. Rotation and
deletion must be possible from the options screen.

UI-layout events should contain only:

- a stable window ID;
- normalized position and size rather than raw desktop coordinates;
- viewport size class, UI scale, locale, and game version;
- an event kind such as opened, moved, resized, or closed;
- monotonic session-relative timing.

Session display and interaction summaries should additionally contain:

- every viewport-resolution, UI-scale, monitor, and window-mode transition,
  plus the final values and the mode/resolution with the longest active time;
- durations and percentages spent windowed, maximized, fullscreen, exclusive
  fullscreen, minimized, and running without application focus;
- player-idle duration buckets based on time since the last game input, with
  thresholds versioned in the event schema;
- real-time duration and percentage at each simulation speed, including pause;
- enough session-duration counters to distinguish a short launch from a long
  background or unattended session.

Minimized, unfocused, and player-idle are separate states and may overlap. The
server must not add their percentages together as if they were mutually
exclusive. Player-idle detection must use only input timestamps; it must not
record keys, text, mouse coordinates, other applications, or operating-system
activity.

Batch events locally with a short retention limit, bounded disk quota, retry
backoff, and idempotency keys. Transport must use HTTPS and a versioned endpoint.
The server must publish retention and deletion rules before collection ships.

## Crash guardian

The release guardian should be a separate, minimal process launched by the game
distribution entry point. It observes the game process exit code and heartbeat;
it must not inject into the game or monitor unrelated processes. On an abnormal
exit it gathers only the current run's bounded logs, build/platform metadata,
and an optional minidump when enabled.

Before upload, show a localized report window that:

- describes every attached file and its size;
- redacts account names and absolute paths;
- lets the player add or edit a description;
- allows removing individual attachments;
- sends only after an explicit confirmation, even when ordinary diagnostics are
  enabled;
- can save the report locally when the network is unavailable.

The guardian must use the same pseudonymous token policy as diagnostics, but a
separate versioned derivation namespace so the two datasets cannot be joined by
token alone.

## Delivery gates

Do not enable network submission until the endpoint contract, authentication,
rate limits, payload limit, retention period, deletion flow, privacy notice, and
localized EN/PL UI are implemented and tested. Release validation must include
offline startup, endpoint failure, repeated submission, corrupted queue data,
redaction fixtures, opt-out enforcement, guardian self-failure, and a packaged
Windows launch/crash test.
