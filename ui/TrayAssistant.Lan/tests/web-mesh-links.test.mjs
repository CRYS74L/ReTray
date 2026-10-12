import { test } from "node:test";
import assert from "node:assert/strict";
import { meshLinkPath } from "../src/LanDeviceMonitor.App/Web/precision.js";
test("mesh link follows actual parent contour and child center at both insets", () => {
  for (const inset of [30, 44])
    for (const heights of [
      [108, 124],
      [184, 156],
    ]) {
      const parent = { x: 0, y: 0, width: 320, height: heights[0] },
        child = {
          x: inset,
          y: heights[0] + 16,
          width: 320 - inset,
          height: heights[1],
        };
      const path = meshLinkPath(parent, child),
        numbers = path.match(/-?\d+(?:\.\d+)?/g).map(Number);
      assert.ok(
        numbers[1] <= parent.height && numbers[1] > parent.height - 26.67,
      );
      assert.equal(numbers.at(-1), child.x + 1);
      assert.ok(path.includes("," + (child.y + child.height / 2)));
      assert.ok(!path.includes("NaN"));
      assert.match(path, /A26\.67,26\.67 0 0 0 /);
    }
});
