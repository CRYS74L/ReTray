import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";

const source = readFileSync(
  new URL("../src/LanDeviceMonitor.App/Web/font-ready.js", import.meta.url),
  "utf8",
);

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

function boot({ supported = true, throws = false } = {}) {
  const fonts = { regular: deferred(), bold: deferred(), icons: deferred() };
  const root = { dataset: {} };
  const timers = new Map();
  const frames = [];
  runInNewContext(source, {
    document: {
      documentElement: root,
      fonts: supported ? {
        ready: Promise.resolve(),
        load(description) {
          if (throws) throw new Error("font service unavailable");
          const key = description.includes("App Icons")
            ? "icons" : description.startsWith("600") ? "bold" : "regular";
          return fonts[key].promise;
        },
      } : undefined,
    },
    setTimeout(callback) { const id = timers.size + 1; timers.set(id, callback); return id; },
    clearTimeout(id) { timers.delete(id); },
    requestAnimationFrame(callback) { frames.push(callback); },
  });
  const paint = async () => {
    await new Promise(resolve => setImmediate(resolve));
    while (frames.length) frames.shift()();
  };
  const loaded = key => fonts[key].resolve([{ status: "loaded" }]);
  return { root, fonts, timers, paint, loaded };
}

test("first text paint waits for regular, bold and icon fonts", async () => {
  const page = boot();
  assert.equal(page.root.dataset.fontState, "loading");
  page.loaded("regular");
  page.loaded("icons");
  await page.paint();
  assert.equal(page.root.dataset.fontState, "loading");
  page.loaded("bold");
  await page.paint();
  assert.equal(page.root.dataset.fontState, "ready");
  assert.equal(page.timers.size, 0);
});

test("a cached font set reveals the page on its next paint", async () => {
  const page = boot();
  for (const key of ["regular", "bold", "icons"]) page.loaded(key);
  await page.paint();
  assert.equal(page.root.dataset.fontState, "ready");
  assert.equal(page.timers.size, 0);
});

test("font download failure reveals a stable fallback", async () => {
  const page = boot();
  page.fonts.regular.reject(new Error("download failed"));
  await page.paint();
  assert.equal(page.root.dataset.fontState, "fallback");
  for (const key of ["bold", "icons"]) page.loaded(key);
  await page.paint();
  assert.equal(page.root.dataset.fontState, "fallback");
  assert.equal(page.timers.size, 0);
});

test("an empty font match cannot be reported as the bundled font", async () => {
  const page = boot();
  page.fonts.regular.resolve([]);
  page.loaded("bold");
  page.loaded("icons");
  await page.paint();
  assert.equal(page.root.dataset.fontState, "fallback");
});

test("slow fonts cannot swap in after the loading deadline", async () => {
  const page = boot();
  const deadline = [...page.timers.values()][0];
  deadline();
  assert.equal(page.root.dataset.fontState, "fallback");
  for (const key of ["regular", "bold", "icons"]) page.loaded(key);
  await page.paint();
  assert.equal(page.root.dataset.fontState, "fallback");
  assert.equal(page.timers.size, 0);
});

test("an unavailable Font Loading API leaves the page readable", () => {
  const page = boot({ supported: false });
  assert.equal(page.root.dataset.fontState, "fallback");
  assert.equal(page.timers.size, 0);
});

test("a synchronous font service error cannot leave the page hidden", () => {
  const page = boot({ throws: true });
  assert.equal(page.root.dataset.fontState, "fallback");
  assert.equal(page.timers.size, 0);
});
