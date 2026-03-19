import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";
import * as crypto from "crypto";
import { TYPE_SUFFIX, GxFileSystemProvider } from "./gxFileSystem";
import { GxUriParser } from "./utils/GxUriParser";
import { GxGatewayClient } from "./infra/GxGatewayClient";
import { extractMcpError, formatMcpErrorMessage } from "./utils/McpErrorFormatter";

const PLACEHOLDER_PREFIX = "// GXMCP_PLACEHOLDER:";
const INDEX_FILE = ".gx_index.json";

type MirrorIndexEntry = {
  type: string;
  name: string;
  part: string;
  path: string;
};

export class GxShadowService {
  private _shadowRoot: string;
  private _baseUrl: string;
  private _gateway: GxGatewayClient;
  private _fileHashes = new Map<string, string>();
  private _fileContentCache = new Map<string, string>();
  private readonly MAX_HASHES = 500;

  constructor(baseUrl: string, shadowRoot?: string) {
    this._baseUrl = baseUrl;
    this._gateway = new GxGatewayClient(baseUrl);
    this._shadowRoot = shadowRoot || this.resolveShadowRoot();

    if (!fs.existsSync(this._shadowRoot)) {
      fs.mkdirSync(this._shadowRoot, { recursive: true });
    }

    GxUriParser.configureShadowRoot(this._shadowRoot);
    GxUriParser.loadMirrorIndex(this._shadowRoot);
  }

  private resolveShadowRoot(): string {
    let workspaceRoot = vscode.workspace.workspaceFolders?.find(
      (f) => f.uri.scheme === "file",
    )?.uri.fsPath;

    if (workspaceRoot && path.basename(workspaceRoot).toLowerCase() === ".gx_mirror") {
      return workspaceRoot;
    }

    if (!workspaceRoot || workspaceRoot.startsWith("genexus")) {
      let current = __dirname;
      while (current !== path.dirname(current)) {
        if (
          fs.existsSync(path.join(current, ".git")) ||
          fs.existsSync(path.join(current, "Genexus18MCP.sln"))
        ) {
          workspaceRoot = current;
          break;
        }
        current = path.dirname(current);
      }
      if (!workspaceRoot) workspaceRoot = process.cwd();
    }

    return path.join(workspaceRoot, ".gx_mirror");
  }

  private rememberFile(shadowPath: string, content: Uint8Array | string): void {
    const buffer =
      typeof content === "string"
        ? Buffer.from(content, "utf8")
        : Buffer.from(content);
    const hash = crypto.createHash("sha256").update(buffer).digest("hex");
    const strContent =
      typeof content === "string"
        ? content
        : new TextDecoder().decode(content);

    if (this._fileHashes.size >= this.MAX_HASHES) {
      const firstKey = this._fileHashes.keys().next().value;
      if (firstKey) {
        this._fileHashes.delete(firstKey);
        this._fileContentCache.delete(firstKey);
      }
    }

    this._fileHashes.set(shadowPath, hash);
    this._fileContentCache.set(shadowPath, strContent);
  }

  private writeTrackedFile(shadowPath: string, content: Uint8Array | string): void {
    const buffer =
      typeof content === "string"
        ? Buffer.from(content, "utf8")
        : Buffer.from(content);

    this.rememberFile(shadowPath, buffer);

    const dir = path.dirname(shadowPath);
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true });
    }

    const tmpPath = `${shadowPath}.tmp`;
    fs.writeFileSync(tmpPath, buffer);
    fs.renameSync(tmpPath, shadowPath);
  }

  private getContainerDisplayName(
    name: string,
    type: string,
    siblingNames: Set<string>,
  ): string {
    const lowered = name.toLowerCase();

    // Escape reserved names that break VS Code or Antigravity
    const isReserved = 
      lowered === ".git" || 
      lowered === ".vscode" || 
      lowered === ".github" || 
      lowered === ".antigravity";

    if (!isReserved && !siblingNames.has(lowered)) {
      siblingNames.add(lowered);
      return name;
    }

    const decorated = `${name} [${type}]`;
    siblingNames.add(decorated.toLowerCase());
    return decorated;
  }

  private getObjectFileName(obj: { name: string; type: string }, part = "Source"): string {
    const partSuffix = part === "Source" ? "" : `.${part}`;
    return `${obj.name}${partSuffix}.gx`;
  }

  private getLegacyObjectFileName(
    obj: { name: string; type: string },
    part = "Source",
  ): string | null {
    const suffix = TYPE_SUFFIX[obj.type];
    if (!suffix) {
      return null;
    }

    const partSuffix = part === "Source" ? "" : `.${part}`;
    return `${obj.name}.${suffix}${partSuffix}.gx`;
  }

  private buildPlaceholder(obj: { name: string; type: string }, part = "Source"): string {
    return `${PLACEHOLDER_PREFIX}${obj.type}:${obj.name}:${part}\n`;
  }

  private getIndexPath(): string {
    return path.join(this._shadowRoot, INDEX_FILE);
  }

  private toRelativeShadowPath(targetPath: string): string {
    return path.relative(this._shadowRoot, targetPath).replace(/\\/g, "/");
  }

  private buildIndexEntry(
    filePath: string,
    obj: { name: string; type: string },
    part = "Source",
  ): MirrorIndexEntry {
    return {
      type: obj.type,
      name: obj.name,
      part,
      path: this.toRelativeShadowPath(filePath),
    };
  }

  private writeMirrorIndex(entries: MirrorIndexEntry[]): void {
    const indexPath = this.getIndexPath();
    const sorted = entries
      .slice()
      .sort((a, b) => a.path.localeCompare(b.path, undefined, { sensitivity: "base" }));
    this.writeTrackedFile(indexPath, JSON.stringify(sorted, null, 2));
    GxUriParser.loadMirrorIndex(this._shadowRoot);
  }

  private findIndexedShadowPath(
    obj: { name: string; type: string },
    part = "Source",
  ): string | null {
    const mirrorPath = GxUriParser.findMirrorPath(obj.type, obj.name, part);
    if (!mirrorPath || !fs.existsSync(mirrorPath)) {
      return null;
    }

    const canonicalPath = this.resolveCanonicalShadowPath(
      path.dirname(mirrorPath),
      obj,
      part,
    );

    return fs.existsSync(canonicalPath) ? canonicalPath : mirrorPath;
  }

  private resolveCanonicalShadowPath(
    targetDir: string,
    obj: { name: string; type: string },
    part = "Source",
  ): string {
    const canonicalPath = path.join(targetDir, this.getObjectFileName(obj, part));
    const legacyName = this.getLegacyObjectFileName(obj, part);
    const legacyPath = legacyName ? path.join(targetDir, legacyName) : null;

    if (legacyPath && fs.existsSync(legacyPath)) {
      if (!fs.existsSync(canonicalPath)) {
        fs.renameSync(legacyPath, canonicalPath);
      } else {
        const canonicalContent = fs.readFileSync(canonicalPath, "utf8");
        const legacyContent = fs.readFileSync(legacyPath, "utf8");
        const canonicalIsPlaceholder = canonicalContent.startsWith(PLACEHOLDER_PREFIX);
        const legacyIsPlaceholder = legacyContent.startsWith(PLACEHOLDER_PREFIX);

        let preferredContent = canonicalContent;
        if (canonicalIsPlaceholder && !legacyIsPlaceholder) {
          preferredContent = legacyContent;
        } else if (!legacyIsPlaceholder) {
          const canonicalMtime = fs.statSync(canonicalPath).mtimeMs;
          const legacyMtime = fs.statSync(legacyPath).mtimeMs;
          if (legacyMtime > canonicalMtime) {
            preferredContent = legacyContent;
          }
        }

        if (preferredContent != canonicalContent) {
          this.writeTrackedFile(canonicalPath, preferredContent);
        }

        fs.unlinkSync(legacyPath);
      }
    }

    return canonicalPath;
  }

  public ensureMirrorPartFile(
    obj: { name: string; type: string },
    part = "Source",
  ): string | null {
    const existingPath = this.findIndexedShadowPath(obj, part);
    if (existingPath) {
      return existingPath;
    }

    const currentIndex = this.readCurrentMirrorIndex();
    const siblingEntry = currentIndex.find(
      (entry) =>
        entry.type.toLowerCase() === obj.type.toLowerCase() &&
        entry.name.toLowerCase() === obj.name.toLowerCase(),
    );

    if (!siblingEntry) {
      return null;
    }

    const siblingAbsolutePath = path.join(this._shadowRoot, siblingEntry.path);
    const targetDir = path.dirname(siblingAbsolutePath);
    const newFilePath = this.resolveCanonicalShadowPath(targetDir, obj, part);

    if (!fs.existsSync(newFilePath)) {
      this.writeTrackedFile(newFilePath, this.buildPlaceholder(obj, part));
    }

    this.writeMirrorIndex([
      ...currentIndex.filter(
        (entry) =>
          !(
            entry.type.toLowerCase() === obj.type.toLowerCase() &&
            entry.name.toLowerCase() === obj.name.toLowerCase() &&
            entry.part.toLowerCase() === part.toLowerCase()
          ),
      ),
      this.buildIndexEntry(newFilePath, obj, part),
    ]);

    return newFilePath;
  }

  public isPlaceholder(filePath: string): boolean {
    if (!fs.existsSync(filePath)) return false;
    try {
      return fs.readFileSync(filePath, "utf8").startsWith(PLACEHOLDER_PREFIX);
    } catch {
      return false;
    }
  }

  public async hydrateOpenedFile(
    uri: vscode.Uri,
    provider: GxFileSystemProvider,
  ): Promise<boolean> {
    if (uri.scheme !== "file" || !this.isPlaceholder(uri.fsPath)) {
      return false;
    }

    const info = GxUriParser.parse(uri);
    if (!info?.name) {
      return false;
    }

    const target =
      info.type && TYPE_SUFFIX[info.type]
        ? `${info.type}:${info.name}`
        : info.name;

    const result = await provider.callMcpTool(
      "genexus_read",
      {
        name: target,
        part: info.part || "Source",
      },
      30000,
    );

    if (!result?.source) {
      return false;
    }

    const text = result.isBase64
      ? Buffer.from(result.source, "base64").toString("utf8")
      : result.source;

    this.writeTrackedFile(uri.fsPath, text);
    return true;
  }

  public async materializeWorkspace(provider: GxFileSystemProvider): Promise<void> {
    const indexEntries: MirrorIndexEntry[] = [];

    const walk = async (parentName: string, parentDir: string) => {
      const children = await provider.browseObjects(parentName);
      const siblingNames = new Set<string>();

      for (const child of children) {
        if (!child?.name || !child?.type) continue;

        if (child.type === "Folder" || child.type === "Module") {
          const displayName = this.getContainerDisplayName(
            child.name,
            child.type,
            siblingNames,
          );
          const nextDir = path.join(parentDir, displayName);
          if (!fs.existsSync(nextDir)) {
            fs.mkdirSync(nextDir, { recursive: true });
          }
          await walk(child.name, nextDir);
          continue;
        }

        const filePath = this.resolveCanonicalShadowPath(parentDir, child);
        if (!fs.existsSync(filePath)) {
          this.writeTrackedFile(filePath, this.buildPlaceholder(child));
        }
        indexEntries.push(this.buildIndexEntry(filePath, child));
      }
    };

    if (!fs.existsSync(this._shadowRoot)) {
      fs.mkdirSync(this._shadowRoot, { recursive: true });
    }

    await walk("Root Module", this._shadowRoot);
    this.writeMirrorIndex(indexEntries);
  }

  public async syncToDisk(
    uri: vscode.Uri,
    content: Uint8Array,
    part: string,
  ): Promise<string | null> {
    try {
      if (!this._shadowRoot) return null;

      const info = GxUriParser.parse(uri);
      if (!info) return null;

      const indexedPath = info.type
        ? this.findIndexedShadowPath({ name: info.name, type: info.type }, part)
        : null;
      const fallbackDir = path.join(this._shadowRoot, info.type || "Objects");
      if (!fs.existsSync(fallbackDir)) fs.mkdirSync(fallbackDir, { recursive: true });

      const shadowPath =
        indexedPath ||
        this.resolveCanonicalShadowPath(
          fallbackDir,
          { name: info.name, type: info.type || "Object" },
          part,
        );

      this.writeTrackedFile(shadowPath, content);
      if (info.type) {
        this.writeMirrorIndex([
          ...this.readCurrentMirrorIndex().filter(
            (entry) => entry.path.toLowerCase() !== this.toRelativeShadowPath(shadowPath).toLowerCase(),
          ),
          this.buildIndexEntry(shadowPath, { name: info.name, type: info.type }, part),
        ]);
      }
      return shadowPath;
    } catch (e) {
      console.error(`[Shadow Service] SyncToDisk failed: ${e}`);
      return null;
    }
  }

  private readCurrentMirrorIndex(): MirrorIndexEntry[] {
    const indexPath = this.getIndexPath();
    if (!fs.existsSync(indexPath)) {
      return [];
    }

    try {
      const raw = fs.readFileSync(indexPath, "utf8");
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  private collectLegacyMirrorFiles(currentDir: string, files: string[] = []): string[] {
    if (!fs.existsSync(currentDir)) {
      return files;
    }

    for (const entry of fs.readdirSync(currentDir, { withFileTypes: true })) {
      const entryPath = path.join(currentDir, entry.name);
      if (entry.isDirectory()) {
        this.collectLegacyMirrorFiles(entryPath, files);
        continue;
      }

      if (!entry.isFile() || !entry.name.toLowerCase().endsWith(".gx")) {
        continue;
      }

      const fileStem = entry.name.replace(/\.gx$/i, "");
      const stemParts = fileStem.split(".");
      const possibleSuffixIndex =
        stemParts.length > 1 &&
        Object.values(TYPE_SUFFIX).includes(stemParts[stemParts.length - 1])
          ? stemParts.length - 1
          : stemParts.length > 2 &&
              Object.values(TYPE_SUFFIX).includes(stemParts[stemParts.length - 2])
            ? stemParts.length - 2
            : -1;

      if (possibleSuffixIndex >= 0) {
        files.push(entryPath);
      }
    }

    return files;
  }

  public consolidateLegacyMirrorFiles(): void {
    const currentIndex = this.readCurrentMirrorIndex();
    const rewrittenEntries: MirrorIndexEntry[] = [];
    const seenKeys = new Set<string>();
    let changed = false;

    for (const entry of currentIndex) {
      const targetDir = path.dirname(path.join(this._shadowRoot, entry.path));
      const canonicalPath = this.resolveCanonicalShadowPath(
        targetDir,
        { name: entry.name, type: entry.type },
        entry.part,
      );
      const normalized = this.buildIndexEntry(
        canonicalPath,
        { name: entry.name, type: entry.type },
        entry.part,
      );
      const key = `${normalized.type}:${normalized.name}:${normalized.part}`.toLowerCase();

      if (!seenKeys.has(key)) {
        rewrittenEntries.push(normalized);
        seenKeys.add(key);
      }

      if (normalized.path !== entry.path.replace(/\\/g, "/")) {
        changed = true;
      }
    }

    for (const legacyFile of this.collectLegacyMirrorFiles(this._shadowRoot)) {
      const info = GxUriParser.parse(vscode.Uri.file(legacyFile));
      if (!info?.type || !info?.name) {
        continue;
      }

      const canonicalPath = this.resolveCanonicalShadowPath(
        path.dirname(legacyFile),
        { name: info.name, type: info.type },
        info.part,
      );
      const normalized = this.buildIndexEntry(
        canonicalPath,
        { name: info.name, type: info.type },
        info.part,
      );
      const key = `${normalized.type}:${normalized.name}:${normalized.part}`.toLowerCase();

      if (!seenKeys.has(key)) {
        rewrittenEntries.push(normalized);
        seenKeys.add(key);
      }

      changed = true;
    }

    if (changed) {
      this.writeMirrorIndex(rewrittenEntries);
      return;
    }

    GxUriParser.loadMirrorIndex(this._shadowRoot);
  }

  public hasMaterializedWorkspace(): boolean {
    return this.readCurrentMirrorIndex().length > 0;
  }

  public shouldIgnore(filePath: string): boolean {
    if (!fs.existsSync(filePath)) return true;

    try {
      const content = fs.readFileSync(filePath);
      const currentHash = crypto.createHash("sha256").update(content).digest("hex");
      const expectedHash = this._fileHashes.get(filePath);

      if (currentHash === expectedHash) return true;

      this._fileHashes.set(filePath, currentHash);
      return false;
    } catch {
      return false;
    }
  }

  private async tryDeltaSync(
    filePath: string,
    newContent: string,
    objectTarget: string,
    partName: string,
  ): Promise<boolean> {
    const oldContent = this._fileContentCache.get(filePath);
    if (!oldContent) return false;

    const oldLines = oldContent.split(/\r?\n/);
    const newLines = newContent.split(/\r?\n/);

    let start = 0;
    while (
      start < oldLines.length &&
      start < newLines.length &&
      oldLines[start] === newLines[start]
    ) {
      start++;
    }

    let oldEnd = oldLines.length - 1;
    let newEnd = newLines.length - 1;
    while (
      oldEnd >= start &&
      newEnd >= start &&
      oldLines[oldEnd] === newLines[newEnd]
    ) {
      oldEnd--;
      newEnd--;
    }

    const linesChanged = newEnd - start + 1;
    if (linesChanged > 0 && linesChanged <= 10) {
      const oldFragment = oldLines.slice(start, oldEnd + 1).join("\n");
      const newFragment = newLines.slice(start, newEnd + 1).join("\n");

      if (oldFragment.length > 0) {
        const response = await this._gateway.callMcpTool(
          "genexus_edit",
          {
            name: objectTarget,
            part: partName,
            mode: "patch",
            operation: "Replace",
            context: oldFragment,
            content: newFragment,
          },
          30000,
        );
        return !response?.error && response?.status !== "Error";
      }
    }

    return false;
  }

  private formatShadowSyncError(
    filePath: string,
    fallbackTarget: string,
    fallbackPart: string,
    rawError: unknown,
  ): string {
    const payload = extractMcpError(rawError);
    const target = payload.target || fallbackTarget;
    const part = payload.part || fallbackPart || "Source";
    const segments = [formatMcpErrorMessage("Shadow Sync Error:", payload)];

    if (target && !payload.target) {
      segments.push(`Object: ${target}`);
    }

    if (part && !payload.part) {
      segments.push(`Part: ${part}`);
    }

    segments.push(`File: ${filePath}`);
    return segments.join(" | ");
  }

  public async syncToKB(filePath: string): Promise<void> {
    try {
      const content = fs.readFileSync(filePath, "utf8");
      if (content.startsWith(PLACEHOLDER_PREFIX)) return;

      const info = GxUriParser.parse(vscode.Uri.file(filePath));
      if (!info?.name) return;

      const objectTarget =
        info.type && TYPE_SUFFIX[info.type]
          ? `${info.type}:${info.name}`
          : info.name;
      const partName = info.part || "Source";

      const deltaSuccess = await this.tryDeltaSync(
        filePath,
        content,
        objectTarget,
        partName,
      );

      if (!deltaSuccess) {
        const response = await this._gateway.callMcpTool(
          "genexus_edit",
          {
            name: objectTarget,
            part: partName,
            mode: "full",
            content,
          },
          30000,
        );

        if (response?.error || response?.status === "Error") {
          throw new Error(response?.error || "Gateway write failed");
        }
      }

      this._fileContentCache.set(filePath, content);
    } catch (e) {
      console.error(`[Shadow Service] SyncToKB failed for ${filePath}: ${e}`);
      const info = GxUriParser.parse(vscode.Uri.file(filePath));
      const fallbackTarget =
        info?.name
          ? info.type && TYPE_SUFFIX[info.type]
            ? `${info.type}:${info.name}`
            : info.name
          : "";
      const fallbackPart = info?.part || "Source";
      vscode.window.showErrorMessage(
        this.formatShadowSyncError(filePath, fallbackTarget, fallbackPart, e),
      );
    }
  }

  public get shadowRoot(): string {
    return this._shadowRoot;
  }
}
