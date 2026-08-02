# FanControl.MinisforumM1Pro

A small [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin for
the Minisforum M1 Pro. It reads the machine's embedded controller (EC) through
FanControl's PawnIO stack and exposes:

- CPU and system fan RPM;
- CPU and system temperature from onboard EC;
- CPU and system fan controls.

# Install

Download the latest [release](https://github.com/zuyan9/FanControl.MinisforumM1Pro/releases),
start FanControl, then install the plugin under **Settings > Plugins > Install plugin**.
FanControl refreshes its sensors after installation.

## Compatibility

The plugin loads only when both checks pass:

- Windows reports baseboard `ARBSC`;
- the live IT5571 controller profile is `55 71 07 cb`.

It was developed on board revision 1.0, BIOS 1.01, and FanControl V272. Other
Minisforum models are not supported. At initialization, the plugin captures
the current 30 fan base/slope bytes. This preserves whichever firmware fan
mode is active instead of requiring one hard-coded factory curve.

## Control behavior

FanControl percentages are converted to the EC's closed-loop target codes in
100-RPM steps. Native codes 1-6 are unstable on the tested fans, so those
requests produce true off. Code 7 is the first stable running step, near
700 RPM; 100% requests 5100 RPM. The system fan physically tops out near
3800 RPM.

FanControl owns calibration, curves, and start/stop policy. Run calibration
for both paired controls after installation. Expect a sharp transition from
off through 12% to running at 13%.

## Build

Install the .NET 10 SDK and FanControl, then run from this directory:

```powershell
dotnet build -c Release
dotnet run --project .\tests\FanControl.MinisforumM1Pro.Tests.csproj -c Release
```


## Recovery behavior

Disabling a control restores that fan's values captured at initialization. A
normal plugin refresh or FanControl exit restores both fans. Force-terminating
FanControl can leave manual values active. Restart Windows before reopening
FanControl when recovering from a forced termination; firmware startup restores
its configured fan mode.
