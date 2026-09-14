// Copies shared assets from the main repository into public/ so they aren't stored twice in git.
import { cpSync, existsSync, mkdirSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const videoRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = join(videoRoot, "..");
const pub = join(videoRoot, "public");

const copy = (from, to) => {
  if (!existsSync(from)) throw new Error(`Missing asset: ${from}`);
  mkdirSync(dirname(to), { recursive: true });
  cpSync(from, to);
};

for (const file of readdirSync(join(repoRoot, "docs", "screenshots")).filter((f) => f.endsWith(".png"))) {
  copy(join(repoRoot, "docs", "screenshots", file), join(pub, "shots", file));
}
for (const weight of ["Medium", "Bold", "ExtraBold"]) {
  const name = `MPLUSRounded1c-${weight}.ttf`;
  copy(join(repoRoot, "src", "Couchtop.App", "Assets", "Fonts", name), join(pub, "fonts", name));
}
copy(join(repoRoot, "assets", "Couchtop.png"), join(pub, "icon.png"));
console.log("Assets synced into video/public");
