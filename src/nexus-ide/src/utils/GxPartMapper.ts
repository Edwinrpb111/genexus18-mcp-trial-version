import * as vscode from "vscode";

export const TYPE_SUFFIX: Record<string, string> = {
  Procedure: "prc",
  WebPanel: "wp",
  Transaction: "trn",
  SDT: "sdt",
  StructuredDataType: "sdt",
  DataProvider: "dp",
  DataView: "dv",
  Attribute: "att",
  Table: "tab",
  SDPanel: "sdp",
};

export const VALID_TYPES = new Set([
  "Procedure",
  "Transaction",
  "WebPanel",
  "DataProvider",
  "Attribute",
  "Table",
  "DataView",
  "SDPanel",
  "SDT",
  "StructuredDataType",
  "Domain",
  "Image",
]);

export class GxPartMapper {
  static stripTypeSuffix(nameWithoutGx: string): string {
    const dotParts = nameWithoutGx.split(".");
    if (dotParts.length > 1) {
      const maybeSuffix = dotParts[dotParts.length - 1];
      if (Object.values(TYPE_SUFFIX).includes(maybeSuffix)) {
        return dotParts.slice(0, -1).join(".");
      }
    }

    return nameWithoutGx;
  }

  private static getTypeFromPath(uriPath: string): string | null {
    const cleanPath = uriPath.replace(/^\//, "");
    const parts = cleanPath.split("/");
    const firstSegment = parts.length > 1 ? parts[0] : null;
    if (firstSegment && VALID_TYPES.has(firstSegment)) {
      return firstSegment;
    }

    const fileName = parts[parts.length - 1] || "";
    const nameWithoutGx = fileName.replace(/\.gx$/, "");
    const dotParts = nameWithoutGx.split(".");
    if (dotParts.length >= 2) {
      const maybeSuffix = dotParts[dotParts.length - 1];
      const mappedType = Object.entries(TYPE_SUFFIX).find(
        ([, suffix]) => suffix === maybeSuffix,
      );
      if (mappedType) {
        return mappedType[0];
      }
    }

    return null;
  }

  static getPart(uri: vscode.Uri, filePartState: Map<string, string>): string {
    const part = filePartState.get(uri.path);
    if (part) return part;

    const detectedType = this.getTypeFromPath(uri.path);
    if (detectedType === "Table") return "Structure";
    return "Source";
  }

  static getCleanObjName(pathPart: string): string {
    const nameWithoutGx = pathPart.replace(/\.gx$/, "");
    const dotParts = nameWithoutGx.split(".");
    if (dotParts.length > 1) {
      const lastPart = dotParts[dotParts.length - 1];
      if (Object.values(TYPE_SUFFIX).includes(lastPart)) {
        return dotParts.slice(0, -1).join(".");
      }
    }
    return this.stripTypeSuffix(nameWithoutGx);
  }

  static getObjectTarget(uriPath: string): string | null {
    const cleanPath = uriPath.replace(/^\//, "");
    if (cleanPath.startsWith(".") || cleanPath.includes("/.")) {
      return null;
    }

    const parts = cleanPath.split("/");
    const typeStr = this.getTypeFromPath(uriPath);
    const fileName = parts[parts.length - 1];
    const objName = this.getCleanObjName(fileName);

    return typeStr && VALID_TYPES.has(typeStr)
      ? `${typeStr}:${objName}`
      : objName;
  }
}
