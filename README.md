# GeneXus MCP Server — Trial Edition (Student Fork)

> **Fork adaptado para GeneXus 18 Trial** con soporte para licencia estudiantil `/learning`.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![MCP](https://img.shields.io/badge/MCP-GeneXus-blue)](https://github.com/Edwinrpb111/genexus18-mcp-trial-version)

---

**GeneXus MCP Server** permite que asistentes de IA — Claude, Cursor, OpenCode, y cualquier cliente compatible con MCP — lean, editen, analicen y refactoreen objetos dentro de una Knowledge Base de **GeneXus 18 Trial**. Se comunica con el **SDK nativo de GeneXus**.

**¿Hablás español?** → [Guía de inicio en español](docs/GETTING_STARTED.es.md)

---

## ✅ Prerequisitos

- ✅ **Windows** (GeneXus es solo Windows)
- ✅ **GeneXus 18 Trial** instalado en `C:\Program Files (x86)\GeneXus\GeneXus18Trial`
- ✅ Modificación del acceso directo de `GeneXus.exe` con flag `/learning` (para builds desde el IDE)
- ✅ **Una KB de GeneXus 18** abierta al menos una vez en el IDE
- ✅ **Node.js 18+** — `node --version`
- ✅ **Un cliente MCP** — [Claude Desktop](https://claude.ai/download), [OpenCode](https://opencode.ai), Cursor, etc.

---

## 🚀 Instalación rápida

### Opción A — Desde GitHub (recomendado)

```bash
npx github:Edwinrpb111/genexus18-mcp-trial-version init --kb "C:\KBs\TuKB" --gx "C:\Program Files (x86)\GeneXus\GeneXus18Trial"
```

### Opción B — Release compilada

Descargá el zip de la [última release](https://github.com/Edwinrpb111/genexus18-mcp-trial-version/releases), extraelo y configurá tu cliente MCP:

```json
{
  "genexus": {
    "type": "local",
    "command": ["node", "ruta\\completa\\cli\\run.js"],
    "environment": {
      "GX_PROGRAM_DIR": "C:\\Program Files (x86)\\GeneXus\\GeneXus18Trial",
      "GX_CONFIG_PATH": "ruta\\completa\\config.json"
    },
    "enabled": true
  }
}
```

### Opción C — OpenCode (ya configurado)

Si usás OpenCode, el archivo `C:\Users\tuuser\.config\opencode\opencode.jsonc` ya debería tener la configuración:

```json
"genexus": {
  "type": "local",
  "command": ["node", "C:\\Users\\tuuser\\Downloads\\genexus18-mcp-trial-vercion\\cli\\run.js"],
  "environment": {
    "GX_PROGRAM_DIR": "C:\\Program Files (x86)\\GeneXus\\GeneXus18Trial",
    "GX_CONFIG_PATH": "C:\\Users\\tuuser\\Downloads\\Genexus\\ahoraci\\config.json"
  },
  "enabled": true
}
```

---

## 🧠 Diferencias con el upstream (lennix1337/Genexus18MCP)

| Feature | Upstream | Este fork |
|---------|----------|-----------|
| Ruta GX | `GeneXus18` | **`GeneXus18Trial`** |
| Build sin licencia | Falla con error GXaccount | **Auto-degrada** a specify-only |
| KB lock | Se queda trabado | **CloseKB()** libera automáticamente |
| `genexus_kb action=create` | No existe | **Implementado** |
| Tests | Budget/Golden para `GeneXus18` | **Adaptados** para Trial |
| Nomenclatura | `ServiceCenterName`, etc. | Sufijo `Name` (ej: `ServiceCenterName`) |

---

## 🛠 Lo que podés hacer

**Exploración:**
- *"Listá los primeros 5 objetos de mi KB con nombre y tipo"*
- *"Mostrame el source de la transacción Cliente"*
- *"Buscá todas las transacciones que referencian el atributo ClienteId"*

**Edición:**
- *"Agregale una regla a la transacción Pedido que valide el total"*
- *"Agregá un nuevo atributo FechaCreación de tipo DateTime"*
- *"Renombrá la variable &qty a &cantidad"*

**Build:**
- *"Hacé un specify completo de la KB y mostrame los errores"*
- *"Compilá el objeto Country"* (degrada automáticamente a specify si no hay licencia)

**KB Management:**
- *"Listá las KBs abiertas"*
- *"Creá una KB nueva en C:\KBs\MiProyecto"*
- *"Abrí la KB examenv3 y cerrá la otra"*

---

## ⚠️ Limitaciones conocidas (Trial)

1. **Build completo solo desde IDE**: El SDK Trial no permite builds completos desde el MCP. El MCP **degrada automáticamente** a `specify-only` cuando detecta el error de licencia. Para builds completos + .xpz, usá el `GeneXus.exe /learning` desde el IDE de escritorio.
2. **`genexus_kb action=create`**: Crea la estructura `.gxw` y el archivo de conexión. Podés abrir la KB con el IDE después para que complete la inicialización (modelo, ambiente, base de datos).
3. **KB lock**: Si el MCP se cuelga, ejecutá `taskkill /IM GxMcp.Worker.exe /F` para liberar la KB.

---

## 📦 Compilación desde source

```bash
git clone https://github.com/Edwinrpb111/genexus18-mcp-trial-version.git
cd genexus18-mcp-trial-version
dotnet build src/GxMcp.Worker/GxMcp.Worker.csproj
dotnet build src/GxMcp.Gateway/GxMcp.Gateway.csproj
dotnet test src/GxMcp.Gateway.Tests/GxMcp.Gateway.Tests.csproj
```

---

## 📝 Licencia

MIT — ver [LICENSE](LICENSE). Este es un fork de [lennix1337/Genexus18MCP](https://github.com/lennix1337/Genexus18MCP) con modificaciones para soporte de GeneXus 18 Trial.
