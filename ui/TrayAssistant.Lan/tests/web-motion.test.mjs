import { test } from "node:test";
import assert from "node:assert/strict";
import {
  nativeEase,
  nativeFrames,
  pageTiming,
  deviceTiming,
} from "../src/LanDeviceMonitor.App/Web/motion.js";
test("page exits overlap delayed entrance, matching the native timeline", () => {
  assert.ok(pageTiming.delay < pageTiming.exit);
  assert.equal(pageTiming.delay + pageTiming.enter, 900);
  assert.equal(deviceTiming.exit + deviceTiming.enter, 600);
  assert.equal(nativeEase.cubicInOut(0.25), 0.0625);
  assert.equal(nativeEase.quinticOut(0.5), 0.96875);
});
test("opacity completes before scale and interrupted motion starts at presentation", () => {
  const frames = nativeFrames(
    { opacity: 0.3, scale: 0.982, y: 9 },
    { opacity: 1, scale: 1, y: 0 },
    "quinticOut",
    720 / 780,
  );
  assert.equal(frames[0].opacity, 0.3);
  assert.equal(frames.at(-1).opacity, 1);
  assert.equal(frames[112].opacity, 1);
  assert.equal(frames.at(-1).transform, "translate(0px,0px) scale(1)");
});
