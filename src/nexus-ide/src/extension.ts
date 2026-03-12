import * as vscode from "vscode";
import * as fs from "fs";
import { GxFileSystemProvider } from "./gxFileSystem";
import { GxTreeProvider } from "./gxTreeProvider";
import { GxActionsProvider } from "./gxActionsProvider";
import { GxDiagnosticProvider } from "./diagnosticProvider";
import { GxShadowService } from "./gxShadowService";

import { BackendManager } from "./managers/BackendManager";
import { ShadowManager } from "./managers/ShadowManager";
import { ContextManager } from "./managers/ContextManager";
import { CommandManager } from "./managers/CommandManager";
import { ProviderManager } from "./managers/ProviderManager";
import { McpDiscoveryManager } from "./managers/McpDiscoveryManager";
import { 
  GX_SCHEME, 
  STATE_KEY_FOLDER_ADDED, 
  VIEW_EXPLORER, 
  VIEW_ACTIONS,
  CONFIG_SECTION,
  CONFIG_MCP_PORT,
  DEFAULT_MCP_PORT,
  DEFAULT_STATUS_BAR_TIMEOUT
} from "./constants";

let backendManager: BackendManager;

export function activate(context: vscode.ExtensionContext) {
  console.log("[Nexus IDE] Extension activating...");

  const provider = new GxFileSystemProvider();

  // 1. REGISTER COMMANDS FIRST (Ensure they are always available)
  context.subscriptions.push(
    vscode.commands.registerCommand("nexus-ide.openKb", () => {
      console.log("[Nexus IDE] Command 'nexus-ide.openKb' triggered.");
      addKbFolder(context, 5, 2000, provider);
    }),
    vscode.commands.registerCommand("nexus-ide.addKbFolder", () => {
      console.log("[Nexus IDE] Manual 'nexus-ide.addKbFolder' triggered.");
      addKbFolder(context, 5, 2000, provider);
    }),
    vscode.commands.registerCommand("nexus-ide.refreshFilesystem", () => {
      console.log(
        "[Nexus IDE] Command 'nexus-ide.refreshFilesystem' triggered.",
      );
      provider.clearDirCache();
    }),
  );

  // 2. REGISTER FILESYSTEM PROVIDER
  try {
    context.subscriptions.push(
      vscode.workspace.registerFileSystemProvider(GX_SCHEME, provider, {
        isCaseSensitive: false,
        isReadonly: false,
      }),
    );
    console.log(
      `[Nexus IDE] GxFileSystemProvider registered for scheme '${GX_SCHEME}'.`,
    );

    // Warm up
    vscode.workspace.fs
      .stat(vscode.Uri.from({ scheme: GX_SCHEME, path: "/" }))
      .then(
        () => console.log(`[Nexus IDE] Scheme '${GX_SCHEME}' warm up success.`),
        () => {},
      );
  } catch (e) {
    console.error("[Nexus IDE] FS registration failed:", e);
  }

  // 3. DEFERRED INITIALIZATION
  setImmediate(async () => {
    try {
      await initializeExtension(context, provider);
      console.log("[Nexus IDE] Initialization complete.");
    } catch (e) {
      console.error("[Nexus IDE] Init error:", e);
      if (e instanceof Error) {
        console.error(`[Nexus IDE] Stack: ${e.stack}`);
      }
    }
  });

  // Auto-add folder will now happen inside initializeExtension or on command
}

export async function addKbFolder(context: vscode.ExtensionContext, maxRetries = 5, delayMs = 2000, provider?: any) {
  const folders = vscode.workspace.workspaceFolders || [];
  const hasGxFolder = folders.some((f) => f.uri.scheme === GX_SCHEME);
  if (!hasGxFolder) {
    console.log(`[Nexus IDE] Checking if KB is accessible for auto-mount...`);

    for (let attempt = 1; attempt <= maxRetries; attempt++) {
      try {
        // No manual initKb here anymore, handled in deferred initializeExtension


        // Double check if we can actually reach the pseudo-root to avoid ghost folders
        await vscode.workspace.fs.stat(
          vscode.Uri.from({ scheme: GX_SCHEME, path: "/" }),
        );

        console.log(`[Nexus IDE] Adding workspace folder: ${GX_SCHEME}:/ on attempt ${attempt}`);
        vscode.workspace.updateWorkspaceFolders(folders.length, 0, {
          uri: vscode.Uri.from({ scheme: GX_SCHEME, path: "/" }),
          name: "GeneXus KB",
        });
        context.globalState.update(STATE_KEY_FOLDER_ADDED, true);
        return; // Success, exit retry loop
      } catch (e) {
        console.warn(
          `[Nexus IDE] KB mount point not ready yet (Attempt ${attempt}/${maxRetries}). Retrying in ${delayMs}ms...`,
        );
        if (attempt < maxRetries) {
          await new Promise(resolve => setTimeout(resolve, delayMs));
        } else {
          console.error("[Nexus IDE] Auto-mount failed after maximum retries.");
          vscode.window.showWarningMessage("Failed to connect to GeneXus KB MCP Server. You can try reconnecting manually from the Command Palette.");
        }
      }
    }
  }
}
function initializeExtension(
  context: vscode.ExtensionContext,
  provider: GxFileSystemProvider,
) {
  console.log("[Nexus IDE] Starting deferred initialization...");

  const config = vscode.workspace.getConfiguration(CONFIG_SECTION);
  const port = config.get(CONFIG_MCP_PORT, DEFAULT_MCP_PORT);
  provider.baseUrl = `http://localhost:${port}/api/command`;

  // Initialize Managers
  backendManager = new BackendManager(context);
  const contextManager = new ContextManager(context, provider);
  const shadowService = new GxShadowService(provider.baseUrl);
  provider.setShadowService(shadowService);

  const discoveryManager = new McpDiscoveryManager(context, provider);
  // Defer discovery registration until AFTER backend is potentially ready
  // discoveryManager.register(); // Moved below

  const diagnosticProvider = new GxDiagnosticProvider(
    (cmd) => provider.callGateway(cmd),
    provider,
  );
  provider.setDiagnosticProvider(diagnosticProvider);

  const treeProvider = new GxTreeProvider(
    (cmd) => provider.callGateway(cmd),
    context.extensionUri,
  );

  const shadowManager = new ShadowManager(
    context,
    provider,
    shadowService,
    diagnosticProvider,
  );
  shadowManager.register();

  // UI Components
  const treeView = vscode.window.createTreeView(VIEW_EXPLORER, {
    treeDataProvider: treeProvider,
    showCollapseAll: true,
  });
  context.subscriptions.push(treeView);

  treeView.onDidChangeSelection((e) => {
    if (e.selection.length > 0) {
      const item: any = e.selection[0];
      if (item && item.resourceUri) {
        provider.preWarm(item.resourceUri);
      }
    }
  });

  const actionsProvider = new GxActionsProvider();
  vscode.window.createTreeView(VIEW_ACTIONS, {
    treeDataProvider: actionsProvider,
    showCollapseAll: false,
  });

  const providerManager = new ProviderManager(context, provider);
  providerManager.register();

  const commandManager = new CommandManager(
    context,
    provider,
    treeProvider,
    diagnosticProvider,
    contextManager,
    providerManager.historyProvider,
  );
  commandManager.register();

  contextManager.register();
  diagnosticProvider.subscribeToEvents(context);

  // Start Backend and register discovery tools
  backendManager
    .start(provider)
    .then(() => {
      console.log(
        "[Nexus IDE] Backend started successfully. Registering discovery tools...",
      );
      discoveryManager.register();
    })
    .catch((e) => console.error("[Nexus IDE] Backend start failed:", e));

  // KB Initialization (Unified Flow)
  console.log("[Nexus IDE] Initiating KB Initialization...");
  provider.initKb().then(async () => {
    console.log("[Nexus IDE] KB background init complete.");
    treeProvider.refresh();

    // Check if we need indexing
    if (provider.isBulkIndexing) return;

    try {
      const status = await provider.callGateway({
        module: "KB",
        action: "GetIndexStatus",
      });

      const shadowPath = shadowService.shadowRoot;
      const shadowDirExists =
        fs.existsSync(shadowPath) && fs.readdirSync(shadowPath).length > 0;

      if (status && !status.isIndexing && !shadowDirExists) {
        vscode.window.setStatusBarMessage(
          "$(sync~spin) GeneXus: Preparando ambiente...",
          10000,
        );
        provider.isBulkIndexing = true;
        await provider.callGateway({
          module: "KB",
          action: "BulkIndex",
        });

        let isDone = false;
        while (!isDone) {
          await new Promise((resolve) => setTimeout(resolve, 3000));
          const currentStatus = await provider.callGateway({
            module: "KB",
            action: "GetIndexStatus",
          });

          if (currentStatus && currentStatus.status === "Complete") {
            isDone = true;
            vscode.window.setStatusBarMessage(
              "$(check) GeneXus: Ambiente Pronto!",
              DEFAULT_STATUS_BAR_TIMEOUT,
            );
          } else if (currentStatus && currentStatus.isIndexing) {
            vscode.window.setStatusBarMessage(
              `$(sync~spin) GeneXus: Indexando (${currentStatus.processed}/${currentStatus.total})...`,
              3000,
            );
          } else {
            isDone = true;
          }
        }
      }
    } catch (e) {
      console.error("[Nexus IDE] Index status check or BulkIndex failed:", e);
    } finally {
      provider.isBulkIndexing = false;
    }
  });

  // Watch for Save event
  context.subscriptions.push(
    vscode.workspace.onDidSaveTextDocument((doc: vscode.TextDocument) => {
      if (doc.uri.scheme === GX_SCHEME) {
        setTimeout(() => {
          diagnosticProvider.refreshAll();
        }, 1000);
      }
    }),
  );

  console.log("[Nexus IDE] Deferred initialization complete.");

  // Final check to add the virtual folder if missing
  setTimeout(() => {
    addKbFolder(context, 5, 2000, provider);
  }, 2000);
}

export function deactivate() {
  if (backendManager) {
    backendManager.stop();
  }
}
