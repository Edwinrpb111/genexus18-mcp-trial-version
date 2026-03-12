import * as vscode from "vscode";
import { GxFileSystemProvider } from "../gxFileSystem";
import { GX_SCHEME, CONTEXT_ACTIVE_PART, DEFAULT_STATUS_BAR_TIMEOUT } from "../constants";

export class ContextManager {
  private statusBarItem: vscode.StatusBarItem;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly provider: GxFileSystemProvider
  ) {
    this.statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    this.context.subscriptions.push(this.statusBarItem);
  }

  register() {
    this.context.subscriptions.push(
      vscode.window.onDidChangeActiveTextEditor((editor) => {
        this.updateActiveContext(editor?.document.uri);
      })
    );

    // Initial update
    this.updateActiveContext(vscode.window.activeTextEditor?.document.uri);
  }

  updateActiveContext(uri?: vscode.Uri) {
    if (uri && uri.scheme === GX_SCHEME) {
      const part = this.provider.getPart(uri);
      const pathStr = decodeURIComponent(uri.path.substring(1));
      const objName = pathStr.split("/").pop()!.replace(".gx", "");

      vscode.commands.executeCommand("setContext", CONTEXT_ACTIVE_PART, part);

      this.statusBarItem.text = `$(file-code) GX: ${objName} > ${part}`;
      this.statusBarItem.show();
    } else {
      vscode.commands.executeCommand("setContext", CONTEXT_ACTIVE_PART, null);
      this.statusBarItem.hide();
    }
  }

  setStatusBarMessage(message: string, timeout: number = DEFAULT_STATUS_BAR_TIMEOUT) {
    vscode.window.setStatusBarMessage(message, timeout);
  }
}
