# M1 Lite BIOS, firmware, and EC research

This note records the evidence used for the experimental `MTBSI` profile. The
analysis was completed without M1 Lite hardware. Firmware was parsed and
disassembled offline; no vendor flasher, EFI program, privileged experiment,
or recovered firmware payload was executed.

## Result and confidence boundary

The M1 Lite EC contains the same two complete fan-policy channels as the M1
Pro: CPU and system curve tables, CPU and system targets, two tachometer paths,
and two policy temperatures. The initializer and RPM-mirror routines are
semantically identical after accounting for relocated calls.

That is strong evidence for an M1 Lite plugin profile, but it is not live
validation. The following remain unknown until hardware is tested:

- physical Super-I/O configuration port and live controller revision;
- response of both motors to manual curve values;
- minimum stable code, stop/start behavior, and physical maximum RPM;
- recovery across sleep, hibernation, firmware mode changes, and abrupt exit;
- the physical source behind the second policy temperature.

The user-facing sensor is therefore called **System Temperature**, not SSD or
memory temperature.

## Public hardware identity and cooling evidence

Public Geekbench results for Core Ultra 5 125U systems identify the exact
baseboard as `Meigao Innovation Technology (Shen Zhen) Co., Ltd MTBSI`, with
system model `Micro Computer (HK) Tech Limited EliteMini Series`. `MTBSI` is
the useful exact gate; `EliteMini Series` is shared and too broad.

Minisforum's product specification names a CPU blower and rates it at 5000
RPM. Its official exploded view and installation video also show a separate,
wired lower radial assembly over the SO-DIMMs/M.2 area. That visual evidence,
together with the two independent firmware tach/control channels, supports
exposing CPU and System fans. Minisforum's M1 Lite prose does not separately
name or rate the lower fan.

Sources:

- [Minisforum M1 Lite product page](https://www.minisforum.com/products/m1-lite-125u)
- [Official exploded-view image](https://www.minisforum.com/cdn/shop/files/08_fefa0973-6798-4c19-9ddc-63b5d9d48723.png?v=1775540867&width=3840)
- [Official M1 Lite installation video](https://s3.us-west-1.amazonaws.com/pc.video.file/M1+Lite+Install.mp4)
- [Public Geekbench MTBSI result](https://browser.geekbench.com/v6/compute/6189164)

## Official BIOS provenance

The current Minisforum support catalog links `MTBSI_1.01_20260210.zip` as M1
Lite BIOS 1.01. The archive used here was retrieved from that official link.

| Artifact | Value |
|---|---|
| Published label | BIOS 1.01 |
| Official archive | `MTBSI_1.01_20260210.zip` |
| Archive size | 15,569,614 bytes |
| Archive SHA-256 | `23c2f089fa0a7089ee4f43aaa72098a325551a5599d7a8ac70faf2af4ac23616` |
| Host image | `MTBSI.bin`, 33,554,432 bytes |
| Host-image SHA-256 | `afcf3987159e37d273c1dce22dd4d650059d0576eb881f2f966520ca70c64193` |

The release note identifies BIOS 1.01 / EC 0.02, dated 2026-02-10, and says
the release updates the EC to 0.02. Earlier entries list BIOS 1.00 / EC 0.01
and BIOS 0.01 / EC 0.00.

Sources:

- [Minisforum downloads catalog](https://www.minisforum.com/pages/product-info)
- [Official BIOS 1.01 archive](https://pc-file.s3.us-west-1.amazonaws.com/M1+Lite/BIOS/MTBSI_1.01_20260210.zip)

## Recovered EC candidate

A high-confidence 128-KiB ITE/8051 candidate begins in `MTBSI.bin` at host
offset `0x1580000`.

| Field | Value |
|---|---|
| Length | `0x20000` / 131,072 bytes |
| SHA-256 | `bb49349df94a9bc89a0fbc086ccc9d004c3ffc2d79db48e61e315f6b3316ad50` |
| Reset vector | `02 00 70` at `0x0000` |
| Header | `ITE EC-V14.6` at `0x0050` |
| Project | `INTEL MTBSI` at `0x7f80` |
| Build | `2026/02/03$` and `16:38:01$` |
| Internal version | `VER:0.02.00` |

The reset vector, header, aligned start, upper-half code/data, project string,
and package provenance strongly establish the candidate start. The 128-KiB
endpoint is a fixed-format inference, not an independently declared flash
descriptor region. The exact EC silicon is not encoded by these strings.

The M1 Pro comparison object is a different image: SHA-256
`c3a5bf37f8d4feaff83d79614fbdeb5c762e4b63e150c60051b9175900333f08`,
project `INTEL ADL UP3`, version `1.00.00`. The two 128-KiB objects differ at
63,611 byte positions, so M1 Lite is not merely a renamed M1 Pro binary.

## Shared fan-policy ABI

The M1 Lite initializer at EC file offsets `0x11e50..0x11f5b` and the M1 Pro
initializer at `0x13218..0x13323` are each 268 bytes. They differ in 42 bytes,
all of which are the operands of 21 relocated `LCALL`/`LJMP` instructions.
After masking those relocation operands, both hash to:

```text
388440a2727a61ba6420506e45a28754dd5b066575981484639d9b854783afa6
```

The policy literals, XRAM destinations, branching, and row geometry match.

| Meaning | XRAM address and format |
|---|---|
| CPU target | `0x0420..0x0421`, big-endian RPM |
| System target | `0x0422..0x0423`, big-endian RPM |
| CPU/System policy temperature | `0x0428`, `0x0429`, unsigned bytes |
| Calculated CPU/System code | `0x0432`, `0x0433` |
| CPU primary RPM mirror | `0x0438..0x0439`, big-endian |
| System primary RPM mirror | `0x043a..0x043b`, big-endian |
| CPU curve rows | `0x0640..0x0657`, 8 × `(base, upper, lower)` |
| System curve rows | `0x0658..0x066c`, 7 × `(base, upper, lower)` |
| Unused system row | `0x066d..0x066f` |
| CPU slopes | `0x0670..0x0677` |
| System slopes | `0x0678..0x067e` |
| Unused system slope | `0x067f` |
| CPU/System raw tach | `0x181e..0x1821`, two big-endian counters |

The target writer multiplies the selected one-byte code by 100 and stores it
big-endian. The tach routine computes:

```text
rpm = raw_counter == 0 ? 0 : floor(2156250 / raw_counter)
```

The policy engine is stateful. While heating it advances a row only when the
temperature is strictly above that row's upper threshold; while cooling it
retreats only when the temperature is strictly below the lower threshold.
Either adjacent row can therefore be valid at a shared boundary, depending on
history. During startup the plugin accepts the union of all rows whose
inclusive hysteresis band contains the sampled temperature (with the terminal
row retained above its upper threshold). It requires two identical samples
where the final codes at `0x0432/0x0433` match one of those possible row
outputs, are inside the profile bounds, and exactly satisfy:

```text
BE16(0x0420) == XRAM[0x0432] * 100
BE16(0x0422) == XRAM[0x0433] * 100
```

The plugin controls only 30 bytes: 15 active row bases and 15 slopes. It does
not write thresholds, targets, tach counters, temperatures, duty internals, or
the unused eighth system row.

## Exact stock curves

Mode byte `0x032e` has three statically observed values. `0xb0` and `0xb2`
share the normal table; `0xb1` selects the alternate table. Their relationship
to user-facing BIOS power-profile names is not established, so the plugin uses
only the neutral normal and alternate labels.

Each tuple is `(base code, upper temperature, lower temperature, slope)`.

Normal (`0xb0` or `0xb2`):

```text
CPU
(0,25,0,0) (18,45,25,20) (22,54,45,66) (28,66,54,41)
(33,80,66,28) (37,92,80,25) (40,96,92,19) (51,100,96,0)

System
(10,25,0,0) (17,60,25,17) (23,91,60,58)
(41,100,90,0) (41,100,90,0) (41,100,90,0) (41,100,90,0)
```

Full `0x0640..0x067f` SHA-256:
`82938edc4142056f6fb9f55f7183caec656ac4e699242a617f94230c28c70c38`.

Alternate (`0xb1`):

```text
CPU
(0,25,0,0) (18,45,25,20) (22,54,45,33) (25,72,54,16)
(28,80,72,50) (32,89,80,22) (34,93,89,169) (51,100,93,0)

System
(10,25,0,0) (16,60,25,17) (22,91,60,61)
(41,100,90,0) (41,100,90,0) (41,100,90,0) (41,100,90,0)
```

Full-block SHA-256:
`e62d75c1b13099b60d206be32a11189ee89dfab8cef9f8d6bd82acde50c1c7cb`.
The slope `169` is the literal byte `0xa9`.

The stock tables establish OEM policy envelopes, not physical limits:

| Channel | Stock off | Minimum nonzero code | Maximum code |
|---|---:|---:|---:|
| CPU | `0` | `18` | `51` |
| System | not present | `10` | `41` |

The plugin does not treat those defaults as manual-control floors. It exposes
native codes `0..51` linearly for both fans so FanControl calibration can find
their actual usable floors, stop/start behavior, and saturation points. CPU
codes `1..17`, System codes `0..9`, and System codes `42..51` are outside the
observed stock policies and remain hardware-unvalidated. Although the curve
base is a byte, static analysis provides no physical justification for values
above the shared code-51 full-scale, so raw codes `52..255` remain rejected.

## Runtime EC identity

The EC copies its build identity into XRAM. The M1 Lite profile requires:

```text
0x0200..0x0205 = 26 02 03 16 38 01
0x0210..0x021a = 32 30 32 36 2f 30 32 2f 30 33 24  # "2026/02/03$"
0x0220..0x0228 = 31 36 3a 33 38 3a 30 31 24        # "16:38:01$"
```

The terminators are literal dollar bytes (`0x24`), not NULs.

Both the M1 Lite and M1 Pro images contain code that ORs `0xc8` into native
register `0x200d`; that does not prove the complete runtime byte. ITE's
published register documentation defines bits 1:0 as I2EC disabled/read-only/
read-write mode. The plugin therefore combines the firmware bits with a
required read/write mode and checks:

```text
(XRAM[0x200d] & 0xcb) == 0xcb
```

Bit 2 remains ignored because it is reserved. The plugin does not guess an
exact M1 Lite value or revision.

## Transport analysis

The M1 Lite ACPI tables define the standard `PNP0C09` EC at data/status ports
`0x62/0x66` and an auxiliary mailbox at `0x68/0x6c`. The complete `H_EC`
definition is text-identical to M1 Pro's. All extracted DSDT and SSDT bodies
were checked; the shipped AML defines the auxiliary methods but does not call
them.

EC firmware does implement fixed mailbox commands:

- `D5/16`, then `D5/17`: system RPM low byte, then cached high byte;
- `D5/18`, then `D5/19`: CPU RPM low byte, then cached high byte;
- `DD/20` and `DD/21`: CPU and system policy temperatures;
- `DD/22`: a third temperature;
- `DE/01..03`: set OEM selector `0xb0`, `0xb1`, or `0xb2`;
- `DE/04`: read the selector.

There is no mailbox command for arbitrary `0x0640..0x067e` curve access. Raw
`0x62/0x66` access would also conflict with the Windows ACPI EC driver, while
user mode cannot acquire the AML-only mailbox mutex. The auxiliary mailbox is
therefore unsuitable for this manual-control plugin.

The native path used successfully on M1 Pro is the compatible candidate:

```text
outer PNP pair:        0x2e/0x2f or 0x4e/0x4f
nested D2 index/data:  0x2e/0x2f
I2EC_ADDR_L:           nested offset 0x10
I2EC_ADDR_H:           nested offset 0x11
I2EC_DATA:             nested offset 0x12
```

ITE's published specification documents the depth-2 registers and a native
base-selector register, but the MTBSI image does not reveal the live selector.
The plugin consequently does not assume M1 Pro's outer `0x4e/0x4f` pair.

The embedded LibreHardwareMonitor PawnIO `LpcIO` module maps slot 0 to physical
`0x2e/0x2f` and slot 1 to `0x4e/0x4f`. Probing an unidentified slot through
the nested registers could mutate an unrelated controller, so discovery is
strictly two-stage:

1. Require exact Windows baseboard `MTBSI` before creating a transport.
2. For slots 1 and 0, read only outer PNP identity registers `0x20..0x22`.
3. Require exactly one product `55 71` with a nonzero, non-`0xff` revision.
4. Only on that slot, enter depth-2 access and require native `0x2000..0x2002`
   to equal the outer identity.
5. Require the build sentinels, read/write `0x200d` mask, two identical coherent
   target/code samples, plausible telemetry, and two identical exact stock-curve
   snapshots.

Zero or multiple outer candidates fail before any nested access. The M1 Pro
profile remains pinned to slot 1 and controller revision `07`.

The outer `55 71` identity and equality with native `0x2000..0x2002` are
conservative transport prerequisites inferred from the compatible M1 Pro path;
the MTBSI image cannot prove their live values. If that inference is wrong, the
profile will refuse the real M1 Lite rather than probe an unidentified device
through depth-2 registers.

Sources:

- [ITE IT82302 specification V0.3.1](https://www.ite.com.tw/upload/2024_01_23/6_2024012316175049je5fzt61.pdf), sections 6.3.1.10–11 and 7.17.4.12–14
- [LibreHardwareMonitor PawnIO resource note](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Resources/PawnIo/README)
- [PawnIO Modules 0.2.10 `LpcIO.p`](https://github.com/namazso/PawnIO.Modules/blob/0.2.10/LpcIO.p)
- [PawnIO Modules `LpcACPIEC.p`](https://github.com/namazso/PawnIO.Modules/blob/0.2.10/LpcACPIEC.p)

The IT82302 document is used as published documentation for the compatible
ITE I2EC register interface. It is not evidence that the M1 Lite contains an
IT82302; the firmware candidate is 8051-compatible, whereas IT82302 itself is
a different controller family.

## Write ownership and recovery

Initialization accepts only an exact stock M1 Lite curve. It captures the
complete 64-byte state and the 30 owned bytes before any curve write. Closing
without a control change performs no data write.

Before every control transaction the backend rechecks native identity, build
identity, mode, thresholds, and the entire expected curve. That preflight,
the writes, readback, and bounded recovery remain inside one acquisition of
the global ISA mutex. A transaction:

1. writes the selected fan's slopes to zero;
2. writes all selected bases to the bounded code;
3. reads back and verifies the complete curve state.

Reset and shutdown restore captured bases first and slopes second, then verify
the complete baseline. A partially failed transaction is restored only when
every byte still matches the prior, attempted, or baseline state and all
unowned bytes remain unchanged. External ownership or a firmware mode change
causes a fail-closed stop rather than a blind overwrite.

This design reduces the risk of an analysis-only profile. It cannot eliminate
the need for live validation.

## First hardware-validation checklist

When M1 Lite hardware becomes available, record the exact BIOS/EC versions and
test in this order:

1. Confirm DMI, selected outer slot/revision, native identity, sentinels, mode,
   stock curve, targets, RPM, and temperatures with read-only diagnostics.
2. Confirm both displayed tach channels correspond to the physical CPU and
   lower system fans.
3. Apply one stock-envelope, nonzero code to one channel, verify RPM response,
   restore immediately, and verify the exact curve baseline.
4. Repeat for the other channel, then explore lower codes and off one channel
   at a time while directly observing temperatures and RPM.
5. Characterize stable floors, restart thresholds, and saturation across the
   exposed `0..51` range.
6. Test Reset, normal FanControl exit, plugin refresh, sleep/resume, reboot, and
   injected write/readback failures.

Until that work is complete, release notes and UI documentation must continue
to label M1 Lite support **analysis-derived and experimental**.
