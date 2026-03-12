import * as http from "http";
import { GxShadowService } from "../gxShadowService";
import { DEFAULT_MCP_PORT } from "../constants";

export class GxGatewayClient {
  private _baseUrl = `http://localhost:${DEFAULT_MCP_PORT}/api/command`;
  private _shadowService?: GxShadowService;

  constructor(baseUrl: string, shadowService?: GxShadowService) {
    this._baseUrl = baseUrl;
    this._shadowService = shadowService;
  }

  public get baseUrl(): string {
    return this._baseUrl;
  }

  set baseUrl(url: string) {
    this._baseUrl = url;
  }

  async call(command: any, customTimeout?: number): Promise<any> {
    return new Promise((resolve, reject) => {
      if (this._shadowService && command.params) {
        command.params.shadowPath = this._shadowService.shadowRoot;
      }

      const data = JSON.stringify(command);
      const timeout = customTimeout || 60000;

      console.log(
        `[GxGateway] Calling: ${this._baseUrl} with module ${command.module}...`,
      );
      const url = new URL(this._baseUrl);
      const req = http.request(
        url,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Content-Length": Buffer.byteLength(data),
          },
          timeout: timeout,
        },
        (res) => {
          console.log(
            `[GxGateway] Response status: ${res.statusCode} for module: ${command.module}`,
          );
          let body = "";
          res.on("data", (chunk) => (body += chunk));
          res.on("end", () => {
            try {
              console.log(
                `[GxGateway] Response body received (length: ${body.length})`,
              );
              const fullResponse = JSON.parse(body);

              // NEW: Handle MCP Response Wrapper
              if (fullResponse && fullResponse.result) {
                const mcpResult = fullResponse.result;
                if (
                  mcpResult.content &&
                  Array.isArray(mcpResult.content) &&
                  mcpResult.content.length > 0
                ) {
                  const text = mcpResult.content[0].text;
                  try {
                    // If the text itself is JSON, parse it (standard for most our tools)
                    const trimmed = text.trim();
                    if (trimmed.startsWith("{") || trimmed.startsWith("[")) {
                      try {
                        resolve(JSON.parse(trimmed));
                      } catch (innerE) {
                        console.error(
                          `[GxGateway] JSON parse error in content:`,
                          innerE,
                        );
                        resolve(text);
                      }
                    } else {
                      resolve(text);
                    }
                  } catch {
                    resolve(text);
                  }
                  return;
                }

                // Fallback: If no content list, but has result, return the result directly
                console.log(
                  `[GxGateway] Found result wrapper but no content list.`,
                );
                resolve(fullResponse.result);
                return;
              }

              console.log(`[GxGateway] No result wrapper found.`);
              resolve(fullResponse);
            } catch {
              resolve(body);
            }
          });
        },
      );

      req.on("timeout", () => {
        req.destroy();
        reject(new Error(`Timeout Gateway (${timeout / 1000}s)`));
      });

      req.on("error", reject);
      req.write(data);
      req.end();
    });
  }
}
