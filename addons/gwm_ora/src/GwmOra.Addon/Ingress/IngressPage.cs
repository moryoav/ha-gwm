using System.Net;
using System.Text;
using GwmOra.Addon.Gwm;

namespace GwmOra.Addon.Ingress;

public static class IngressPage
{
    public static IResult Render(GwmVehicleService vehicles)
    {
        var health = vehicles.GetHealth();
        var snapshots = vehicles.GetVehicles().Vehicles;
        var statusClass = health.Status == "ok" ? "ok" : health.Status == "verification_required" ? "warning" : "error";
        var rows = snapshots.Count == 0
            ? "<p class=\"muted\">No vehicle data has been received yet.</p>"
            : String.Join(String.Empty, snapshots.Select(vehicle =>
            {
                var soc = Format(vehicle.Values.Soc, "%");
                var range = Format(vehicle.Values.RangeKm, " km");
                var updated = WebUtility.HtmlEncode(vehicle.Timestamps.UpdateTime?.LocalDateTime.ToString("g") ?? "unknown");
                return $"""
                    <tr>
                      <td>{WebUtility.HtmlEncode(vehicle.Name)}</td>
                      <td class="vin-cell">{WebUtility.HtmlEncode(vehicle.Vin)}</td>
                      <td>{soc}</td>
                      <td>{range}</td>
                      <td>{updated}</td>
                    </tr>
                    """;
            }));

        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>GWM</title>
              <style>
                :root { color-scheme: light dark; font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
                body { margin: 0; background: Canvas; color: CanvasText; }
                main { max-width: 920px; margin: 0 auto; padding: 24px; }
                header { display: flex; gap: 16px; align-items: center; justify-content: space-between; margin-bottom: 24px; }
                h1 { font-size: 28px; margin: 0; }
                .status { border-radius: 999px; padding: 6px 12px; font-weight: 650; text-transform: uppercase; font-size: 12px; letter-spacing: .04em; }
                .ok { background: #0b7f4a; color: white; }
                .warning { background: #a15c00; color: white; }
                .error { background: #b3261e; color: white; }
                section { border: 1px solid color-mix(in srgb, CanvasText 18%, transparent); border-radius: 8px; padding: 18px; margin-bottom: 18px; }
                h2 { font-size: 18px; margin: 0 0 12px; }
                dl { display: grid; grid-template-columns: minmax(120px, 180px) 1fr; gap: 8px 16px; margin: 0; }
                dt { color: color-mix(in srgb, CanvasText 70%, transparent); }
                dd { margin: 0; font-weight: 600; }
                table { width: 100%; border-collapse: collapse; }
                th, td { text-align: left; padding: 10px 8px; border-bottom: 1px solid color-mix(in srgb, CanvasText 12%, transparent); }
                th { color: color-mix(in srgb, CanvasText 70%, transparent); font-size: 13px; }
                .vin-cell { word-break: break-all; }
                .muted { color: color-mix(in srgb, CanvasText 70%, transparent); margin: 0; }
                code { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
              </style>
            </head>
            <body>
              <main>
                <header>
                  <h1>GWM</h1>
                  <span class="status {{statusClass}}">{{WebUtility.HtmlEncode(health.Status)}}</span>
                </header>
                <section>
                  <h2>Add-on Status</h2>
                  <dl>
                    <dt>Authenticated</dt><dd>{{YesNo(health.Authenticated)}}</dd>
                    <dt>Verification</dt><dd>{{(health.VerificationRequired ? "Required" : "Not required")}}</dd>
                    <dt>Vehicles</dt><dd>{{health.VehicleCount}}</dd>
                    <dt>Remote commands</dt><dd>{{YesNo(health.RemoteCommandsEnabled)}}</dd>
                    <dt>Poll interval</dt><dd>{{health.PollIntervalSeconds}} seconds</dd>
                    <dt>Last refresh</dt><dd>{{WebUtility.HtmlEncode(health.LastRefresh?.LocalDateTime.ToString("g") ?? "never")}}</dd>
                  </dl>
                  {{RenderLastError(health.LastError)}}
                </section>
                <section>
                  <h2>Vehicles</h2>
                  {{RenderVehiclesTable(rows, snapshots.Count)}}
                </section>
              </main>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html", Encoding.UTF8);
    }

    private static string Format(double? value, string suffix)
    {
        return value.HasValue ? WebUtility.HtmlEncode($"{value.Value:0.#}{suffix}") : "unknown";
    }

    private static string YesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string RenderLastError(string? lastError)
    {
        return String.IsNullOrWhiteSpace(lastError)
            ? String.Empty
            : $"<p class=\"muted\">Last error: <code>{WebUtility.HtmlEncode(lastError)}</code></p>";
    }

    private static string RenderVehiclesTable(string rows, int count)
    {
        if (count == 0)
        {
            return rows;
        }

        return $$"""
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>VIN</th>
                  <th>SOC</th>
                  <th>Range</th>
                  <th>Updated</th>
                </tr>
              </thead>
              <tbody>{{rows}}</tbody>
            </table>
            """;
    }
}
