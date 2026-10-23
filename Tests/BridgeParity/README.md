# Shared cloud bridge checks

Build the production plugin with deployment disabled, then run from the repository:

```powershell
dotnet run --project Tests/BridgeParity -c Release -f net10.0 -- 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64' ClientPlugin/bin/Release/net10.0/Clouds.dll Pack
```

Use `net48` in both places for the legacy runtime. Expected: 39 checks. Loads the
actual plugin DLL and shipped noise through a recording Anomaly API. Exercises
both selected renderer routes: static studio parameters, mip publication,
history invalidation, disabled state, placed world volumes, removal and lifetime
republication. Pending/Vulkan clocks must stay static without loading Render11.
Animation is deferred until a shared render clock exists. The fixture does not
prove effect pixels, live placement input or real device-loss recovery. GPU
resource transport and actual pack compilation are separate GuestRuntime checks.
