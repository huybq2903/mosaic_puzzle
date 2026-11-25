# Databuckets Analytics for Unity — customer guide

[English](USER_GUIDE.md) | [Hướng dẫn tiếng Việt](USER_GUIDE_VI.md)

SDK version: **4.0.1**. Requires Unity **2022.3 or later** and a native player with writable local storage and background-thread support. Validate your exact Unity version and target devices before release. WebGL is not supported.

This guide covers installation, recording events, every public function, configuration limits, and handling errors. [README.md](README.md) contains the detailed event-field and delivery reference.

## 1. Install one package format

Choose **one** of the following. Installing both copies creates duplicate assemblies. Neither package includes the SDK's tests or changes your scenes or Player Settings.

### Import the `.unitypackage`

1. In **Window → Package Manager**, click **+ → Add package by name**. Install `com.unity.nuget.newtonsoft-json`, version `3.2.1`. If your project already uses a compatible version of Unity's Newtonsoft package, keep that version.
2. Choose **Assets → Import Package → Custom Package**, select `DatabucketsAnalytics-4.0.1.unitypackage`, and import all items.
3. Wait for compilation. The SDK and this guide appear under `Assets/DatabucketsAnalytics`.
4. Keep the `Editor` folder: it adds the native bridges and privacy-manifest entry when exporting iOS builds. Keep the `.meta` files when moving the SDK folder.

The asset package does not install Package Manager dependencies automatically. Use Unity's Newtonsoft package rather than adding another `Newtonsoft.Json.dll`. Projects that have removed built-in modules must also enable `com.unity.modules.unitywebrequest` and, for Android, `com.unity.modules.androidjni` at version `1.0.0`.

Unity documents [importing local asset packages](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetPackagesImport.html) and [adding packages by name](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-quick.html).

### Install the Package Manager archive

1. Keep `com.realtimeevents.sdk-4.0.1.tgz` in a stable folder, for example your project's `LocalPackages` folder. Include it in version control if teammates or CI need it.
2. Open **Window → Package Manager → + → Add package from tarball** and select the `.tgz`.
3. Wait for Unity to resolve the declared dependencies and compile. The SDK appears under **Packages → Realtime Events**.

This format declares its Newtonsoft, Android JNI, and UnityWebRequest dependencies, so Package Manager resolves them. See Unity's [tarball installation instructions](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-tarball.html).

If your game scripts use an Assembly Definition with explicit references, add a reference to **RealtimeEvents**. The public convenience API's namespace is **Databuckets.Analytics**; the configurable APIs use **RealtimeEvents**.

### Upgrade or remove

For an asset-package upgrade, close running Play Mode, remove the previous SDK source folder, then import the new package. For a Package Manager installation, replace the package through Package Manager. When switching formats, remove the previous format first. Keep one SDK installation per project.

After updating the SDK for iOS, export to a fresh Xcode project folder and build again so the generated native bridges come from the updated package. The bridges support Objective-C exceptions being disabled; no manual change to Xcode's **Enable Objective-C Exceptions** setting is required.

Removing SDK source does not delete player data under `Application.persistentDataPath`. Keep existing queue files when upgrading. Do not run an older SDK against a newer queue format or delete lock/queue files while a client owns them.

## 2. Start with automatic lifecycle handling

Put this component on a GameObject in your first scene. Replace both placeholders with your service configuration. The endpoint must be an absolute HTTPS URL whose path is exactly `/push`.

```csharp
using System;
using System.Collections.Generic;
using Databuckets.Analytics;
using UnityEngine;

public sealed class DatabucketsBootstrap : MonoBehaviour
{
    private async void Awake()
    {
        try
        {
            DatabucketsTracker.Init("https://events.example.com/push", "YOUR_API_KEY",
                new Dictionary<string, object>
                {
                    ["plan"] = "premium",
                    ["language"] = "en"
                });
            await DatabucketsTracker.Ready;
            DatabucketsTracker.Record("game_started");
        }
        catch (Exception error)
        {
            Debug.LogWarning("Analytics initialization failed: " + error.Message);
        }
    }
}
```

`Init` must run on Unity's main thread. It creates a persistent tracker that survives scene changes, batches events, pumps networking, and handles pause/focus/resume. Repeating `Init` with the same endpoint/key reuses an active tracker. A different configuration while active throws.

You may record after `Init` returns without awaiting `Ready`; accepted commands wait behind initialization. Await `Ready` when you need initialized getters or want to handle startup failure before producing events. `Ready` does not mean events reached the server. Recoverable optional metadata/session errors can still appear in `LastError`.

### Initial properties and call order

Pass properties to `Init` when they must appear on `first_open` and the initial `session_start`. The dictionary is optional; existing `Init(endpoint, apiKey)` calls still work. This example shows which events receive a later update:

```csharp
using System.Collections.Generic;
using Databuckets.Analytics;

DatabucketsTracker.Init("https://events.example.com/push", "YOUR_API_KEY",
    new Dictionary<string, object> { ["plan"] = "premium" });
DatabucketsTracker.Record("game_started"); // plan = premium, even before Ready completes.
DatabucketsTracker.SetUserProperties(("plan", "free"));
DatabucketsTracker.Record("plan_changed"); // plan = free if the update succeeds.
```

Initial properties are captured during the call, merged with saved properties, and saved before startup events are created. Matching saved keys are overwritten; other keys remain. Null or an empty dictionary adds nothing, and a null property value is stored as JSON null. The values survive restarts. SDK-owned fields retain their normal precedence. Do not mutate the dictionary or nested values during capture; changes after `Init` returns do not change the captured values.

Initialization and accepted commands run in worker submission order. An event submitted after `SetUserProperties` uses the updated properties if that update succeeds, even while initialization is still running. Updates cannot change already created events, including startup events. Concurrent callers follow submission order; coordinate calls when one depends on another.

Invalid or oversized initial inputs throw during `Init`. If merging with saved properties exceeds the limit, or saving fails, `Ready` faults before creating new events. Await `Ready` to observe startup failure; it does not wait for property updates submitted afterward. To confirm a later update through the static API, await `PersistAsync()` after submitting it. Lower-level clients also expose `SetUserPropertiesAsync()`.

Repeating `Init` with the same endpoint/key on an active tracker ignores the initial dictionary. Use `SetUserProperties` for later changes. Calling `Init` again does not replay `first_open` or rewrite previously queued events.

## 3. Record events and persistent properties

```csharp
using System.Collections.Generic;
using Databuckets.Analytics;

// Record once at the relevant gameplay action.
DatabucketsTracker.Record("level_completed", new Dictionary<string, object>
{
    ["level"] = 12,
    ["score"] = 9500,
    ["won"] = true
});

// Properties apply to later submitted events once their save succeeds.
DatabucketsTracker.SetUserProperties(new Dictionary<string, object>
{
    ["plan"] = "premium",
    ["player_level"] = 12
});

DatabucketsTracker.SetVariants("new_shop");
DatabucketsTracker.SetSegments("paying_player");
DatabucketsTracker.SetPersonalizations("shop", "offers");
DatabucketsTracker.ResetVariants();
DatabucketsTracker.ResetSegments();
DatabucketsTracker.ResetPersonalizations();
```

Event names must contain non-whitespace text. Omitted or null fields in `Record` mean an empty dictionary. Values can be JSON-compatible primitives, arrays, and nested dictionaries. Fields are captured during the call; later modifications do not change that event. Do not mutate data concurrently during capture, or pass objects with blocking getters or getters that access Unity from callback threads.

Saved properties survive restarts and override matching ordinary event fields. Avoid setting a saved `event_name`, because it overrides custom event names. SDK-owned timing, SDK/platform, and session fields retain SDK-controlled values. Updating properties does not rewrite already accepted events. Setting a property to null stores JSON null; there is no general remove-property function.

On Android and iOS, the SDK collects the preferred native locale as `device.user_default_language`, using lowercase language tags such as `en-us` or `vi-vn`. Startup migrates a saved `device.language` to the new key, preserving an existing new-key value and leaving already queued events unchanged. A migration save/size failure faults `Ready`. The `language` key in the examples is a separate custom player property.

Variants and segments are persistent string arrays named `realtime_variants` and `realtime_segments`. Adds are unique and case-sensitive; duplicate adds and missing removals do nothing. Removing the last value produces JSON null. Values must be non-null strings. Avoid assigning non-string arrays to these keys through `SetUserProperties`.

Initialization, sessions, and lifecycle handling generate `first_open` when its time is known, `session_start`, `session_end`, `app_focus_start`, and `app_focus_end`. Do not manually record those names to reproduce automatic tracking. Events also receive identity, SDK/platform, session, UTC/local-time, engagement, and retention fields. See the [field reference](README.md).

## 4. Which function should I use?

In this table, **any thread** means a game thread or callback thread can submit the call. Call `Init` first. Event/property submission and getter methods require a usable client; status tasks and diagnostics remain readable after shutdown. A void method returning confirms submission, not completion of disk work or delivery.

| `DatabucketsTracker` member | What it does | When to use it | Thread |
| --- | --- | --- | --- |
| `Init(endpoint, apiKey)` / `Init(endpoint, apiKey, initialUserProperties)` | Starts/reuses the automatic tracker. A new tracker saves initial properties before startup events. | Once during startup; again only after a failed/closed client has completed shutdown. | Main |
| `Record(eventName, fields = null)` | Captures and queues one custom event. | At the actual gameplay/ad action; no manual flush is normally needed. | Any |
| `SetUserProperties(dictionary)` / `SetUserProperties((key, value), ...)` | Queues an atomic merge/save of persistent properties for future events. | When player attributes change. | Any |
| `SetVariants(values)` | Replaces the persistent experiment/variant list. | When the player's variants change. | Any |
| `ResetVariants()` | Resets the variant list to null. | When no variants apply. | Any |
| `SetSegments(values)` | Replaces the persistent audience list. | When the player's segments change. | Any |
| `ResetSegments()` | Resets the segment list to null. | When no segments apply. | Any |
| `SetPersonalizations(values)` | Replaces the persistent personalization list. | When the player's personalizations change. | Any |
| `ResetPersonalizations()` | Resets the personalization list to null. | When no personalizations apply. | Any |
| `GetDeviceModel()` | Returns the cached model string; null if unavailable or not loaded. | Enriching UI/diagnostics without querying the device again. | Any |
| `GetUserPseudoId()` | Returns the cached persistent identity; null before loading or if explicitly cleared. | Associating diagnostics with this SDK identity. | Any |
| `GetCountry()` | Returns the first cached/resolved country code, or `"__"`. | Reading the resolved country without another request. | Any |
| `RefreshDeviceProperties()` | Captures current device metadata and queues its update. Resume already refreshes it automatically. | After a foreground language/timezone change you want reflected in future events. | Main |
| `Persist()` | Schedules saving remaining accepted, unattempted events, subject to disk-quota eviction; returns before the save finishes. | An explicit save request when you cannot await completion, such as an ad-click callback before an SDK-controlled redirect. | Any |
| `PersistAsync()` | Returns a task confirming that save; also reports an earlier unreported queued-operation failure. | Await after an important event or before an external handoff your code can delay. | Any |
| `Flush()` | Requests a send after earlier commands, respecting suspension and retry backoff. | When you want an earlier send opportunity than normal batching. It is not a delivery acknowledgement. | Main |
| `Ready` | Task for worker initialization, including the initial property merge/save. Does not wait for later updates. | Await during startup to observe initialization failures. | Read on any |
| `LastError` | Current diagnostic string, or null. | Health monitoring and troubleshooting; not a per-event status. | Read on any |
| `Completion` | Task for eventual shutdown/resource release, including shutdown errors. | Before replacing a failed/closed client. Reading it does not initiate shutdown. | Read on any |
| `FirstOpenTimestamp` | Cached installation/first-open epoch milliseconds; `-1` if unknown, initially `0` while unavailable. | Inspecting the SDK's selected installation time. | Read on any |

`SdkInfo.Version` is a constant in `Databuckets.Analytics`, available even before `Init`. `SdkInfo.VersionNumber` is its integer form with dots removed (`4.0.1` → `401`). Every new event includes these values as the persistent user properties `sdk_ver` and `sdk_ver_num`; caller-supplied values cannot override them.

### When to use `Persist()` and `PersistAsync()`

With `DatabucketsTracker.Init` or `EventTracker.Create`, **ordinary focus loss and pause are handled automatically**. You do not need to call `Persist()` after every event or add your own persistence calls to `OnApplicationFocus` and `OnApplicationPause`.

| Situation | What to do |
| --- | --- |
| Normal foreground gameplay | Call `Record` at the action. Let the tracker handle batching and sending. |
| The player goes to the home screen, switches apps, or follows an ad to an app store | Keep the tracker alive and enabled. On focus loss or pause it suspends networking and schedules saving pending, unattempted events. An extra `Persist()` is normally unnecessary. |
| Your code controls opening a store/browser or showing an ad | If the preceding event needs confirmed local persistence, record it and **await `PersistAsync()` before** invoking the handoff or ad-show API. |
| An ad SDK controls the redirect and only gives you a click callback | Record the click immediately. You can then call `Persist()` to request a save without waiting for a later Unity lifecycle callback. Returning from `Persist()` does not confirm the save. Awaiting inside your callback only delays the redirect if the ad SDK explicitly waits for that callback's task. |
| A reward or revenue callback arrives while the app is out of focus | Record it when the callback actually runs. Events submitted from a background thread, or processed while the client is suspended, are saved by the worker without requiring another Unity frame. A void `Record` call still returns before that work finishes. |
| An important event needs a local save checkpoint while still in focus | Record it, then await `PersistAsync()` and handle failure. Focus loss is not required to use either persistence method. |

The tracker combines pause and focus state: either `OnApplicationPause(true)` or `OnApplicationFocus(false)` suspends it. It resumes sending only after all suspension reasons clear, so a focus-gained callback cannot override an outstanding pause. Returning from the home screen or app store does not require another `Init`, `Persist`, or `Flush`. Callback combinations vary: for example, Android can report Home through pause rather than focus when the keyboard is open. See Unity's [pause callback reference](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.OnApplicationPause.html).

### Recording, saving, and sending are different

For a handoff your code controls, perform it after the awaited save:

```csharp
// On Unity's main thread, in an async method inside your error handling:
DatabucketsTracker.Record("store_open_requested");
await DatabucketsTracker.PersistAsync();
UnityEngine.Application.OpenURL("https://example.com/your-store-page");
```

For a callback whose return cannot wait for storage, an explicit save request looks like this:

```csharp
// Inside the ad SDK's click callback, with your usual error handling:
DatabucketsTracker.Record("ad_clicked");
DatabucketsTracker.Persist(); // Requests a save; does not wait for disk or HTTP.
```

This explicit request is useful for a main-thread callback that arrives before Unity reports focus loss or pause. A background-thread callback already requests persistence as part of recording. Neither approach guarantees completion before an immediate SDK-controlled redirect or OS suspension. A save before showing an ad covers earlier events; later click/reward callbacks must still record their own events.

Both persistence methods work while focused or suspended, provided the client is usable and the process can execute. They do not change focus state or resume networking. `PersistAsync` covers remaining earlier accepted, unattempted events, subject to disk-quota eviction; it does not cover future callbacks or prevent later eviction. It does not wait for HTTP or make an uncertain in-flight upload replayable. `Flush()` requests sending and is not a substitute for confirming a local save. User-property, variant, and segment updates already queue their own saves; they do not require `Persist()` to become persistent.

Do not use `.Wait()`, `.Result`, or thread joins on Unity's main thread. If a barrier fails after an event was accepted, retry **persistence**, not `Record`, which would create another event. The first successful save can still report an earlier operation failure; inspect the exception, and a later barrier can confirm subsequent recovery.

An OS pause/kill can stop execution before asynchronous persistence finishes; an `await` in a lifecycle callback does not make the OS wait. Request saves at important actions while the app is running when this matters. Do not rely on `OnApplicationQuit` as the final save opportunity: Unity documents that mobile apps can be terminated while suspended without that callback. See Unity's [quit callback reference](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.OnApplicationQuit.html). Keep the automatic tracker's GameObject alive and enabled through ads and app-store visits.

## 5. Configure limits while keeping automatic lifecycle handling

`DatabucketsTracker.Init` uses default limits and accepts an optional initial-property dictionary. To customize limits or storage, create and retain an **`EventTracker` instance** instead. Set `EventOptions.InitialUserProperties` to include properties on startup events. Use that instance's methods for all events; it does not initialize the static `DatabucketsTracker` facade.

```csharp
using System;
using System.Collections.Generic;
using RealtimeEvents;
using UnityEngine;

public sealed class CustomAnalyticsBootstrap : MonoBehaviour
{
    public static EventTracker Analytics { get; private set; }

    private async void Awake()
    {
        try
        {
            if (Analytics != null) return;
            Analytics = EventTracker.Create(new EventOptions
            {
                Endpoint = "https://events.example.com/push",
                ApiKey = "YOUR_API_KEY",
                InitialUserProperties = new Dictionary<string, object> { ["plan"] = "premium" },
                MaxBatchEvents = 25,
                MaxBatchBytes = 32 * 1024,
                MaxPendingOperations = 128,
                MaxPendingBytes = 256 * 1024,
                MaxUserPropertiesBytes = 8 * 1024,
                RequestTimeoutSeconds = 30
            });
            await Analytics.Ready;
            Analytics.Track("game_started");
        }
        catch (Exception error)
        {
            Debug.LogWarning("Analytics initialization failed: " + error.Message);
        }
    }
}
```

Use either this bootstrap or the default bootstrap, once in the first scene. For later calls use `CustomAnalyticsBootstrap.Analytics.Track(...)`. The smaller limits are an example to measure on your target devices; they reject bursts sooner. For an existing installation, do not reduce the property/file limits below its saved backlog without a recovery plan.

`EventTracker` exposes the same property, array, getter, persistence, refresh, status, and flush members listed above. Its event method is `Track`, with dictionary or tuple overloads, with or without a name. Unlike static `Record`, lower-level dictionary overloads reject null dictionaries. Use `tracker.Track("event_name")` when no extra fields are needed.

`EventTracker.Create(options)` must run on the main thread. It creates its own persistent GameObject; do not add an uninitialized `EventTracker` component yourself. To close a manually retained tracker, capture its `Completion`, destroy its GameObject on the main thread, then await the captured task in an owner that remains alive. Disabling only suspends it and retains its storage lease.

### Every configurable setting and limit

Set options **before** creating the tracker/client. Options are copied at construction; changing the original object later has no effect. Byte counts are UTF-8 payload bytes, not total heap usage or total filesystem space. All numeric options below are mandatory positive values; there is no zero/unlimited mode.

| Setting | Type / default | Allowed value | Effect and when to change it |
| --- | --- | --- | --- |
| `Endpoint` | `string`, required | Absolute HTTPS URL; path exactly `/push`; no URL credentials or fragment; query parameters allowed. | Destination for POSTs. The SDK sets `resolve_country=true`. |
| `ApiKey` | `string`, null in lower-level options | Null omits the header in lower-level APIs; otherwise nonblank printable ASCII. Static `Init` requires a non-null key. | `X-API-Key` header. Supply the key required by your service. |
| `StorageDirectory` | `string`, null | Null selects an endpoint-specific directory under `Application.persistentDataPath`; otherwise a usable local path with file locking. Relative paths resolve at construction. | Use a dedicated persistent directory to isolate destinations/accounts. Only one client/process may own it. |
| `InitialUserProperties` | `IDictionary<string, object>`, null | Null or a dictionary of JSON-compatible values within `MaxUserPropertiesBytes`, including the merged saved snapshot. | Captured at construction and merged/saved before startup events. Use for properties that must appear on `first_open` and the initial `session_start`. |
| `MaxBatchEvents` | `int`, **100** | `1`–`2,147,483,647` | Count limit for new buffered batches. Smaller batches send sooner and increase request/file overhead. |
| `MaxBatchBytes` | `int`, **262,144** (256 KiB) | `3`–`1,048,576` (1 MiB) | Maximum new event/batch bytes, including SDK fields and each trailing newline. Must fit real automatic events; the numeric minimum is not a practical setting. |
| `MaxDiskBytes` | `long`, **67,108,864** (64 MiB) | At least `MaxBatchBytes`; at most `9,223,372,036,854,775,807` | Quota for queued payload, including claimed files and pending first-open data. Oldest pending batches are evicted to fit new saves; in-flight files are protected. Metadata/filesystem overhead is additional. Increase to retain larger offline backlogs. |
| `MaxDiskFiles` | `int`, **4,096** | `1`–`65,536` | Maximum queued batch files, including in-flight files; also bounds recovered queue entries. Full queues evict oldest pending batches for new saves; over-limit recovered queues still fail initialization with files preserved. |
| `MaxUserPropertiesBytes` | `int`, **65,536** (64 KiB) | `2`–`1,048,576` (1 MiB) | Maximum merged saved property snapshot, including automatic properties and variant/segment arrays. Also bounds reading an existing property file. Actual startup properties need more than the numeric minimum. |
| `MaxPendingOperations` | `int`, **1,024** | `1`–`2,147,483,647` | Maximum queued plus executing worker commands. Also caps retained focus transitions during storage failure. Capacity exhaustion rejects work; lifecycle overflow closes the client after attempting persistence. |
| `MaxPendingBytes` | `int`, **4,194,304** (4 MiB) | At least `MaxBatchBytes`; at most `2,147,483,647` | Budget for queued/executing serialized inputs. Include your largest property update when choosing it; tasks, strings, metadata, and native copies consume extra memory. |
| `FlushIntervalSeconds` | `double`, **1** | Any finite value `> 0` | Deadline starts with the first buffered event. On a tick, a ready batch can send or be persisted while older work is pending. Also controls coalesced session checkpoints. |
| `InitialRetrySeconds` | `double`, **2** | Any finite value `> 0`, no greater than `MaxRetrySeconds` | Initial delay for retryable network/storage/metadata failures. Delays grow exponentially; affected operations use separate deadlines. |
| `MaxRetrySeconds` | `double`, **60** | Any finite value at least `InitialRetrySeconds` | Upper bound on retry delay, not the number of retries or total retry duration. |
| `RequestTimeoutSeconds` | `int`, **15** | `1`–`2,147,483,647` | Native timeout and SDK deadline from native send start. Main-thread pumping enforces the deadline, followed by at most one second for cancellation to publish its final status. Uncertain timeouts are retired. |

Pending batches have no age-based expiry. When a new save reaches `MaxDiskBytes` or `MaxDiskFiles`, the oldest pending batches are deleted until it fits. In-flight files are protected, and an older reserved retry cannot evict newer saved batches. First-open and session events can also be evicted; their timestamp/session metadata remains cached. Delivery uses LIFO batch order: the newest ready memory batch or newest eligible saved batch sends first. Events inside a batch retain their original order and bytes. An in-flight request finishes before another batch starts; an older retry retained in memory after a failed save remains active until it can be resolved. Continuous new traffic can delay older batches. Automatic first-open/session events may arrive after more recent events; use `_ts` and session fields to interpret event time. `PersistAsync()` saves the remaining accepted events subject to this policy; it does not protect them from later eviction.

Invalid configurations throw before startup. Very large legal values are not recommendations for old devices. Event size includes saved properties and automatically generated fields. Disk quotas do not guarantee that the device has that much free space. Existing backlog above a lowered byte quota may drain; a lowered file-count/property limit can prevent initialization. Recovered older batches can be up to the supported 1 MiB even after lowering the new-batch limit.

The default storage namespace uses the endpoint, **not the API key**. Key rotation for the same destination retains its backlog. Different accounts sharing an endpoint need different explicit storage directories; otherwise pending events can be sent using the next client's key.

### Fixed limits and policies you cannot configure

- Input capture accepts at most **1,024 top-level fields per call** and **32 nested containers**. These are separate from the configurable byte budgets.
- The supported event/property size ceiling is **1 MiB**. Final event bytes include SDK enrichment and newlines.
- Session inactivity timeout is **30 minutes**. Installation timestamp selection uses the existing **48-hour** rule; unknown iOS installation time remains `-1`.
- There is **one active HTTP request per client**, with redirects disabled and certificate validation enabled.
- Country response capture accepts at most **64 bytes**; larger responses do not establish a country. The native receive buffer is 4 KiB.
- Retry eligibility is fixed: definite pre-send failures, completed zero-byte connection failures, and HTTP **503**. Other HTTP statuses, including 429/500, and uncertain uploads are retired.
- No retry-attempt limit, event expiry, retry jitter, `Retry-After` handling, automatic event-ID generation, or per-event delivery task is available.
- Automatic event/metadata collection has no public disable switch. There is no general property-delete or queue-clear API.

## 6. Advanced: own the client and lifecycle

Use `new EventClient(options)` only when your integration already owns the game loop and lifecycle. `EventTracker` is easier for normal games. Create the client on Unity's main thread and keep that same thread for all networking/lifecycle calls.

| `EventClient` member | What it does / when to use it | Thread |
| --- | --- | --- |
| Constructor `EventClient(options)` | Starts asynchronous initialization with your copied settings; captures `InitialUserProperties` before starting the worker and saves them before startup events. | Main |
| `Track(...)` | Captures/submits an event; accepts dictionaries or `(key, value)` tuples, with or without a name. Unnamed overloads do not add an event name. | Any |
| `TrackAsync(...)` | Same overloads; its task completes after processor acceptance. Foreground events may still be in memory; background/suspended acceptance includes persistence. | Any |
| `SetUserProperties(...)` / `SetUserPropertiesAsync(...)` | Merge future-event properties. Async completion confirms the update was saved and published. | Any |
| `AddVariant` / `RemoveVariant` / `AddSegment` / `RemoveSegment` | Add/remove individual variant or segment labels; adds skip duplicates and removing the last label resets to null. | Any |
| `AddVariantAsync` / `RemoveVariantAsync` / `AddSegmentAsync` / `RemoveSegmentAsync` | Await the corresponding save, or successful no-op if nothing changed. | Any |
| `GetDeviceModel()` / `GetUserPseudoId()` / `GetCountry()` | Read cached values without device/network access. | Any |
| `Persist()` / `PersistAsync()` | Schedule/confirm saving remaining accepted, unattempted events, subject to disk-quota eviction. | Any |
| `Flush()` | Request an early send; continue pumping `Tick`. | Main |
| `Tick()` | Pump native HTTP and schedule a coalesced worker tick. Call every frame, including while suspended if your loop continues. Uses time independent of `Time.timeScale`. | Main |
| `SetSuspended(true)` | Stop native starts, cancel/resolve the current attempt, and schedule persistence. | Main |
| `SetSuspended(false)` | Resume after all pause/focus/disable reasons clear; refresh metadata and schedule a fresh send opportunity. | Main |
| `RefreshDeviceProperties()` | Queue an explicit refresh; real resumes already refresh. | Main |
| `Dispose()` | Stop networking and schedule final persistence/resource release. Returns without joining the worker. | Main |
| `DisposeAsync()` | Initiate the same shutdown and return `Completion` so you can await it. | Invoke on main |
| `CheckUsable(mainThread = true)` | Throws if disposed, or if the default main-thread check fails. Usually unnecessary; SDK methods perform their own checks. | Main by default; any with `false` |
| `Ready`, `LastError`, `Completion`, `FirstOpenTimestamp` | Same status meanings as the automatic tracker. | Read on any |
| `IsSuspended` | Current client suspension/closed state. | Read on any |
| `IsSending` | Whether a native request is active; a worker/mailbox request may still be waiting to start. | Read on main |
| `BufferedEventCount` | Last published count of events in the processor's current memory buffer. Excludes worker commands, disk backlog, and the active upload. | Read on any |

Combine pause, focus loss, disabled state, and quit before calling `SetSuspended`; one resume callback must not override another active suspension reason. Stop calling the disposed client. Await `Completion` before creating another client using the same directory.

There is no public method that waits until every event has reached the server. `TrackAsync`, `PersistAsync`, and `Completion` each confirm a different local stage.

## 7. Delivery and failure handling

The SDK preserves pending events across restarts when their saves succeed. An HTTP 2xx acknowledges the entire batch. Retries preserve its exact bytes, timestamps, and properties. Failed deletion of an acknowledged batch retries cleanup without sending it again.

An observed HTTP 503 remains retryable even when its response body or cancellation stalls. An observed HTTP 410 disables recording and sending immediately. The worker saves an `sdk-disabled` marker in the storage directory and retries failed persistence with backoff. Once saved, the marker keeps the SDK disabled after restart; resume, flush, or another `Init` does not re-enable it. Later event/property submissions are ignored. Already accepted, unattempted memory is saved during disablement, but the backlog stays unsent. `LastError` reports the disablement or any persistence failure.

An uncertain upload is retired to avoid replay; this can lose events. HTTP 503 is an explicit retry exception and can duplicate events if the service stored them before responding. Strict duplicate prevention needs your service's deduplication contract and stable IDs, or a guarantee that 503 means nothing was stored. Retries retain backoff, but newer eligible queued batches take priority over older saved retries. Continuous new traffic can delay older batches indefinitely.

Treat analytics failures as recoverable application errors. Catch synchronous input/capacity errors and exceptions from awaited SDK tasks. Monitor `LastError` without logging it every frame. A failure can refer to a previous void call; a later successful call does not prove that failed event was accepted.

| Symptom | What to check or do |
| --- | --- |
| `Newtonsoft.Json` / `JObject` compilation errors | Install Unity's `com.unity.nuget.newtonsoft-json` package and remove conflicting manually bundled copies. |
| Duplicate `RealtimeEvents` assemblies/types | Remove the second SDK installation; choose Assets import or Package Manager. |
| `Ready` faults | Inspect the exception and `LastError`. Check writable storage, exclusive directory ownership, limits, and saved-record integrity. Await faulted `Completion` inside error handling; fix the cause before recreating. |
| An event is too large | Reduce fields/properties or raise `MaxBatchBytes` within 1 MiB. Include automatic fields in the budget. |
| Pending work is full | Reduce event bursts or increase worker budgets within available memory. Submission was rejected; do not block the main thread waiting for space. |
| Disk queue is full | No eligible pending batch can free enough quota (in-flight files and newer data are protected from older retries). Let networking drain it or increase quotas. Storage I/O failures still require working/free device storage. Retry persistence for already accepted memory. |
| No sends occur | Check initialization, `LastError`, current suspension, connectivity, credentials, retry delay, and whether the tracker is enabled or your custom client calls `Tick`. |
| Client closes after lifecycle overload | Handle `Completion`, allow storage to recover, and create a new client. The static API can be initialized again after completion. |
| iOS bridge export fails | Keep the SDK's Editor/native files and `.meta` files; install iOS Build Support for your Unity editor. |

A permanently corrupt committed cache requires diagnosis; the SDK preserves conflicting records and does not guess which copy is safe to replay. Deleting its data can reset identity, installation/session state, and pending events, so do not use deletion as a normal retry strategy.

## 8. Older devices and release checks

Keep events small and record gameplay actions rather than every frame. Serialization runs on the calling thread; filesystem work runs on a dedicated worker. Bounded queues constrain payload growth, but configured byte budgets exclude dictionaries, tasks, strings, native request copies, and filesystem overhead.

On the oldest devices you support, test a release/IL2CPP build with an offline backlog, slow network/storage, repeated ads and app-store visits, and process termination. Check frame time and memory peaks. Use your production server's certificate chain: an Editor HTTPS test does not establish an older phone's trust-store compatibility.

The SDK is validated here with Unity 2022.3.62f3. Minimum OS support also depends on your exact engine patch: Unity documents iOS 13 as the minimum from 2022.3.72f1, despite some settings still displaying iOS 12. See the [Unity release note](https://unity.com/releases/editor/whats-new/2022.3.74f1). Managed tests and C# compilation do not replace native device testing.
