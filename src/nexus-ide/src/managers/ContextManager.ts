import * as vscode from "vscode";
import { GxFileSystemProvider } from "../gxFileSystem";
import { CONTEXT_ACTIVE_PART, DEFAULT_STATUS_BAR_TIMEOUT } from "../constants";
import { GxUriParser } from "../utils/GxUriParser";

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
    if (uri && GxUriParser.isGeneXusUri(uri)) {
      const part = this.provider.getPart(uri);
      const objName = GxUriParser.getObjectName(uri);

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
