# Third-party skills — attribution

The following skill bundles under `.gemini/skills/` are imported (and may be locally adapted) from the official **[`genexuslabs/genexus-skills`](https://github.com/genexuslabs/genexus-skills)** repository:

| Local path | Upstream path | License |
|---|---|---|
| `nexa/` | `nexa/` | Apache License 2.0 |
| `frontend/chameleon-controls-library/` | `frontend/chameleon-controls-library/` | Apache License 2.0 |
| `frontend/design-system-builder/` | `frontend/design-system-builder/` | Apache License 2.0 |
| `frontend/mercury-design-system/` | `frontend/mercury-design-system/` | Apache License 2.0 |
| `frontend/ui-creator/` | `frontend/ui-creator/` | Apache License 2.0 |

Full upstream license text: [`LICENSE.upstream`](./LICENSE.upstream).

This Genexus18MCP repository itself remains MIT-licensed (see top-level [`LICENSE`](../../LICENSE)). The imported skills retain their original Apache 2.0 terms; modifications, if any, are tracked in this repo's git history.

To refresh skills against upstream (manual, no automation):

```bash
git clone --depth 1 https://github.com/genexuslabs/genexus-skills.git /tmp/gx-skills
rm -rf .gemini/skills/nexa .gemini/skills/frontend
cp -r /tmp/gx-skills/nexa .gemini/skills/nexa
cp -r /tmp/gx-skills/frontend .gemini/skills/frontend
cp /tmp/gx-skills/LICENSE .gemini/skills/LICENSE.upstream
```
