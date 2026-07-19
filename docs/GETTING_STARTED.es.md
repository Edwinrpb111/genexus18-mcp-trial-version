# Guía de inicio — GeneXus MCP (Trial Edition)

Esta guía te lleva de cero a "el asistente de IA está editando mi KB de GeneXus" en unos minutos.

---

## ¿Qué es esto?

Es un **puente** entre tu asistente de IA (OpenCode, Claude, Cursor) y tu **Knowledge Base de GeneXus 18 Trial**. El asistente puede leer, editar y analizar objetos de tu KB usando el SDK nativo de GeneXus.

---

## ✅ Requisitos

- ✅ **Windows**
- ✅ **GeneXus 18 Trial** instalado en `C:\Program Files (x86)\GeneXus\GeneXus18Trial`
- ✅ **Una KB** abierta al menos una vez en el IDE
- ✅ **Node.js 18+** — `node --version`
- ✅ **OpenCode** (o cualquier cliente MCP)

---

## 🚀 Instalación

### Paso 1 — Ejecutá el instalador

En una terminal, reemplazando las rutas por las tuyas:

```bash
npx github:Edwinrpb111/genexus18-mcp-trial-version init --kb "C:\KBs\TuKB" --gx "C:\Program Files (x86)\GeneXus\GeneXus18Trial"
```

### Paso 2 — Configurá tu cliente MCP

Si el instalador no detectó tu cliente, agregá esto manualmente en la configuración MCP:

**Para OpenCode** (`C:\Users\tuuser\.config\opencode\opencode.jsonc`):

```json
"genexus": {
  "type": "local",
  "command": ["node", "C:\\ruta\\a\\genexus18-mcp-trial-vercion\\cli\\run.js"],
  "environment": {
    "GX_PROGRAM_DIR": "C:\\Program Files (x86)\\GeneXus\\GeneXus18Trial",
    "GX_CONFIG_PATH": "C:\\ruta\\a\\config.json"
  },
  "enabled": true
}
```

### Paso 3 — Reiniciá el cliente y probá

Pegá este prompt:

> *"Usando el GeneXus MCP, listá los primeros 5 objetos de mi KB y mostrame nombre + tipo."*

Si ves una lista de objetos, **está funcionando**.

---

## 🔧 Solución de problemas comunes

| Problema | Solución |
|----------|----------|
| "Worker failed to start" | Verificá que `GX_PROGRAM_DIR` apunte a `GeneXus18Trial` |
| KB locked / no responde | `taskkill /IM GxMcp.Worker.exe /F` y reintentá |
| Error de licencia GXaccount | Es normal — el MCP degrada automáticamente a specify-only |
| El build falla | Usá el IDE de escritorio con `GeneXus.exe /learning` para builds completos |
| `create` no funciona | Abrí la KB con el IDE después de crearla para que complete la inicialización |

---

## 📚 Comandos útiles

| Comando | Qué hace |
|---------|----------|
| `genexus_kb action=list` | Lista KBs abiertas |
| `genexus_kb action=open path="..."` | Abre una KB |
| `genexus_kb action=create path="..." name="..."` | Crea una KB nueva |
| `genexus_kb action=close` | Cierra la KB actual |
| `genexus_lifecycle action=status` | Estado del build |
| `genexus_lifecycle action=build target=ObjectName` | Compila un objeto (degrada a specify sin licencia) |
| `genexus_whoami` | Info del entorno |
| `taskkill /IM GxMcp.Worker.exe /F` | Libera KB trabada |
