import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = fs.readFileSync(
  path.join(root, "src/LanDeviceMonitor.App/Services/ThemeService.cs"),
  "utf8",
);
const cssName = (name) =>
  name.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
const cssColor = (value) =>
  value.length === 9 ? "#" + value.slice(3) + value.slice(1, 3) : value;
const sections = [
  [
    "Light",
    source.slice(
      source.indexOf("LightPalette ="),
      source.indexOf("DarkPalette ="),
    ),
  ],
  [
    "Dark",
    source.slice(
      source.indexOf("DarkPalette ="),
      source.indexOf("public static string NormalizeMode"),
    ),
  ],
];
const output = [
  "/* Generated from ThemeService.cs. Run node scripts/sync-web-desktop-tokens.mjs. */",
];
for (const [theme, body] of sections) {
  const entries = [
    ...body.matchAll(/\["(\w+)Color"\]\s*=\s*"(#[0-9a-f]{6,8})"/gi),
  ];
  if (entries.length < 70)
    throw new Error(`Incomplete desktop ${theme} palette`);
  output.push(
    `${theme === "Light" ? ":root, " : ""}html[data-theme="${theme}"] {`,
    ...entries.map(
      ([, name, color]) => `  --${cssName(name)}: ${cssColor(color)};`,
    ),
    "}",
  );
  console.log(`${theme}: ${entries.length} desktop colors`);
}
fs.writeFileSync(
  path.join(root, "src/LanDeviceMonitor.App/Web/desktop-tokens.css"),
  output.join("\n") + "\n",
);
