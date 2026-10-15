import { test } from "node:test";
import assert from "node:assert/strict";
import {
  shouldCompleteReturn,
  returnSpringFrames,
} from "../src/LanDeviceMonitor.App/Web/edge-return.js";

test("edge return needs a deliberate drag, uses momentum and respects reversal", () => {
  assert.equal(shouldCompleteReturn(2, 4, 390), false);
  assert.equal(shouldCompleteReturn(150, 0, 390), true);
  assert.equal(shouldCompleteReturn(40, 1, 390), true);
  assert.equal(shouldCompleteReturn(40, 0, 390), false);
  assert.equal(shouldCompleteReturn(270, -0.5, 390), false);
});
test("return settles from its current presentation and never leaves viewport bounds", () => {
  for (const [from, to, velocity] of [
    [150, 390, 1],
    [150, 0, -1],
    [30, 0, 0.1],
    [380, 390, 2],
  ]) {
    const frames = returnSpringFrames(from, to, velocity, 390);
    const x = (f) => +f.transform.match(/translate3d\(([^p]+)px/)[1];
    assert.equal(x(frames[0]), from);
    assert.equal(x(frames.at(-1)), to);
    assert.ok(frames.every((f) => x(f) >= 0 && x(f) <= 390));
    assert.ok(
      frames.every((f, i) => i === 0 || f.offset > frames[i - 1].offset),
    );
  }
});
