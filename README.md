# FanControl.MinisforumM1Pro

A small [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin for
the Minisforum M1 Pro. It reads the machine's embedded controller (EC) through
FanControl's PawnIO stack and exposes:

- CPU and system fan RPM;
- EC CPU and system temperatures;
- independent, paired CPU and system fan controls.

## Compatibility

The plugin loads only when all of these checks pass:

- manufacturer `Micro Computer (HK) Tech Limited`;
- product `AI Series` and board `ARBSC`;
- live IT5571 signature `55 71 07 cb`;
- the complete expected stock fan curve.

It was developed on board revision 1.0, BIOS 1.01, and FanControl V272. Other
Minisforum models are not supported.

## Control behavior

FanControl percentages are converted to the EC's closed-loop target codes in
100-RPM steps. Native codes 1-6 are unstable on the tested fans, so those
requests produce true off. Code 7 is the first stable running step, near
700 RPM; 100% requests 5100 RPM. The system fan physically tops out near
3800 RPM.

FanControl owns calibration, curves, and start/stop policy. Run calibration
for both paired controls after installation. Expect a sharp transition from
off through 12% to running at 13%.

## Build and install

Install the .NET 10 SDK and FanControl, then run from this directory:

```powershell
dotnet build -c Release
dotnet run --project .\tests\FanControl.MinisforumM1Pro.Tests.csproj -c Release
```

Close FanControl and install
`bin\Release\net10.0-windows\FanControl.MinisforumM1Pro.dll` using
**Settings > Plugins > Install plugin**, or copy it into FanControl's
`Plugins` directory. Start FanControl as administrator and refresh sensors.

## Recovery behavior

Disabling a control restores that fan's stock automatic curve. A normal plugin
refresh or FanControl exit restores both fans. Force-terminating FanControl can
leave manual control active; if the plugin refuses to reload because the curve
is not stock, reboot before trying again.
