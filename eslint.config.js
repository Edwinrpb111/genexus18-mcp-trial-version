"use strict";

const js = require("@eslint/js");

module.exports = [
  {
    ignores: ["publish/**", "src/nexus-ide/**", "node_modules/**"],
  },
  {
    files: ["cli/**/*.js", "scripts/**/*.js", "eslint.config.js"],
    languageOptions: {
      ecmaVersion: "latest",
      sourceType: "commonjs",
      globals: {
        require: "readonly",
        module: "writable",
        process: "readonly",
        __dirname: "readonly",
        __filename: "readonly",
        console: "readonly",
        Buffer: "readonly",
        setTimeout: "readonly",
        setInterval: "readonly",
        clearTimeout: "readonly",
        clearInterval: "readonly",
      },
    },
    rules: {
      ...js.configs.recommended.rules,
      "no-unused-vars": ["warn", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      "no-empty": ["warn", { allowEmptyCatch: true }],
    },
  },
];
