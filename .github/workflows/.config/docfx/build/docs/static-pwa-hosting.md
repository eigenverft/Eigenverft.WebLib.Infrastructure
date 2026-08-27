# Isolated static and PWA hosting

WebLib's static/PWA hosting APIs are intentionally small. They compose normal ASP.NET Core branch,
static-file, default-file, and endpoint middleware instead of introducing a separate routing or mount system.

The main pieces are:

- `MapIsolated(path, ...)` for an exclusively owned URL subtree.
- `MapRemaining(...)` for the shell pipeline that receives requests not claimed by an earlier isolated subtree.
- `UseStaticFiles(AdditionalMappings...)` for normal ASP.NET Core static files with typed additive MIME mappings.

## Pipeline ownership

`MapIsolated` delegates to native ASP.NET Core `Map` with non-rejoining branch semantics. A matching request
never continues into `MapRemaining`. If the branch does not serve or otherwise handle the request, the native
end of that branch returns HTTP 404.

The matched path segment is preserved. This is useful for folder-oriented hosting because a request such as
`/apps/index.html` remains `/apps/index.html` inside the branch and normal static-file middleware resolves it
against `wwwroot/apps/index.html`. No separate physical-folder mount layer is required.

```csharp
using Eigenverft.WebLib.Infrastructure.Hosting.Pipeline;
using Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles;

app.MapIsolated("/apps", apps =>
{
    apps.UseDefaultFiles();
    apps.UseStaticFiles(AdditionalMappings.WebApp);
});

app.MapIsolated("/downloads", downloads =>
{
    downloads.UseStaticFiles(AdditionalMappings.Media);
});

app.MapRemaining(shell =>
{
    shell.UseRouting();
    shell.UseEndpoints(endpoints =>
    {
        endpoints.MapRazorComponents<App>();
    });
});
```

`MapRemaining` deliberately exposes a normal `IApplicationBuilder`. Endpoint APIs such as `MapStaticAssets()` and
`MapRazorComponents<T>()` therefore stay inside native `UseEndpoints(...)`; WebLib does not add a hybrid
pipeline/router builder just to flatten that syntax.

Declare all isolated branches before `MapRemaining`. `MapRemaining` is terminal for the rest of the application,
so middleware registered after it is intentionally unreachable.

## Missing files and status-code re-execution

An isolated static/PWA subtree owns its misses. A request for `/apps/missing.js` that is not served by the
branch ends as 404 inside that branch and cannot fall through to Razor Components, a SPA fallback, or another
shell endpoint.

This remains true when the application has global status-code re-execution configured before the branches:

```csharp
app.UseStatusCodePagesWithReExecute("/errors/{0}");

app.MapIsolated("/apps", apps =>
{
    apps.UseDefaultFiles();
    apps.UseStaticFiles(AdditionalMappings.WebApp);
});

app.MapRemaining(shell =>
{
    // shell/error endpoints
});
```

For isolated requests, WebLib disables an already-active outer status-code-pages feature. Without that guard,
an outer `UseStatusCodePagesWithReExecute(...)` could re-run the downstream pipeline with `/errors/404`, which
would no longer match `/apps` and could transfer handling to the shell. Disabling the outer feature keeps the
404 owned by the isolated branch while leaving normal status-code re-execution available to non-isolated
requests.

## Additive content-type mappings

ASP.NET Core's `FileExtensionContentTypeProvider` remains an internal implementation detail. WebLib always
creates the target framework's normal provider first and adds a mapping only when that provider does not already
define the extension. Framework defaults therefore remain the base and are never replaced by a WebLib group.

Current predefined groups are:

| Group | `net8.0` additions | `net10.0` additions | Notes |
|---|---|---|---|
| `AdditionalMappings.WebApp` | `.br` → `application/octet-stream`; `.dat` → `application/octet-stream` | same | `.webmanifest` and `.wasm` are already ASP.NET Core defaults and are not redefined. |
| `AdditionalMappings.Media` | `.avif` → `image/avif` | none | ASP.NET Core 10 already includes `.avif`, so this group is intentionally a no-op there. |

Combine predefined groups when a branch needs both:

```csharp
var mappings = AdditionalMappings.Combine(
    AdditionalMappings.WebApp,
    AdditionalMappings.Media);

app.MapIsolated("/apps", apps =>
{
    apps.UseDefaultFiles();
    apps.UseStaticFiles(mappings);
});
```

The group type is opaque by design. Consumers select semantic groups rather than mutating a public
`FileExtensionContentTypeProvider` or maintaining a second copy of ASP.NET Core's mapping table.

## Default files and web-app mappings

WebLib does not add a PWA-specific hosting convenience. Compose the native ASP.NET Core default-file middleware
with the typed additive static-file extension when a web app needs `/index.html` behavior:

```csharp
app.MapIsolated("/apps", apps =>
{
    apps.UseDefaultFiles();
    apps.UseStaticFiles(AdditionalMappings.WebApp);
});
```

For example, `wwwroot/apps/index.html` is served for `/apps/` through normal ASP.NET Core default-file behavior.
Missing files still end at the native isolated-branch 404. No endpoint clearing, route-value clearing, custom terminal
404, SPA/Razor fallback, or PWA-specific hosting abstraction is added.

## Migration from legacy helpers

The older RequestFilters-era hosting code mixed several concerns: PWA/Blazor MIME mappings, folder mounting,
conditional branching, endpoint/route-value clearing, default files, and custom terminal 404 middleware. WP6
splits those concerns along the existing ASP.NET Core abstractions instead.

Use these replacements:

- Replace `AddPwaAndBlazorMappings` with `AdditionalMappings.WebApp`. Only mappings still missing from the
  current ASP.NET Core target framework are carried forward.
- Do not migrate `UseStaticFilesWithPwaAndBlazorContentTypes(...)` as a separate API. Use
  `UseStaticFiles(AdditionalMappings.WebApp)`.
- Replace folder/branch-specific `UseNonAssetFiles` style hosting with `MapIsolated(path, ...)` plus native
  `UseDefaultFiles()` where needed and `UseStaticFiles(...)` inside the branch.
- Put the Razor/Blazor/API shell under `MapRemaining(...)` when explicit remaining-pipeline ownership is useful.

No endpoint clearing, route-value clearing, custom route system, or universal mount primitive is required.
