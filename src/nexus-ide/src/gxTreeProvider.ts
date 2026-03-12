import * as vscode from "vscode";
import { TYPE_SUFFIX } from "./gxFileSystem";
import { GX_SCHEME } from "./constants";

// Sort priority inside any folder: Module → Folder → everything else (alphabetical within groups)
const TYPE_ORDER: Record<string, number> = {
  Module: 0,
  Folder: 1,
};
const FILE_ORDER = 2;

// Maps GeneXus type names → icon file in resources/
const TYPE_ICON_FILE: Record<string, string> = {
  Module: "module",
  Folder: "folder",
  Procedure: "procedure",
  WebPanel: "webpanel",
  Transaction: "transaction",
  SDT: "sdt",
  DataProvider: "dataprovider",
  DataView: "dataview",
  Attribute: "attribute",
  Table: "table",
  SDPanel: "sdpanel",
};

export interface GxObject {
  name: string;
  type: string;
  description?: string;
  parent?: string;
  module?: string;
}

export class GxTreeItem extends vscode.TreeItem {
  constructor(
    public readonly gxName: string,
    public readonly gxType: string,
    public readonly gxParentPath: string,
    collapsibleState: vscode.TreeItemCollapsibleState,
    private readonly extensionUri: vscode.Uri,
  ) {
    super(gxName, collapsibleState);

    const isContainer = gxType === "Module" || gxType === "Folder";

    this.tooltip = `[${gxType}] ${gxName}`;
    this.contextValue = `gx_${gxType.toLowerCase()}`;

    // Icon: use SVG if available, else codicon fallback
    const iconFile = TYPE_ICON_FILE[gxType];
    if (iconFile) {
      const iconUri = vscode.Uri.joinPath(
        extensionUri,
        "resources",
        `${iconFile}.svg`,
      );
      this.iconPath = { light: iconUri, dark: iconUri };
    } else {
      this.iconPath = new vscode.ThemeIcon("symbol-misc");
    }

    if (!isContainer) {
      // File item: set resourceUri with descriptive suffix
      const suffix = TYPE_SUFFIX[gxType] ? `.${TYPE_SUFFIX[gxType]}` : "";
      this.resourceUri = vscode.Uri.from({
        scheme: GX_SCHEME,
        path: `/${gxParentPath ? gxParentPath + "/" : ""}${gxName}${suffix}.gx`,
      });
      this.command = {
        command: "vscode.open",
        title: "Open",
        arguments: [this.resourceUri],
      };
    }
  }
}

export class GxTreeProvider implements vscode.TreeDataProvider<GxTreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<
    GxTreeItem | undefined | null | void
  >();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private _cache = new Map<string, { items: GxTreeItem[]; time: number }>();

  constructor(
    private readonly callGateway: (cmd: any) => Promise<any>,
    private readonly extensionUri: vscode.Uri,
  ) {}

  /** Clear the cache and re-render the whole tree */
  refresh(): void {
    this._cache.clear();
    this._onDidChangeTreeData.fire();
  }

  /** Clear cache for a single node (e.g., after save) */
  refreshNode(item: GxTreeItem): void {
    this._cache.delete(item.gxName);
    this._onDidChangeTreeData.fire(item);
  }

  getTreeItem(element: GxTreeItem): vscode.TreeItem {
    return element;
  }

  async getChildren(element?: GxTreeItem): Promise<GxTreeItem[]> {
    const parentName = element ? element.gxName : "";
    const parentPath = element
      ? (element.gxParentPath ? element.gxParentPath + "/" : "") +
        element.gxName
      : "";

    const cacheKey = parentName || "ROOT";
    const cached = this._cache.get(cacheKey);
    // PERFORMANCE: Increased cache time to 5 minutes
    if (cached && Date.now() - cached.time < 300000) return cached.items;

    try {
      console.log(`[GxTree] getChildren for: "${parentName}"`);
      const result = await this.callGateway({
        module: "Search",
        action: "Query",
        target: `parent:"${parentName}"`,
        limit: 100000,
      });

      console.log(
        `[GxTree] Result for "${parentName}": ${result?.results?.length || 0} objects`,
      );

      const objects: GxObject[] =
        result.results || (Array.isArray(result) ? result : []);

      // Sort: Module (0) → Folder (1) → Files (2), alphabetical within each group
      objects.sort((a, b) => {
        const oa = TYPE_ORDER[a.type] ?? FILE_ORDER;
        const ob = TYPE_ORDER[b.type] ?? FILE_ORDER;
        if (oa !== ob) return oa - ob;
        return a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
      });

      const items = objects.map((obj) => {
        const isContainer = obj.type === "Module" || obj.type === "Folder";
        return new GxTreeItem(
          obj.name,
          obj.type,
          parentPath,
          isContainer
            ? vscode.TreeItemCollapsibleState.Collapsed
            : vscode.TreeItemCollapsibleState.None,
          this.extensionUri,
        );
      });

      this._cache.set(cacheKey, { items, time: Date.now() });

      // --- ELITE BACKGROUND PRE-FETCH ---
      // If we just loaded the root, pre-fetch more folders to ensure instant navigation
      if (parentName === "") {
        const containers = items.filter(
          (i) => i.gxType === "Folder" || i.gxType === "Module",
        );
        // Pre-fetch first 10 containers sequentially in background to not choke the gateway
        (async () => {
          for (const folder of containers.slice(0, 10)) {
            try {
              // Only 1 level deep for auto-prefetch
              const result = await this.callGateway({
                module: "Search",
                action: "Query",
                target: `parent:"${folder.gxName}"`,
                limit: 50,
              });
              // Store in cache directly without full getChildren recursion
              const subObjects: GxObject[] =
                result.results || (Array.isArray(result) ? result : []);
              if (subObjects.length > 0) {
                const subItems = subObjects.map((obj) => {
                  const isSubContainer =
                    obj.type === "Module" || obj.type === "Folder";
                  return new GxTreeItem(
                    obj.name,
                    obj.type,
                    (folder.gxParentPath ? folder.gxParentPath + "/" : "") +
                      folder.gxName,
                    isSubContainer
                      ? vscode.TreeItemCollapsibleState.Collapsed
                      : vscode.TreeItemCollapsibleState.None,
                    this.extensionUri,
                  );
                });
                this._cache.set(folder.gxName, {
                  items: subItems,
                  time: Date.now(),
                });
              }
            } catch {}
            await new Promise((r) => setTimeout(r, 200));
          }
        })();
      }

      return items;
    } catch (e) {
      console.error(`[Nexus IDE] TreeProvider error for ${parentName}:`, e);
      return [];
    }
  }
}
