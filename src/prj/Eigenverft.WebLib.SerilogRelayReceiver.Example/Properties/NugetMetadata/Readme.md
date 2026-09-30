# Eigenverft.WebLib.SerilogRelayReceiver.Example

This receiver is one half of a localhost demo. The sender uses `Eigenverft.NetLib.SerilogRelay` from NuGet; the ASP.NET Core receiver uses `Eigenverft.WebLib.SerilogRelayReceiver` from NuGet and stores events in SQLite.

From the repository root, start the receiver:

```powershell
dotnet run --project src/prj/Eigenverft.WebLib.SerilogRelayReceiver.Example/Eigenverft.WebLib.SerilogRelayReceiver.Example.csproj
```

In another terminal, send an event:

```powershell
dotnet run --project src/prj/Eigenverft.WebLib.SerilogRelaySender.Example/Eigenverft.WebLib.SerilogRelaySender.Example.csproj -- "Hello from the example"
```

Inspect the stored message in the second terminal:

```powershell
(Invoke-RestMethod 'http://127.0.0.1:5217/demo/events')[0].renderMessage
```

The result contains `Hello from the example`. Both programs use loopback without authentication. These projects are examples and are not packed or published.
