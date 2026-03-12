import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import { GxShadowService } from "./gxShadowService";
import { GxDiagnosticProvider } from "./diagnosticProvider";
import { GxGatewayClient } from "./infra/GxGatewayClient";
import { GxUriParser } from "./utils/GxUriParser";
import { GxPartMapper, TYPE_SUFFIX, VALID_TYPES } from "./utils/GxPartMapper";
import { GxCacheManager } from "./managers/GxCacheManager";
import { 
  GX_SCHEME, 
  DEFAULT_MCP_PORT, 
  MODULE_SEARCH, 
  MODULE_KB, 
  MODULE_HEALTH,
  DEFAULT_STATUS_BAR_TIMEOUT 
} from "./constants";

export { TYPE_SUFFIX };

export class GxFileSystemProvider implements vscode.FileSystemProvider {
  private _emitter = new vscode.EventEmitter<vscode.FileChangeEvent[]>();
  readonly onDidChangeFile: vscode.Event<vscode.FileChangeEvent[]> =
    this._emitter.event;

  private _gateway: GxGatewayClient;
  private _cache: GxCacheManager;
  private _shadowService?: GxShadowService;
  private _diagnosticProvider?: GxDiagnosticProvider;
  public isBulkIndexing: boolean = false;

  private _kbInitPromise: Promise<any> | null = null;

  constructor() {
    this._cache = new GxCacheManager();
    this._gateway = new GxGatewayClient(
      `http://localhost:${DEFAULT_MCP_PORT}/api/command`,
    );
  }

  public set baseUrl(value: string) {
    this._gateway.baseUrl = value;
  }
  public get baseUrl(): string {
    return this._gateway.baseUrl;
  }

  public setShadowService(service: GxShadowService) {
    this._shadowService = service;
    (this._gateway as any)._shadowService = service;
  }

  public setDiagnosticProvider(provider: GxDiagnosticProvider) {
    this._diagnosticProvider = provider;
  }

  public setPart(uri: vscode.Uri, partName: string) {
    this._cache.filePartState.set(uri.path, partName);
    this._cache.invalidatePartCache(uri.toString(), partName);
    this._cache.mtimes.set(uri.toString(), Date.now());
    this._emitter.fire([{ type: vscode.FileChangeType.Changed, uri }]);
  }

  public getPart(uri: vscode.Uri): string {
    return GxPartMapper.getPart(uri, this._cache.filePartState);
  }

  public async initKb() {
    if (this._kbInitPromise) return this._kbInitPromise;

    console.log("[Nexus IDE] Warming up KB...");
    try {
      await this.callGateway(
        { module: MODULE_HEALTH, action: "Ping" },
        2000,
      );
    } catch {}

    this._kbInitPromise = this.callGateway(
      { module: MODULE_KB, action: "Initialize" },
      300000,
    );

    this._kbInitPromise.then(() => {
      console.log("[Nexus IDE] KB SDK Init complete. Refreshing Root...");
      this._emitter.fire([
        {
          type: vscode.FileChangeType.Changed,
          uri: vscode.Uri.from({ scheme: GX_SCHEME, path: "/" }),
        },
      ]);
      setTimeout(() => this.shadowMetadata(), 1000);
    });

    return this._kbInitPromise;
  }

  private async shadowMetadata() {
    try {
      const result = await this.callGateway({
        module: MODULE_SEARCH,
        action: "Query",
        target: "type:Transaction or type:SDT",
        limit: 20,
      });

      if (result && result.results) {
        for (const obj of result.results) {
          const suffix = TYPE_SUFFIX[obj.type]
            ? `.${TYPE_SUFFIX[obj.type]}`
            : "";
          const uri = vscode.Uri.parse(
            `${GX_SCHEME}:/${obj.type}/${obj.name}${suffix}.gx`,
          );
          const part = obj.type === "Transaction" ? "Structure" : "Source";
          this.fetchAndCacheMetadata(uri, obj.type, obj.name, part);
        }
      }
    } catch (e) {
      console.error("[Nexus IDE] Shadowing failed:", e);
    }
  }

  private async fetchAndCacheMetadata(
    uri: vscode.Uri,
    objType: string,
    objName: string,
    partName: string,
  ) {
    try {
      const target = VALID_TYPES.has(objType)
        ? `${objType}:${objName}`
        : objName;
      const res = await this.callGateway({
        module: "Read",
        action: "ExtractSource",
        target: target,
        part: partName,
      });
      if (res && res.source) {
        let decoded = res.isBase64
          ? Buffer.from(res.source, "base64").toString("utf8")
          : res.source;
        this._cache.metadataCache.set(uri.toString() + ":" + partName, decoded);
      }
    } catch {}
  }

  watch(
    _uri: vscode.Uri,
    _options: { recursive: boolean; excludes: string[] },
  ): vscode.Disposable {
    return new vscode.Disposable(() => {});
  }

  stat(uri: vscode.Uri): vscode.FileStat {
    console.log(`[GxFS] stat: ${uri.toString()}`);
    const info = GxUriParser.parse(uri);
    const pathStr = info ? info.path : "";

    // Support IDE metadata probes (VS Code / Antigravity)
    if (
      info?.path === ".vscode" ||
      info?.path === ".mcp" ||
      info?.path === ".antigravity"
    ) {
      return {
        type: vscode.FileType.Directory,
        ctime: Date.now(),
        mtime: Date.now(),
        size: 0,
      };
    }
    if (
      info?.path.includes("mcp.json") ||
      info?.path.includes("tasks.json") ||
      info?.path.includes("settings.json")
    ) {
      return {
        type: vscode.FileType.File,
        ctime: Date.now(),
        mtime: Date.now(),
        size: 0,
      };
    }

    if (info?.path.startsWith(".") && !info?.path.startsWith(".gx")) {
      throw vscode.FileSystemError.FileNotFound(uri);
    }

    if (info?.path === "" || !info?.path.endsWith(".gx")) {
      return {
        type: vscode.FileType.Directory,
        ctime: Date.now(),
        mtime: Date.now(),
        size: 0,
      };
    }

    const cachedContent = this._cache.contentCache.get(uri.toString());
    let size = cachedContent ? cachedContent.byteLength : 0;

    if (size === 0) {
      const info = GxUriParser.parse(uri);
      if (info) {
        const shadowPath = path.join(
          this._shadowService?.shadowRoot || "",
          info.type,
          `${info.name}.gx`,
        );
        size = fs.existsSync(shadowPath) ? fs.statSync(shadowPath).size : 0;
      }
    }

    if (!this._cache.mtimes.has(uri.toString()))
      this._cache.mtimes.set(uri.toString(), Date.now());

    return {
      type: vscode.FileType.File,
      ctime: Date.now(),
      mtime: this._cache.mtimes.get(uri.toString())!,
      size: size,
    };
  }

  async readDirectory(uri: vscode.Uri): Promise<[string, vscode.FileType][]> {
    try {
      console.log(`[GxFS] readDirectory START: ${uri.toString()}`);
      const info = GxUriParser.parse(uri);
      const pathStr = info ? info.path : "";
      console.log(`[GxFS] readDirectory pathStr: "${pathStr}"`);

      const parentName = (info?.path === "" || !info) ? "" : info.path.split("/").pop()!;
      const cacheKey = `dir:${pathStr}`;

      const cached = this._cache.dirCache.get(cacheKey);
      if (cached && Date.now() - cached.time < 300000) {
        console.log(`[GxFS] readDirectory CACHE HIT: ${cacheKey}`);
        return cached.entries;
      }

      console.log(`[GxFS] readDirectory Fetching: ${parentName}`);
      let query = VALID_TYPES.has(parentName)
        ? `type:${parentName}`
        : `parent:"${parentName}"`;

      const result = await this.callGateway({
        module: MODULE_SEARCH,
        action: "Query",
        target: query,
        limit: 100000,
      });

      console.log(
        `[GxFS] readDirectory Gateway result received for ${parentName}`,
      );
      const objects = result.results || (Array.isArray(result) ? result : []);

      console.log(
        `[GxFS] readDirectory Gateway returned ${objects.length || 0} objects for ${parentName}`,
      );
      if (objects.length > 0) {
        console.log(
          `[GxFS] readDirectory First object example: ${JSON.stringify(objects[0]).substring(0, 100)}`,
        );
      }

      if (Array.isArray(objects)) {
        const mapped = objects.map((obj: any) => {
          const isDir = obj.type === "Folder" || obj.type === "Module";
          const suffix =
            !isDir && TYPE_SUFFIX[obj.type] ? `.${TYPE_SUFFIX[obj.type]}` : "";
          const name = isDir ? obj.name : `${obj.name}${suffix}.gx`;
          return [
            name,
            isDir ? vscode.FileType.Directory : vscode.FileType.File,
          ];
        }) as [string, vscode.FileType][];

        console.log(
          `[GxFS] readDirectory Mapped ${mapped.length} entries. Updating cache.`,
        );
        this._cache.dirCache.set(cacheKey, {
          entries: mapped,
          time: Date.now(),
        });
        return mapped;
      }
    } catch (e) {
      console.error(`[GxFS] readDirectory error for ${uri.toString()}:`, e);
    }
    return [];
  }

  async readFile(uri: vscode.Uri): Promise<Uint8Array> {
    const partName = this.getPart(uri);
    const uriStr = uri.toString();
    const cacheKey = this._cache.getCacheKey(uri, partName);

    const pCache = this._cache.partsCache.get(uriStr);
    if (pCache && pCache.has(partName)) return pCache.get(partName)!;

    const shadowed = this._cache.metadataCache.get(uriStr + ":" + partName);
    if (shadowed) return Buffer.from(shadowed, "utf8");

    if (this._cache.contentCache.has(uriStr))
      return this._cache.contentCache.get(uriStr)!;

    const cached = this._cache.readCache.get(cacheKey);
    if (cached && Date.now() - cached.time < 30000) return cached.data;

    if (this._cache.pendingReadRequests.has(cacheKey))
      return this._cache.pendingReadRequests.get(cacheKey)!;

    const request = (async () => {
      const info = GxUriParser.parse(uri);
      const target = info ? `${info.type}:${info.name}` : null;
      if (!target) {
        return Buffer.alloc(0);
      }
      try {
        const allPartsResult = await this.callGateway({
          module: "Read",
          action: "ExtractAllParts",
          target: target,
        });
        if (allPartsResult && allPartsResult.parts) {
          const newPCache = new Map<string, Uint8Array>();
          for (const [p, content64] of Object.entries(allPartsResult.parts)) {
            newPCache.set(p, Buffer.from(content64 as string, "base64"));
          }
          this._cache.partsCache.set(uriStr, newPCache);
          if (newPCache.has(partName)) {
            const data = newPCache.get(partName)!;
            this._cache.contentCache.set(uriStr, data);
            this._shadowService?.syncToDisk(uri, data, partName);
            return data;
          }
        }

        const result = await this.callGateway({
          module: "Read",
          action: "ExtractSource",
          target: target,
          part: partName,
        });
        const data =
          result && result.source
            ? result.isBase64
              ? Buffer.from(result.source, "base64")
              : Buffer.from(result.source, "utf8")
            : Buffer.from(`// Part not available: ${partName}`, "utf8");

        this._cache.readCache.set(cacheKey, { data, time: Date.now() });
        this._cache.contentCache.set(uriStr, data);
        this._shadowService?.syncToDisk(uri, data, partName);
        return data;
      } catch (error) {
        return Buffer.from(`// Error reading part: ${error}`, "utf8");
      } finally {
        this._cache.pendingReadRequests.delete(cacheKey);
      }
    })();

    this._cache.pendingReadRequests.set(cacheKey, request);
    return request;
  }

  writeFile(
    uri: vscode.Uri,
    content: Uint8Array,
    options: { create: boolean; overwrite: boolean },
  ): Promise<void> {
    return this._writeFile(uri, content, options);
  }

  async preWarm(uri: vscode.Uri): Promise<void> {
    if (
      !this._cache.partsCache.has(uri.toString()) &&
      !this._cache.pendingReadRequests.has(uri.toString())
    ) {
      this.readFile(uri).catch(() => {});
    }
  }

  private async _writeFile(
    uri: vscode.Uri,
    content: Uint8Array,
    options: { create: boolean; overwrite: boolean },
  ): Promise<void> {
    const target = GxPartMapper.getObjectTarget(uri.path);
    const partName = this.getPart(uri);
    const base64Source = Buffer.from(content).toString("base64");

    this._cache.contentCache.set(uri.toString(), content);
    this._cache.mtimes.set(uri.toString(), Date.now());

    try {
      const result = await this.callGateway({
        module: "Write",
        target: target,
        action: partName,
        payload: base64Source,
      });
      if (!result || result.error || result.status === "Error") {
        if (result?.issues && this._diagnosticProvider) {
          const editor = vscode.window.visibleTextEditors.find(
            (e: vscode.TextEditor) =>
              e.document.uri.toString() === uri.toString(),
          );
          if (editor)
            this._diagnosticProvider.setDiagnostics(
              editor.document,
              result.issues,
            );
        }
        throw new Error(result?.error || "Save failed");
      }

      this._cache.commitWrite(uri, partName);
      this._shadowService?.syncToDisk(uri, content, partName);
      this._emitter.fire([{ type: vscode.FileChangeType.Changed, uri }]);

      // Force directory refresh for structural changes
      this.clearDirCache();

      vscode.window.setStatusBarMessage(`$(check) Saved ${target}`, DEFAULT_STATUS_BAR_TIMEOUT);
    } catch (err: any) {
      vscode.window.showErrorMessage(`Save Error: ${err}`);
      throw err;
    }
  }

  public async triggerSave(
    uri: vscode.Uri,
    content: Uint8Array,
  ): Promise<void> {
    return this._writeFile(uri, content, { create: false, overwrite: true });
  }

  public async callGateway(command: any, customTimeout?: number): Promise<any> {
    console.log(`[GxFS] callGateway: ${command.module}`);
    if (!command.method) command.method = "execute_command";
    if (!command.params) command.params = { ...command }; // Compatibility with old internal structure
    return this._gateway.call(command, customTimeout);
  }

  public clearDirCache(): void {
    this._cache.clearDirectoryCache();
    this._emitter.fire([
      {
        type: vscode.FileChangeType.Changed,
        uri: vscode.Uri.from({ scheme: GX_SCHEME, path: "/" }),
      },
    ]);
  }

  createDirectory(uri: vscode.Uri): void {
    throw vscode.FileSystemError.NoPermissions("Not supported");
  }
  delete(uri: vscode.Uri, options: { recursive: boolean }): void {
    throw vscode.FileSystemError.NoPermissions("Not supported");
  }
  rename(
    oldUri: vscode.Uri,
    newUri: vscode.Uri,
    options: { overwrite: boolean },
  ): void {
    throw vscode.FileSystemError.NoPermissions("Not supported");
  }
  copy(
    source: vscode.Uri,
    destination: vscode.Uri,
    options: { overwrite: boolean },
  ): void {
    throw vscode.FileSystemError.NoPermissions("Not supported");
  }
}
