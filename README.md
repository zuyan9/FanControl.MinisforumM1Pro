# FanControl.MinisforumM1Pro

A small [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin for
the Minisforum M1 Pro and M1 Lite. It reads the embedded controller (EC)
through FanControl's PawnIO stack and exposes:

- CPU and system fan RPM;
- CPU and system policy temperature from the EC;
- paired CPU and system fan controls.

> [!TIP]
> Checkout [FanControl.MinisforumUM](https://github.com/zuyan9/FanControl.MinisforumUM)
> for fan control on UM series, such as UM780 and UM690.

<img width="400" alt="M1 Pro FanControl sensors" src="https://github.com/user-attachments/assets/f02dcb36-28c6-4523-bc50-71c186b99fe3" />
<img width="300" alt="M1 Pro FanControl controls" src="https://github.com/user-attachments/assets/6e94fff1-f59f-429b-8265-fdc747283ac9" />

## Install

Download the latest plugin
[release](https://github.com/zuyan9/FanControl.MinisforumM1Pro/releases),
start FanControl, then install the DLL under **Settings > Plugins > Install
plugin...**. FanControl refreshes its sensors after installation.

Do not run another EC-writing or fan-control utility at the same time.

## Compatibility and evidence

| Model | Required board | Firmware/runtime gate | Status |
|---|---|---|---|
| M1 Pro | `ARBSC` | outer/native controller `55 71 07`, exact `0x200d = 0xcb`, and one of two exact known stock curve tables | Hardware-validated on board revision 1.0, BIOS 1.01 |
| M1 Lite | `MTBSI` | exact EC 0.02 build identity, read/write controller mode, and one of two exact stock curve tables | Derived from BIOS/EC analysis; no live hardware validation |

The internal curve selector maps `0xb0` and `0xb2` to the normal table and
`0xb1` to the alternate table. These values are not named BIOS power profiles.

M1 Lite support is deliberately experimental. Static analysis proves that its
EC firmware implements the same two-channel curve engine, control addresses,
target format, RPM mirrors, and temperature inputs as M1 Pro. It does not prove
the live Super-I/O slot, controller revision, motor floor, or physical maximum.
The plugin discovers the slot conservatively and refuses all curve access
unless exactly one controller and the full firmware fingerprint match.

See [M1 Lite BIOS, firmware, and EC research](docs/m1-lite-firmware.md) for the
evidence, byte maps, transport analysis, and remaining limitations.

## Control behavior

FanControl percentages map to the EC's closed-loop target codes in 100-RPM
steps. The two models intentionally use different policies:

| Model/channel | Applied native codes | FanControl behavior |
|---|---:|---|
| M1 Pro CPU and system | `0`, `7..51` | Preserves the validated behavior: requests 1–6 apply off; the UI retains the requested quantized percentage |
| M1 Lite CPU and system | `0..51` | Direct linear control with no plugin-imposed running floor; FanControl calibration can determine each fan's usable range |

The M1 Lite stock tables use CPU `0` or `18..51` and System `10..41`, but those
are OEM policy defaults rather than measured motor limits. The plugin therefore
exposes the complete `0..51` FanControl scale for both channels, including
System off. CPU codes `1..17`, System codes `0..9`, and System codes `42..51`
remain hardware-unvalidated; raw byte values above 51 are not exposed.

At startup the plugin captures and verifies the full 64-byte curve block.
Manual control owns, modifies, and restores only its 30 base/slope bytes. It
writes slopes before bases, verifies the full block after every transaction,
and restores bases before slopes. Ownership checks, writes, readbacks, and any
bounded recovery all run under one ISA mutex acquisition. Any unresolved mode,
threshold, owned-byte, identity, or readback mismatch stops further writes.

Manual control replaces the OEM temperature-dependent curve, including its
high-temperature fan escalation. If you want a guard, configure a curve from a
reliable CPU-package sensor and combine it with your regular curve using
FanControl's Max Mix.

Run FanControl calibration for both paired controls after installation. For
M1 Lite, calibrate one channel at a time with direct temperature and acoustic
observation because its low-speed, stop/start, and upper-range behavior has not
yet been validated on hardware.

## Build and test

Install the .NET 10 SDK and FanControl, then run from this directory:

```powershell
dotnet build -c Release
dotnet run --project .\tests\FanControl.MinisforumM1Pro.Tests.csproj -c Release
```

The console harness is hardware-free. It covers both profiles, controller-slot
discovery and native port sequencing, all startup gates and hysteresis boundary
samples, policy bounds, model metadata, write ordering, readback, ownership
drift, verified recovery, and failure containment.

## Recovery behavior

Disabling a control restores that fan's captured startup values. A normal
plugin refresh or FanControl exit restores both modified channels. Closing the
plugin without ever enabling a control performs no EC data write.

Any Set or Reset failure closes the backend and attempts a verified full
baseline restoration. The plugin logs the failure instead of throwing it to
FanControl, which would otherwise stop updating every other fan and sensor.
The plugin's controls then stay idle until FanControl is refreshed. An invalid
command, such as NaN from a broken curve, returns that fan to the firmware
curve. If restoration fails, restart Windows before the plugin can be
initialized again.

Force-terminating FanControl can leave manual values active. Restart Windows
before reopening FanControl after a forced termination. M1 Lite startup also
requires an exact stock curve, so it intentionally refuses a stale manual
curve until firmware startup has restored it. M1 Pro also requires one of the
two known stock tables, preventing a partial crash-left curve from becoming a
new baseline.
