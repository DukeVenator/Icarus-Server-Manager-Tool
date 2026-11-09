# Example prospect

`DemoProspect.json` is a small, anonymised copy of the test prospect save. Use it to try the editor
without touching a real save.

- Prospect: `SufferingResort`, lobby name `Demo Prospect`
- Members: four demo accounts (`TESTPLAYER01`–`TESTPLAYER04`, account IDs `76561190000000001`–`…004`)
- No real account IDs, player names or server data

Open it directly:

```powershell
IcarusProspectEditor.exe "samples\DemoProspect.json"
```

Or copy it somewhere writable first, since saving writes a `.bak` next to the file:

```powershell
Copy-Item samples\DemoProspect.json "$env:TEMP\DemoProspect.json"
```

`SampleProspectTests` loads this file in CI, so it stays a valid encoded save.
