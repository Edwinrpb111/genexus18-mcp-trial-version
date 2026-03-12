import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import * as os from "os";
import { GxFileSystemProvider } from "../gxFileSystem";
import { 
  CONFIG_SECTION,
  CONFIG_MCP_PORT, 
  DEFAULT_MCP_PORT, 
  COMMAND_PREFIX,
  DISCOVERY_DELAY 
} from "../constants";

/**
 * McpDiscoveryManager: Torna o servidor MCP GeneXus visível para IAs (Copilot, Claude, Gemini CLI).
 */
export class McpDiscoveryManager {
  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly provider: GxFileSystemProvider,
  ) {}

  public async register() {
    // Try to register tools first as they are critical for the LLM
    this.registerCopilotTools();

    // Defer file creation to avoid triggering workspace updates during activation
    setTimeout(() => this.createLocalDiscoveryFile(), DISCOVERY_DELAY);

    this.registerCommands();
  }

  /**
   * 1. Cria um arquivo .mcp_config.json na raiz do workspace.
   * Isso permite que agentes que acessam a pasta saibam como se conectar ao Gateway.
   */
  private createLocalDiscoveryFile() {
    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) return;

    // Skip virtual folders
    const physicalFolder = folders.find((f) => f.uri.scheme === "file");
    if (!physicalFolder) return;

    const rootPath = physicalFolder.uri.fsPath;
    const configPath = path.join(rootPath, ".mcp_config.json");
    const port = vscode.workspace
      .getConfiguration(CONFIG_SECTION)
      .get(CONFIG_MCP_PORT, DEFAULT_MCP_PORT);

    const config = {
      mcpServers: {
        genexus: {
          type: "http",
          url: `http://localhost:${port}/api/command`,
          name: "GeneXus MCP Server",
          version: "3.5.0",
          capabilities: ["search", "read", "write", "build"],
        },
      },
    };

    try {
      fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
      console.log(
        `[McpDiscoveryManager] Discovery file created at ${configPath}`,
      );
    } catch (e) {
      console.error(
        "[McpDiscoveryManager] Failed to create discovery file:",
        e,
      );
    }
  }

  /**
   * 2. Registra as ferramentas MCP como VS Code Language Model Tools.
   * Isso permite que o Copilot Chat e extensões de IA usem as ferramentas GeneXus diretamente.
   */
  private registerCopilotTools() {
    // Nota: A API de LanguageModelTool ainda é experimental/proposta em algumas versões.
    // Usamos um wrapper seguro.
    try {
      const anyVscode = vscode as any;
      if (anyVscode.lm && anyVscode.lm.registerTool) {
        anyVscode.lm.registerTool("genexus_search", {
          invoke: async (options: any, _token: vscode.CancellationToken) => {
            console.log(
              `[McpDiscoveryManager] Tool invoked: genexus_search with query: ${options.parameters.query}`,
            );
            const result = await this.provider.callGateway({
              method: "execute_command",
              params: {
                module: "Search",
                action: "Query",
                target: options.parameters.query,
              },
            });
            return {
              content: [
                {
                  type: "text",
                  text:
                    typeof result === "string"
                      ? result
                      : JSON.stringify(result),
                },
              ],
            };
          },
        });
        console.log(
          "[McpDiscoveryManager] Registered 'genexus_search' tool for VS Code LM.",
        );
      }
    } catch (e) {
      // Ignora se a API não estiver disponível
    }
  }

  /**
   * 3. Comandos para registro global.
   */
  private registerCommands() {
    this.context.subscriptions.push(
      vscode.commands.registerCommand(
        `${COMMAND_PREFIX}.registerMcpGlobally`,
        async () => {
          const choice = await vscode.window.showInformationMessage(
            "Deseja registrar o GeneXus MCP no Claude Desktop?",
            "Sim (Recomendado)",
            "Não",
          );

          if (choice === "Sim (Recomendado)") {
            await this.updateClaudeConfig();
          }
        },
      ),
    );
  }

  private async updateClaudeConfig() {
    const isWin = os.platform() === "win32";
    if (!isWin) return;

    const claudePath = path.join(
      os.homedir(),
      "AppData",
      "Roaming",
      "Claude",
      "claude_desktop_config.json",
    );
    const port = vscode.workspace
      .getConfiguration(CONFIG_SECTION)
      .get(CONFIG_MCP_PORT, DEFAULT_MCP_PORT);

    try {
      let config: any = { mcpServers: {} };
      if (fs.existsSync(claudePath)) {
        config = JSON.parse(fs.readFileSync(claudePath, "utf8"));
      }

      config.mcpServers = config.mcpServers || {};
      config.mcpServers.genexus = {
        command: "npx",
        args: [
          "-y",
          "@modelcontextprotocol/server-http",
          `http://localhost:${port}/api/command`,
        ],
      };

      fs.writeFileSync(claudePath, JSON.stringify(config, null, 2));
      vscode.window.showInformationMessage(
        "GeneXus MCP registrado no Claude Desktop com sucesso!",
      );
    } catch (e) {
      vscode.window.showErrorMessage(
        `Falha ao atualizar config do Claude: ${e}`,
      );
    }
  }
}
