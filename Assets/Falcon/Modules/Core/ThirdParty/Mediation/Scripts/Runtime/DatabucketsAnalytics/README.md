# Databuckets Analytics

Send dictionary events as compact NDJSON to an HTTPS `/push` endpoint. Requires Unity 2022.3+.

Start with the [English customer guide](USER_GUIDE.md) for `.unitypackage`/Package Manager installation, a function-by-function API reference, and every configurable limit with defaults and valid ranges. A [Vietnamese quick-start guide](USER_GUIDE_VI.md) is also available. This README provides the detailed event-field and delivery reference.

```csharp
using Databuckets.Analytics;
using UnityEngine;

public sealed class DatabucketsBootstrap : MonoBehaviour
{
    private void Awake()
    {
        DatabucketsTracker.Init(
            "YOUR_API_ENDPOINT",
            "YOUR_API_KEY");
    }
}
```

Record an event without additional fields:

```csharp
using Databuckets.Analytics;

DatabucketsTracker.Record("game_started", null);
```

Record an event with fields:

```csharp
using System.Collections.Generic;
using Databuckets.Analytics;

DatabucketsTracker.Record(
    "level_completed",
    new Dictionary<string, object>
    {
        ["level"] = 12,
        ["score"] = 9500,
        ["won"] = true
    });
```

Call `Init` on Unity's main thread during startup. Replace `YOUR_API_ENDPOINT` with an absolute HTTPS `/push` URL, such as `https://events.example.com/push`. The API key is required and sent in the `X-API-Key` request header. The SDK automatically adds `resolve_country=true` to every `/push` POST. Initialization creates one component that survives scene changes and sends automatically. Repeated calls with the same endpoint and key reuse an active tracker; a different configuration while it is active raises `InvalidOperationException`. A failed initialization automatically closes its worker. Observe `Ready` and `Completion` failures, correct the cause, then call `Init` again after `Completion` finishes to create a new tracker. The same replacement works after a client has closed because of a lifecycle failure; a completed client is never silently reused.

Pass initial user properties when they must be present on `first_open`, `session_start`, and immediately recorded events:

```csharp
DatabucketsTracker.Init("YOUR_API_ENDPOINT", "YOUR_API_KEY",
    new Dictionary<string, object> { ["plan"] = "premium" });
DatabucketsTracker.Record("game_started");
```

The optional dictionary is captured during initialization and merged with saved properties on the worker before startup events are created. Supplied keys overwrite saved values; other saved keys remain. Null or an empty dictionary adds nothing. SDK-owned fields retain their normal precedence. Invalid or oversized inputs throw during `Init`; a failed merge/save faults `Ready` before creating new events. Changes survive restarts and do not rewrite already queued events. Repeated `Init` calls on an active tracker ignore initial properties; use `SetUserProperties` for later updates. Lower-level clients accept the same dictionary through `EventOptions.InitialUserProperties`.

`Record(eventName, fields)` accepts an `IDictionary<string, object>`. Null or omitted fields mean no additional fields, so `Record("game_started")` also works. Call `Init` before recording; there is no need to await `Ready` first. Calls before initialization raise `InvalidOperationException`. Names must not be null, empty, or whitespace-only.

`DatabucketsTracker.Record`, `SetUserProperties`, cached getters, and `PersistAsync` are thread-safe. Call `Init`, `Flush`, and `RefreshDeviceProperties` on Unity's main thread. `DatabucketsTracker.Ready`, `LastError`, and `Completion` expose initialization, errors, and shutdown completion. Await `DatabucketsTracker.PersistAsync()` to confirm persistence of remaining accepted events, subject to disk-quota eviction.

The lower-level `EventTracker`, `EventClient`, and `EventOptions` APIs remain available in `RealtimeEvents` for custom configuration and lifecycle control. Their event method is named `Track`; `DatabucketsTracker.Record` uses the same event pipeline. The lifecycle methods below that mention `Tick`, `SetSuspended`, or `Dispose` apply to these lower-level APIs.

## Nonblocking execution and durability

`Ready` completes after initialization, recovery, identity, cached metadata, and any initial user-property merge/save. Events and property updates can be queued before it completes. Getters return their default values until the worker publishes a snapshot.

- `Record()` / `Track()` return after bounded serialization and enqueueing. `EventClient.TrackAsync()` completes after the processor accepts the event; main-thread submissions can remain buffered. Submissions from background callbacks or while suspended are persisted by the worker before their task completes.
- `SetUserProperties()` enqueues an update. `EventClient.SetUserPropertiesAsync()` completes after its atomic save. Later submitted events use that update if it succeeds.
- `Persist()` schedules a save. **Await `PersistAsync()` to confirm persistence of remaining accepted, unattempted events, subject to disk-quota eviction.** It does not wait for HTTP and never makes an uncertain upload replayable. The barrier also reports an earlier queued operation failure once, so void calls cannot silently hide a rejected event. Retrying a failed save should retry persistence, not an already accepted event.
- `Flush()` queues a send request after earlier submitted events, respecting suspension and retry backoff. Keep calling `Tick()` to advance networking.
- `Dispose()` returns promptly. Await `Completion` / `EventClient.DisposeAsync()` before recreating a client for the same directory. Do not use `.Wait()`, `.Result`, or thread joins on Unity's main thread.

Initialization and asynchronous operation failures appear through tasks and `LastError`. Input validation and pending-work overflow still throw synchronously. If lifecycle transitions cannot enter a full command queue, or storage failures retain `MaxPendingOperations` unprocessed focus transitions, the client stops networking and closes after attempting to persist accepted work. `Completion` and `LastError` report the failure; it never waits for capacity inside a callback. Recreate it after storage recovers and `Completion` finishes.

Worker ticks save newer buffered events when their flush interval or batch limit is reached, including while draining an older disk backlog, waiting for HTTP, or backing off a failed read/claim/restore/delete; an explicit flush also requests this save. Buffer-save failures retain the buffer and use a separate retry backoff, so they cannot delay acknowledging or draining older requests. While suspended, ticks retry failed persistence and attempt cleanup without starting HTTP, if Unity continues to run. Networking still runs one request at a time, and a timeout after uploading is never treated as permission to replay that upload. The response handler uses a fixed receive buffer to avoid per-chunk allocations, following [Unity's download-handler guidance](https://docs.unity3d.com/2022.3/Documentation/Manual/UnityWebRequest-CreatingDownloadHandlers.html).

`RequestTimeoutSeconds` also sets a monotonic deadline checked by `Tick()`, starting when native sending begins. At the deadline the SDK requests cancellation and allows at most one additional second of main-thread pumping for Unity to publish the final status. An uncertain request is then retired so later batches can proceed. An observed HTTP 503 remains retryable even if its response body or cancellation never finishes. The deadline requires Unity to keep ticking; suspension cancels the request immediately.

An observed HTTP 410 permanently disables recording and sending for the current storage directory. The worker saves an `sdk-disabled` marker and retries failed saves with backoff; after that marker is saved, restart also stays disabled. `LastError` reports the disablement. Resume, flush, and repeated initialization do not re-enable it. Already accepted, unattempted memory is saved during disablement, but later submissions are ignored and the saved backlog is not sent.

Limits default to 1,024 pending operations / 4 MiB of queued UTF-8 snapshots, 4,096 batch files, and 64 KiB of user properties. When upgrading, larger existing property files require an explicit `MaxUserPropertiesBytes` increase within the supported range. Input snapshots allow at most 1,024 top-level fields (including `event_name` for named events) and 32 nested containers. `MaxBatchBytes` and `MaxUserPropertiesBytes` support at most 1 MiB; `MaxDiskFiles` supports at most 65,536. Increase the file limit explicitly when recovering a larger existing queue. An over-limit queue faults `Ready` without deleting pending events. A forced kill before a persistence task completes can lose pending memory; lifecycle callbacks cannot guarantee completion after the OS suspends execution.

Every new event has an integer `_ts` in UTC Unix epoch milliseconds, captured when `Record` or `Track` starts and retained through buffering and retries. Event fields and user properties cannot override `_ts`.

Every event also includes these SDK-owned date and retention fields:

| Event parameter | Calculation |
| --- | --- |
| `event_date` | UTC calendar date of `_ts`, as an integer `yyyyMMdd`, such as `20260919` |
| `retention_day` | UTC calendar-day boundaries crossed since `first_open_timestamp` |
| `retention_hour` | Completed elapsed hours since `first_open_timestamp` |
| `retention_minute` | Completed elapsed minutes since `first_open_timestamp` |

`install_day` is an SDK-owned, persistent user property containing the UTC calendar date of `first_open_timestamp`, as an integer `yyyyMMdd`, such as `20260526`. It is included in each event and restored from the first-open cache on restart. Caller-supplied values cannot override these five fields.

`retention_day` uses `max(0, floor(_ts / 86400000) - floor(first_open_timestamp / 86400000))`, counting UTC calendar-day boundaries independently of the device timezone. `retention_hour` and `retention_minute` use `max(0, _ts - first_open_timestamp)` divided by `3600000` or `60000`, respectively, with integer division. For example, first open at `2026-05-26 23:59 UTC` and an event at `2026-05-27 00:01 UTC` produce `install_day = 20260526`, `event_date = 20260527`, and retention values `1`, `0`, and `2`. An event timestamp before first open produces zero retention.

When `first_open_timestamp` is unknown (`-1`), `install_day` and all retention fields are JSON null; `event_date` still comes from the event timestamp. Unrepresentable dates also produce null derived values. The SDK uses its cached or initially selected first-open timestamp, including the existing two-day selection rule, rather than a caller-supplied event field. Install-day persistence failures appear in `LastError` and retry with the existing metadata backoff; event fields still derive from the selected timestamp. Buffering, persistence, and retries preserve the original values, and older queued events are not rewritten.

Every new event also receives these SDK-owned local-time parameters, calculated from its captured `_ts` plus `device.time_zone_offset_seconds`:

| Parameter | Value | Example: Saturday at 23:04 |
| --- | --- | --- |
| `event_local_day_of_week` | String `"0 - Sunday"`, `"1 - Monday"`, …, `"6 - Saturday"`, or `"-1 - Unknown"` (always English) | `"6 - Saturday"` |
| `event_local_hour` | Integer `0` to `23`, or `-1` when unknown | `23` |
| `event_local_hour_minute` | Integer `hour * 100 + minute` (00:05 is `5`), or `-1` when unknown | `2304` |

These values are recalculated for each `Record`, `Track`, or `TrackAsync` call and override matching event fields or user properties. They use the saved timezone offset in worker submission order, including the latest successful device-property refresh; missing, null, non-integer, or out-of-range offsets (a magnitude of 24 hours or more), and unrepresentable local dates, produce `event_local_hour = -1`, `event_local_hour_minute = -1`, and `event_local_day_of_week = "-1 - Unknown"`. On new events, the hour and HHmm fields are always JSON integers and the weekday is always a non-empty English string; none of these fields are null. Editor and desktop runs use these unknown values unless an offset is supplied. Previously queued events retain their original payloads, including any null local-time fields. Automatic first-open and session events use their own `_ts`. On resume, the device-property refresh is attempted before creating new session events; a failed refresh retains the previous saved offset. Persistence and retries retain the original values.

Each initialization sets `geo.country` to the cached country code (such as `VN`), or `"__"` when no valid cache is available, and sets `country` to `"__"`. These defaults apply before `first_open`, including in the Editor. The first valid country received from a successful `/push` response, such as plain text `COUNTRY:VN`, immediately sets `geo.country` for future events and is saved to `country-code.txt` in the queue directory. This country is cached once, has no expiry, and is never replaced by later responses, including after restart. Already tracked events keep their original snapshots. Every `/push` POST automatically includes `resolve_country=true`, including retries and queued batches sent after restart. The transport replaces existing values for this parameter and preserves other query parameters. Empty, malformed, oversized, and failed responses do not establish a country. Cache writes are atomic; failures appear in `LastError` and retry the same first country with backoff without blocking event delivery. A failed startup cache read also retries with backoff, even without a network response, and restores the original country once storage recovers.

Initialization reuses the cached `FirstOpenTimestamp` without querying the platform again. Without a cache, Android reads the package's [`firstInstallTime`](https://developer.android.com/reference/android/content/pm/PackageInfo#firstInstallTime). iOS reads the existing Documents directory's [`creationDate`](https://developer.apple.com/documentation/foundation/fileattributekey/creationdate) through a small Objective-C++ bridge and converts it to UTC Unix epoch milliseconds. The directory is never created by the lookup. For a new queue, installation metadata is read before creating storage directories, so queue setup cannot fabricate the date. Missing, unreadable, invalid, or future iOS metadata produces `first_open_timestamp = -1`. Native Objective-C exceptions and managed interop exceptions also return `-1`, but the native bridge must compile and link successfully: iOS plug-ins are [statically linked](https://docs.unity3d.com/2022.3/Documentation/Manual/NativePlugins.html), so a missing native symbol can fail the build before C# runs. Installation lookup and queue recovery run on the storage worker; neither holds a lock needed by Unity's main thread. There is no network lookup.

For a valid platform date, the original two-day rule applies: strictly older than 48 hours uses that date; otherwise it uses the current time. An unknown iOS date stays `-1` and never falls back to the current time. Android metadata failures, the Editor, and desktop retain their current-time fallback. Documents creation time is an installation-age estimate; Apple defines it as a filesystem creation date, without guaranteeing it represents installation.

Upgrades from Databuckets 1.1.0, 2.1.3, and 3.0.3 automatically recover `user_pseudo_id`, the session counter, and the first-open timestamp. Existing 4.x records take precedence. Missing values are filled from native 3.0.3 state, then the highest-generation valid 2.1.3 JSON slot, then 1.1.0 PlayerPrefs. The first new session continues at the recovered counter plus one. Old caches remain untouched.

| Previous SDK | Persistent storage used for migration |
| --- | --- |
| 1.1.0 | PlayerPrefs strings `user_pseudo_id`, `session_number`, `first_open_timestamp`; timestamp fallback: `first_open_candidate_open_ts`, then `install_timestamp` only when `is_sent_first_open == 1`. |
| 2.1.3 | `databuckets_sdk_state_a.json` / `databuckets_sdk_state_b.json` under `Application.persistentDataPath`; SHA-256-checked `payload_json` fields `sdk_persistent_properties.user_pseudo_id`, `session_number`, `first_open_ts_ms`. Missing/invalid fields fall back to the other valid slot or older preferences. |
| 3.0.3 Android | SharedPreferences `databuckets_static_fields`: `user_pseudo_id`, `session_number`, `rt2_anchor_ms`; the old SDK's first-open timestamp is derived from `PackageInfo.firstInstallTime`, consulted only when legacy SDK state exists. |
| 3.0.3 iOS | Standard UserDefaults: `com.databuckets.user_pseudo_id`, `com.databuckets.session_number`, `com.databuckets.first_seen_ms`, with `com.databuckets.rt2_anchor_ms` as timestamp fallback. |

A recovered first-open timestamp is saved directly as completed metadata: **no `first_open` event is queued**, regardless of the old SDK's delivery flag. This remains true after restart or a failed migration write. Android 3.0.3 derives its timestamp from the original package installation time, with its persisted `rt2_anchor_ms` as fallback; iOS recovers `first_seen_ms`, falling back to `rt2_anchor_ms`. Invalid values are ignored; unreadable stores or failed commits fault initialization so they cannot silently replace an established installation. When no valid prior timestamp exists, the fresh-install behavior below applies.

Initialization durably queues `first_open` only when the selected timestamp is not `-1`, with both `first_open_timestamp` and `_ts` equal to that value. Other events always receive their own track-time `_ts`. Saved properties are included, but cannot override these fields or the first-open event's `event_name`. The event and cache share a single atomic record, so an interrupted startup cannot create a second queued event. The record stays in the queue directory as `first-open-pending.json`, `first-open-sending.json`, or `first-open.json` after sending/retirement.

An unknown timestamp is cached atomically as `-1` directly in `first-open.json`, without queuing or sending an event. This metadata does not consume the event queue quota or need to fit `MaxBatchBytes`. Cached `-1` is reused across restarts without repeating the platform lookup; pending `-1` first-open records from older SDK versions are retired without sending. A malformed or incomplete committed record fails initialization without being overwritten or replayed. Keep these files with the queue. Pending payloads count toward disk limits. If startup events cannot yet be saved, the client reports `LastError` and retries while older batches drain. An uncached `FirstOpenTimestamp` is `0` until its record can be committed; a previously cached timestamp stays available. Storage retries retain the initially selected timestamp and do not repeat a successful platform lookup. Events exceeding `MaxBatchBytes` require a larger limit or smaller user properties. The cache is scoped to `StorageDirectory` (endpoint-specific by default); clearing it allows first-open detection again. Delivery and retries follow the regular event policy.

The package automatically adds its Objective-C++ bridge to exported iOS Xcode projects and merges the `NSPrivacyAccessedAPICategoryFileTimestamp` reason `C617.1` (app-container metadata) into `UnityFramework/PrivacyInfo.xcprivacy`, preserving existing manifest entries. See [Apple's required-reason API documentation](https://developer.apple.com/documentation/bundleresources/app-privacy-configuration/nsprivacyaccessedapitypes/nsprivacyaccessedapitype).

`SetUserProperties` accepts one or more tuples or an `IDictionary<string, object>`. Each call atomically adds or overwrites the supplied keys and retains other properties. Keys are case-sensitive; null is stored as JSON null, and an empty update does nothing. For tracked events, saved properties win collisions with event fields, including `"event_name"`, except for SDK-owned timestamp, engagement, date, retention, local-time, and session fields. They are included in the same flat JSON object and count toward the event byte limit. Already tracked events keep their original snapshot.

Updates are saved by the worker to `user-properties.json`, surviving scene changes and restarts with the same directory. `SetUserPropertiesAsync()` completes after a flushed staging file atomically replaces the snapshot; a failed update faults its task before publishing properties. Uncommitted staging files are ignored. Corrupt, unreadable, or oversized committed properties fault `Ready` without retaining queue ownership. Property storage has its own `MaxUserPropertiesBytes` limit.

Values are serialized during updates and events during `Record` or `Track`; cached JSON fragments are combined without reserializing values. Do not mutate inputs during these calls. Updates work from worker threads and while suspended. Concurrent updates and tracking observe complete snapshots in worker submission order.

Use the dedicated helpers on `DatabucketsTracker` to replace or reset the string arrays `realtime_variants`, `realtime_segments`, and `realtime_personalizations`:

```csharp
DatabucketsTracker.SetVariants("a", "b"); // ["a", "b"]
DatabucketsTracker.SetVariants("c");      // ["c"]
DatabucketsTracker.SetSegments("paid");
DatabucketsTracker.SetPersonalizations("shop", "offers");
DatabucketsTracker.ResetVariants();        // null
DatabucketsTracker.ResetSegments();        // null
DatabucketsTracker.ResetPersonalizations(); // null
```

All three properties appear as JSON `null` on manual and automatic events until set. Each `Set` accepts a string array or individual string arguments and replaces the entire list. Each `Reset` stores JSON `null`. These helpers use the existing `SetUserProperties` persistence path. `EventTracker` and `EventClient` continue to expose the single-value `AddVariant`, `RemoveVariant`, `AddSegment`, and `RemoveSegment` helpers.

The helpers are thread-safe and run in worker submission order with other property updates and events. Changes survive restarts and affect only later events. `EventClient` also provides `AddVariantAsync`, `RemoveVariantAsync`, `AddSegmentAsync`, and `RemoveSegmentAsync`, which complete after saving (or immediately on the worker for a no-op). Save or size-limit failures keep the previous array active and use the same task, `LastError`, and persistence-barrier reporting as `SetUserPropertiesAsync`.

Every new event includes the persistent user properties `sdk_ver` (string, from `Databuckets.Analytics.SdkInfo.Version`) and `sdk_ver_num` (integer, from `SdkInfo.VersionNumber`). The numeric value is derived by removing dots from the version: `4.0.1` becomes `401`, and `4.0.2` becomes `402`. The SDK saves both values on every platform before creating `first_open` or session events, and refreshes missing or older saved values during initialization. Event fields, user-property updates, and device-property refreshes cannot override them. Already queued events retain their original version fields. If the values cannot be saved during startup, `Ready` faults before new events are accepted.

`SdkInfo.Version` in [SdkInfo.cs](Runtime/SdkInfo.cs) reports the SDK version and is available before calling `Init`. Release maintainers must keep it in sync with `package.json`; the exporter rejects mismatched versions.

Every new event also includes the persistent user properties `platform` and `app_platform`. Both contain the same non-null, lowercase runtime platform: `android`, `ios`, `windows`, `macos`, `web` (WebGL), or `unknown` for platforms without a named mapping, including Linux. Windows and macOS Editor runs report the host OS, independently of the selected build target. The SDK saves both properties before startup events and refreshes them on initialization. Caller-supplied event fields, property updates, and device-property refreshes cannot override them; already queued events keep their original values.

At startup on every platform, the SDK reuses the saved `user_pseudo_id`. If it is missing, null, blank, or not a string, the SDK first tries legacy migration, then generates `db-<UUID>` (for example, `db-550e8400-e29b-41d4-a716-446655440000`) and atomically saves it in `user-properties.json` before queuing `first_open`. The ID is included in future events and survives restarts with the same `StorageDirectory`. Existing nonblank string IDs are preserved; device-property refreshes do not regenerate them. Clearing the saved properties allows a legacy ID to be recovered or a new ID to be generated. If the initial ID cannot be saved, `Ready` faults before queuing new events.

The thread-safe accessors are available on `DatabucketsTracker`, `EventTracker`, and `EventClient`:

```csharp
string model = DatabucketsTracker.GetDeviceModel();   // Cached device.mobile_model_name; null if unavailable.
string userId = DatabucketsTracker.GetUserPseudoId(); // Cached user_pseudo_id.
string country = DatabucketsTracker.GetCountry();     // First resolved country code; "__" until available.
```

`GetCountry()` reads the SDK's country cache, independent of custom `country` or `geo.country` overrides. Getters use memory and do not trigger network or device lookups.

Every newly created event includes the SDK-owned integer `engagement_time_msec`: foreground milliseconds since the previous accepted event. It uses a monotonic clock, so wall-clock changes and `Time.timeScale` do not affect it. `first_open`, `session_start`, and `app_focus_start` carry `0`. Events tracked while suspended carry `0`.

The SDK automatically records focus transitions:

- `app_focus_end`: recorded when the game enters suspension, with the remaining foreground time since the previous event. This saves the final foreground interval even when gameplay emits no event before backgrounding.
- `app_focus_start`: recorded when the game returns to the foreground, always with `engagement_time_msec = 0`. Subsequent events count from that resume time. Initialization uses `session_start`; it does not emit an extra focus-start event.

Focus events follow the combined pause, focus, component enable, and quit state that controls delivery. Overlapping or repeated callbacks produce one event per actual state change. Disposing a foreground client also records its final `app_focus_end`. Suspension schedules persistence of the end event; await `PersistAsync()` to confirm the save before a handoff you control. Focus events use SDK-owned names and session fields, and retain their callback timestamps and engagement intervals across storage retries. An expired session ends before the resumed session's `app_focus_start`.

For example, `session_start (0)` → play after 2 seconds `(2000)` → background 3 seconds later, `app_focus_end (3000)` → resume after an hour, `app_focus_start (0)` → play after 4 seconds `(4000)`. The old session totals `5000` ms and the new session totals `4000` ms; the hour in the background contributes nothing. Sum `engagement_time_msec` across **all events**, including automatic events, grouped by `session_id`.

A foreground `session_end` includes the remaining interval, capped at the 30-minute timeout. A later `app_focus_end` does not count that interval again. Background time and time while the process was closed never count as engagement. Rejected events do not consume the interval; persistence, sending, and retries preserve accepted values. Previously queued events are replayed unchanged. A forced kill before persistence can lose the final interval, as with other memory-only events.

The SDK automatically tracks `session_start` on initialization and `session_end` after 30 minutes without activity. Accepted events, suspension of an active session, and a real app resume count as activity; rejected events, property changes, getters, sending, and repeated resume notifications do not. A new activity after expiry starts the next session. `session_end` uses the old session's properties and an `_ts` of last activity plus 30 minutes. When the OS suspends or terminates execution, the expired end event is queued on the next tick, resume, tracked event, or initialization, before a new session starts.

Every event, including automatic events, receives these SDK-managed user properties:

- `session_id`: `<user_pseudo_id>:<session_start_time_epoch_millisecond>`.
- `session_number`: an integer starting at 1, incremented once for each new session.

Both stay fixed throughout a session and override caller-supplied values. Session state and the counter are atomically cached in `session.json` in the queue directory. Restarting within 30 minutes continues the session; an expired restart ends it and increments the counter. Session boundary events use the normal queue, batch-size limits, and delivery policy. Session boundary writes run on the storage worker. Ordinary activity checkpoints are coalesced to the flush interval and are also committed by `PersistAsync()` and shutdown. A process killed before its checkpoint can recover an earlier last-activity time; await `PersistAsync()` when that distinction matters. Failures appear in operation tasks and `LastError`; automatic retries use backoff. Incomplete boundary writes recover before that boundary is sent. A full payload quota does not prevent draining older events. A queue exceeding `MaxDiskFiles` faults `Ready` with its files preserved; raise that limit within the supported range to recover it. Cached boundary payloads must match their session state and contain exactly one valid event. When present, cached engagement must be `0` for `session_start` and between `0` and `1800000` ms for `session_end`; malformed records fail initialization without being replayed. Focus-transition retries retain each original activity timestamp, including when several suspensions and resumes occur before storage recovers, so session boundaries reflect the actual inactivity gaps.

On iOS and Android, `DatabucketsTracker.Init`, `EventTracker.Create`, and `new EventClient` automatically collect the following flat user properties through `SetUserProperties`, before queuing `first_open`. They are refreshed on each actual resume. Editor and desktop runs do not add mobile properties.

| Property | Collected value |
| --- | --- |
| `app_id` | `Application.identifier` (bundle/package ID) |
| `app_version` | `Application.version` |
| `device.category` | `mobile` or `tablet`; iOS uses the device idiom, Android uses the application context's screen configuration (at least 600 dp for tablets; undefined width becomes null) |
| `device.user_default_language` | Preferred native locale as a lowercase language tag, such as `es-us` |
| `device.mobile_brand_name` | `Apple` on iOS; Android `Build.BRAND` with its first letter capitalized |
| `device.mobile_marketing_name` | Model name fallback, such as `SM-A336M` or `iPhone15,2`; no device-name catalog or network lookup |
| `device.mobile_model_name` | Android `Build.MODEL`; iOS hardware model from `uname` |
| `device.mobile_os_hardware_model` | Same hardware model |
| `device.operating_system` | `Android` or `iOS` |
| `device.operating_system_version` | OS name and release, such as `Android 14` or `iOS 18.2` |
| `device.time_zone_offset_seconds` | Integer offset east of UTC at collection time, including daylight saving, such as `-14400` |

Automatic refresh retains unrelated custom properties and overwrites the automatic keys with current values. Unavailable fields are JSON null, including a timezone that cannot be read from the platform. Already queued events keep their original snapshot.

At startup, a saved `device.language` is migrated to `device.user_default_language` and the old key is removed. An existing new-key value takes precedence. This atomic migration runs once, preserves other properties, and leaves previously queued event payloads unchanged. If migration cannot be saved or exceeds `MaxUserPropertiesBytes`, `Ready` faults before startup events are created.

Automatic collection and `RefreshDeviceProperties` are best-effort. Collection or property-save errors are reported through `LastError`, without aborting startup, escaping a resume callback, or blocking event delivery. `Tick` retries metadata with a separate exponential backoff using the configured retry limits; event acknowledgments and HTTP retries continue independently. Native device properties use the previous durable snapshot until saving the refresh succeeds. Thread/disposal checks and explicit `SetUserProperties` calls retain their exception behavior.

Unity/iOS UIKit fields are captured on Unity's main thread. Android metadata and installation lookup run on a worker attached to JNI; all disk work uses the same dedicated storage worker. The SDK does not collect advertising IDs, add the Android advertising-ID dependency or permission, or link iOS tracking frameworks. iOS exports include the native device-property bridge. Previously saved properties and queued event snapshots are retained; removing collection does not erase historical values.

To refresh properties explicitly, call the following on Unity's main thread:

```csharp
DatabucketsTracker.RefreshDeviceProperties(); // Also available on EventClient.
```

## Older mobile devices

This project's Unity 2022.3.62f3 settings target Android 5.1/API 22 and iOS 12. Keep the player's CPU architecture and OS targets compatible with your oldest device. Unity documents an [iOS 13 minimum starting with 2022.3.72f1](https://unity.com/releases/editor/whats-new/2022.3.74f1), even if the target setting still displays 12; check the [requirements for your exact Unity version](https://docs.unity3d.com/2022.3/Documentation/Manual/system-requirements.html) before upgrading.

For a new deployment with small analytics events, start with smaller memory budgets and profile on the oldest supported phone:

```csharp
using RealtimeEvents;

var options = new EventOptions
{
    Endpoint = "https://events.example.com/push",
    MaxBatchEvents = 25,
    MaxBatchBytes = 32 * 1024,
    MaxPendingOperations = 128,
    MaxPendingBytes = 256 * 1024,
    MaxUserPropertiesBytes = 8 * 1024
};
```

These limits trade burst capacity for lower memory use; handle rejected submissions and size your events, including automatic fields and saved properties, to fit. `MaxPendingBytes` counts UTF-8 input snapshots, not total heap usage: strings, dictionaries, tasks, and native request copies need additional memory. Do not lower the property limit below an existing saved property file's size. Older queued batches retain their original size until delivered.

Recovery validates UTF-8 through reusable buffers. Idle publication reuses unchanged property snapshots. Serialization still runs on the caller, and request creation runs on Unity's main thread; keep payloads small, avoid tracking every frame, and never block that thread on an SDK task. Test a release IL2CPP build on the oldest device with a full offline queue, slow storage, repeated pause/resume, and the real server certificate chain. Desktop coverage and managed platform compilation do not measure device frame times or native compatibility.

## Delivery and storage

Defaults: 100 events / 256 KiB per batch, one-second flush interval, 15-second request timeout, 2–60 second exponential retry delay, and 64 MiB of queued disk payload. Only explicit HTTP 503 and confirmed failures before any payload transmission are retried. Completed zero-byte connection failures without an HTTP response qualify; a running zero-byte request does not. Other HTTP errors, partial/full uploads followed by failure, and uncertain attempts are retired. Check `LastError` for failures.

**The `/push` service must guarantee that a 503 response means no events from that batch were stored.** The SDK relies on that contract to avoid duplicates on retry.

Certificate validation stays enabled. A certificate failure is retryable when Unity reports a completed connection error with no HTTP response and zero uploaded bytes. The original batch is saved for retry, just as for HTTP 503. Retries have no attempt limit or expiry and use the configured backoff; `Retry-After` headers are not used. An invalid certificate or trust chain must be corrected before delivery can succeed. After retry backoff, newer queued batches take priority over older saved retries, subject to the disk quota. Successful delivery clears the transport error. `LastError` reports worker-operation and cleanup errors first, followed by transport/session/metadata errors.

Retries reuse the original batch unchanged and retain its existing queue file. Retry events are never appended to another batch; new events form separate batches. A memory batch reserves its queue ID before its first upload without writing to disk. This preserves its age, so newer saved batches take priority over the retry, including after restart. This also applies after pause/resume and restart. A lost response alone does not permit a retry. A server that processes events and returns 503 can still receive them again.

Storage defaults to an endpoint-specific directory beneath `Application.persistentDataPath`. Relative `StorageDirectory` paths are resolved when the client is constructed, before worker initialization. New batches are written and flushed under a `writing-` prefix, then renamed to `pending-` only after the write succeeds. Uncommitted writing files are discarded after restart, so failed writes cannot leave a replayable partial copy. Before a persisted batch is sent, it is renamed to `sending-`; only a confirmed retryable outcome restores it to pending. Only pending files replay after restart. Unresolved sending files are retired, and legacy files without send history are quarantined in `legacy/` without replay. Do not run older SDKs against this queue format. Pause, focus loss, disable, quit, and destruction save pending memory. Successful `2xx` responses acknowledge entire batches. Delivery is not guaranteed: uncertain and non-retryable attempts can be lost. Add event IDs and server deduplication for the 503 retry exception. A forced kill can lose events still only in memory. Disk errors can prevent shutdown persistence.

Pending batches have no age-based expiry. When a new save reaches `MaxDiskBytes` or `MaxDiskFiles`, the oldest pending batches are deleted until it fits. In-flight files are protected, and an older reserved retry cannot evict newer saved batches. First-open and session events can also be evicted; their timestamp/session metadata remains cached. Delivery uses LIFO batch order: the newest ready memory batch or newest eligible saved batch sends first. Events inside a batch retain their original order and bytes. An in-flight request finishes before another batch starts; an older retry retained in memory after a failed save remains active until it can be resolved. Continuous new traffic can delay older batches. Automatic first-open/session events may arrive after more recent events; use `_ts` and session fields to interpret event time. `PersistAsync()` saves the remaining accepted events subject to this policy; it does not protect them from later eviction.

Suspension cancels the current request and stops further sends. Errors inspecting or cleaning up the active attempt do not skip saving newer, unattempted events; persistence still requires working storage. Events from later pause/quit callbacks are saved by the worker without requiring a Unity update; their void calls return before persistence completes. Sending resumes with a fresh request once the component is enabled and the application is both unpaused and focused. Pause/focus callback order and duplicate notifications are handled. Focus loss from an on-screen keyboard also suspends sending conservatively. `Time.timeScale = 0` alone does not suspend the SDK. An uncertain aborted request is never re-enqueued. If reading request progress fails, the uncertain attempt is retired so later batches can proceed; failed cleanup retries without resending. Completed zero-byte connection failures and explicit 503 responses retain their retry eligibility.

Before sending on each update or a manual `Flush()`, the tracker also checks Unity's current focus state to recover from missed focus callbacks. Active pause, disable, and quit states still take priority. Keep the tracker's GameObject active through native ads and app-store visits so it continues receiving pause/resume notifications.

For ad callbacks, calls from a background thread cause the storage worker to persist the event without needing a Unity frame or lifecycle notification. Await `EventClient.TrackAsync()` for that callback's completion, or call `DatabucketsTracker.Record(...)` and await `DatabucketsTracker.PersistAsync()` before a controlled handoff. Pass plain data without blocking getters/converters or Unity object access. Keep the tracker alive through app-store/browser round trips and let Unity's lifecycle callbacks control resumption. Observe asynchronous errors. A failed save of an already accepted event should retry persistence, not tracking. Events never delivered by an ad provider cannot be recovered.

Memory, input serialization, and disk records have explicit bounds. New input snapshots are rejected at their size budget, even when a field would later be overridden. A full processor buffer that cannot spill rejects the new event through its task and the next persistence barrier. Disk work runs on a dedicated worker; callbacks never join it. Native desktop/mobile players with writable storage are required.

Recovery validates pending batch filenames and the UTF-8 bytes in complete queued records before sending. Cached session outbox events must also fit the supported 1 MiB batch limit before they enter the queue; valid older events remain replayable after lowering `MaxBatchBytes`. Invalid filenames, corrupt UTF-8, conflicting session and queue records, multiple first-open states, or pending/sending copies of the same batch fault `Ready` and release queue ownership, preserving committed data for inspection instead of endlessly retrying an unrecoverable batch. Incomplete trailing records are still discarded during recovery.

Only one client/process may own a queue directory. Use local storage with operating-system file locking support. The SDK holds separate locks for same-process and cross-process ownership and releases them when `Completion` finishes or the process terminates. Leave its reserved `queue.lock` and `queue.process.lock` files alone while it is running.

Lease opens and closes are serialized between SDK workers so concurrent initialization cannot bypass the ownership check. Recovery and payload I/O stay outside that gate, and Unity's main thread never acquires it.

Use `EventClient` directly when you want to own the lifecycle: call `Tick()` every frame, `SetSuspended(true)` on suspension, `SetSuspended(false)` when all suspension reasons clear, and `Dispose()` at shutdown. A real resume schedules an immediate attempt; subsequent failures keep exponential backoff. `Persist()` schedules a save of unattempted memory without suspending; it never makes an active upload replayable. `Flush()` requests an immediate send while respecting retry backoff and suspension. `Dispose()` schedules resource release; `Completion` / `DisposeAsync()` report completion and save failures.
