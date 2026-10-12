import { test } from "node:test";
import assert from "node:assert/strict";
import { metadataLayout } from "../src/LanDeviceMonitor.App/Web/ui.js";

test("landscape metadata keeps all four fields on one row", () => {
  const layout = metadataLayout([48, 51, 110, 136], 500, false);
  assert.deepEqual(layout.order, [0, 1, 2, 3]);
  assert.equal(layout.scale, 1);
  assert.deepEqual(layout.widths, [48, 51, 110, 136]);
});

test("portrait metadata hides status and shows type, IP and MAC on one row", () => {
  const layout = metadataLayout([48, 51, 110, 136], 342, true);
  assert.deepEqual(layout.order, [1, 2, 3]);
  assert.equal(layout.scale, 1);
  assert.deepEqual(layout.widths, [51, 110, 136]);
});

test("tight portrait rows fit complete long addresses without wrapping", () => {
  const layout = metadataLayout([48, 60, 130, 150], 280, true);
  assert.deepEqual(layout.order, [1, 2, 3]);
  assert.equal(layout.widths.length, 3);
  assert.ok(layout.scale > 0 && layout.scale < 1);
  assert.ok(layout.widths.reduce((sum, width) => sum + width, 0) + layout.gap * 2 <= 280.00001);
});

test("orientation rather than available width determines whether status is shown", () => {
  const widths = [48, 51, 110, 136];
  assert.deepEqual(metadataLayout(widths, 500, true).order, [1, 2, 3]);
  const narrowLandscape = metadataLayout(widths, 280, false);
  assert.deepEqual(narrowLandscape.order, [0, 1, 2, 3]);
  assert.equal(narrowLandscape.widths.length, 4);
  assert.ok(narrowLandscape.widths.reduce((sum, width) => sum + width, 0) + narrowLandscape.gap * 3 <= 280.00001);
});
