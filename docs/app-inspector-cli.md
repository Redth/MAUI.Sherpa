# App Inspector CLI

`maui-sherpa-inspector` hosts only the MAUI Sherpa app inspector as a local web app. It is intended for host applications that want to start an inspector process and point an embedded WebView at the emitted URL.

The primary distribution is a self-contained executable per platform/runtime identifier, so the host machine does not need the .NET runtime installed.

## Usage

```bash
maui-sherpa-inspector serve --agent-port 9231
```

`--agent-port` is the only required input. The tool defaults to `localhost` for the target agent and binds its own web server to `127.0.0.1` on an ephemeral port.

Optional metadata can be supplied when the caller already has broker/session context:

```bash
maui-sherpa-inspector serve \
  --agent-host localhost \
  --agent-port 9231 \
  --agent-id my-agent \
  --project /path/to/app \
  --session-id abc123 \
  --app-name MyApp \
  --tab tree
```

## Ready output contract

When the server is ready, stdout contains a single sentinel line:

```text
INSPECTOR_READY {"status":"ready","url":"http://127.0.0.1:54321/inspector/devflow/...","endpoints":["http://127.0.0.1:54321"],"pid":12345,"agent":{...},"stop":{...}}
```

Callers should wait for the `INSPECTOR_READY ` prefix, parse the JSON that follows it, and navigate their WebView to `url`.

The payload includes:

| Field | Description |
| --- | --- |
| `url` | Authenticated inspector URL for the WebView. |
| `endpoints` | Local server endpoint list. |
| `pid` | Process ID for explicit termination. |
| `agent` | Agent host, port, and optional caller metadata. |
| `stop.shutdownUrl` | Authenticated local endpoint that stops the server. |
| `stop.autoExitIdleSeconds` | Idle heartbeat timeout, or `null` when auto-exit is disabled. |

## Lifetime

The hosted page sends a heartbeat to the CLI process every few seconds. After the first WebView connection, the process exits when no heartbeat is observed for the configured idle timeout.

Stop options are also printed in the ready payload:

- Send Ctrl+C or SIGTERM to the process.
- Terminate the emitted `pid`.
- Call `stop.shutdownUrl`.

Use `--no-auto-exit` to keep the process alive until one of the explicit stop paths is used.

## Security

The server binds to loopback by default and generates a per-run token. The emitted URL includes the token and the server stores it in an HTTP-only cookie for the interactive Blazor connection. Internal heartbeat and shutdown endpoints require the same token.

Use `--listen-host` only when the caller intentionally wants a different bind address. The inspector can interact with the target app, so exposing it off-machine is not recommended.

## Distribution

Publish self-contained artifacts with a runtime identifier:

```bash
dotnet publish src/MauiSherpa.AppInspector.Cli \
  -c Release \
  -r osx-arm64 \
  --self-contained true
```

Expected release artifacts include runtime-specific executables such as `osx-arm64`, `osx-x64`, `win-x64`, and `linux-x64`.
