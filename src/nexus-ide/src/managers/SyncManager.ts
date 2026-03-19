import * as vscode from "vscode";
import * as http from "http";
import { GxFileSystemProvider } from "../gxFileSystem";
import { GxShadowService } from "../gxShadowService";

export class SyncManager {
  private _sseRequest?: http.ClientRequest;
  private _isDisposed = false;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly provider: GxFileSystemProvider,
    private readonly shadowService: GxShadowService
  ) {}

  public register() {
    this.startListening();
    
    // Command to manually trigger sync/check
    this.context.subscriptions.push(
      vscode.commands.registerCommand("nexus-ide.syncWithKb", async () => {
        const editor = vscode.window.activeTextEditor;
        if (editor && editor.document.uri.scheme === "file" && editor.document.uri.fsPath.endsWith(".gx")) {
             await this.shadowService.hydrateOpenedFile(editor.document.uri, this.provider);
             vscode.window.showInformationMessage("Objeto sincronizado com a KB.");
        }
      })
    );
  }

  private startListening() {
    if (this._isDisposed) return;

    const url = new URL(this.provider.baseUrl);
    const options: http.RequestOptions = {
      hostname: url.hostname,
      port: url.port,
      path: url.pathname,
      method: "GET",
      headers: {
        "Accept": "text/event-stream",
        "Cache-Control": "no-cache",
        "Connection": "keep-alive"
      }
    };

    console.log(`[SyncManager] Connecting to SSE: ${this.provider.baseUrl}`);

    this._sseRequest = http.request(options, (res) => {
      let buffer = "";
      res.on("data", (chunk) => {
        buffer += chunk.toString();
        this.processSseBuffer(buffer);
        // Basic SSE line clear logic could go here if needed
      });

      res.on("end", () => {
        console.log("[SyncManager] SSE Connection ended. Reconnecting in 5s...");
        this.scheduleReconnect();
      });

      res.on("error", (err) => {
        console.error("[SyncManager] SSE Error:", err);
        this.scheduleReconnect();
      });
    });

    this._sseRequest.on("error", (err) => {
      console.error("[SyncManager] SSE Request Error:", err);
      this.scheduleReconnect();
    });

    this._sseRequest.end();
  }

  private processSseBuffer(buffer: string) {
    const lines = buffer.split("\n");
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i].trim();
        if (line.startsWith("data: ")) {
            try {
                const data = JSON.parse(line.substring(6));
                if (data.method === "notifications/resources/updated") {
                    this.handleUpdateNotification(data.params);
                }
            } catch (e) {
                // Not JSON or partial JSON, ignore
            }
        }
    }
  }

  private async handleUpdateNotification(params: any) {
    const uri = params.uri; // e.g., genexus://objects/Procedure:DebugGravar
    console.log(`[SyncManager] Received update notification for: ${uri}`);
    
    // Logic to invalidate cache and potentially re-hydrate if file is open
    this.provider.clearDirCache();
    
    // If the file is open, we should try to reload it or at least let the user know
    const editors = vscode.window.visibleTextEditors;
    for (const editor of editors) {
        // Simple mapping check (could be more robust using GxUriParser)
        if (editor.document.uri.fsPath.includes(params.name || "")) {
            vscode.window.setStatusBarMessage(`$(sync) KB Atualizada: ${params.name}`, 3000);
            // Proactively hydrate to update .gx_mirror file on disk
            await this.shadowService.hydrateOpenedFile(editor.document.uri, this.provider);
        }
    }
  }

  private scheduleReconnect() {
    if (this._isDisposed) return;
    setTimeout(() => this.startListening(), 5000);
  }

  public dispose() {
    this._isDisposed = true;
    this._sseRequest?.destroy();
  }
}
