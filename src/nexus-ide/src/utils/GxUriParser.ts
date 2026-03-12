import * as vscode from "vscode";
import { GX_SCHEME } from "../constants";

export interface GxUriInfo {
  type: string;
  name: string;
  part: string;
  path: string;
}

export class GxUriParser {
  /**
   * Parses a GeneXus URI into its components.
   * Format: gxkb18:/Type/Name.gx#Part or gxkb18:/Type/Name.Part.gx
   */
  static parse(uri: vscode.Uri): GxUriInfo | null {
    if (uri.scheme !== GX_SCHEME) return null;

    const pathStr = decodeURIComponent(uri.path.substring(1));
    const parts = pathStr.split("/");
    
    // Example: /Procedure/MyProc.Source.gx
    const fileName = parts.pop() || "";
    const type = parts.pop() || "";
    
    // Remove .gx suffix
    let cleanName = fileName.replace(".gx", "");
    let part = "Source"; // Default

    // Handle part in name (e.g., MyProc.Source.gx)
    const nameParts = cleanName.split(".");
    if (nameParts.length > 1) {
      part = nameParts.pop()!;
      cleanName = nameParts.join(".");
    }

    return {
      type,
      name: cleanName,
      part,
      path: pathStr
    };
  }

  /**
   * Resolves the active GeneXus editor URI, with fallback to visible editors.
   */
  static getActiveGxUri(): vscode.Uri | null {
    const activeEditor = vscode.window.activeTextEditor;
    if (activeEditor && activeEditor.document.uri.scheme === GX_SCHEME) {
      return activeEditor.document.uri;
    }

    // Fallback to the first visible GeneXus editor
    const visibleGxEditor = vscode.window.visibleTextEditors.find(
      (e) => e.document.uri.scheme === GX_SCHEME
    );
    
    return visibleGxEditor?.document.uri || null;
  }

  /**
   * Gets the object name from a GeneXus URI.
   */
  static getObjectName(uri: vscode.Uri): string {
    const info = this.parse(uri);
    return info?.name || "";
  }
}
