import { test } from "node:test";
import assert from "node:assert/strict";
import {
  trafficScale,
  trafficCurve,
} from "../src/LanDeviceMonitor.App/Web/chart.js";
import { rate, total, duration } from "../src/LanDeviceMonitor.App/Web/ui.js";
import * as chartFunctions from "../src/LanDeviceMonitor.App/Web/chart.js";

test("fixed slots end at the latest received sample, keep missing directions unknown", () => {
  assert.equal(typeof chartFunctions.trafficSlots, "function");
  const slots = chartFunctions.trafficSlots(
    [
      { at: "2026-10-05T00:00:00.120Z", upload: 7, download: null },
      { at: "2026-10-05T00:00:00.990Z", upload: 0, download: null },
      { at: "2026-10-05T00:00:02.450Z", upload: null, download: 40 },
    ],
    1,
  );
  assert.equal(slots.length, 61);
  assert.equal(slots[0].time, Date.parse("2026-10-04T23:59:02Z"));
  assert.equal(slots[60].time, Date.parse("2026-10-05T00:00:02Z"));
  assert.equal(slots[58].upload, 0);
  assert.equal(slots[58].download, null);
  assert.equal(slots[59].placeholder, true);
  assert.equal(slots[59].upload, null);
  assert.equal(slots[60].upload, null);
  assert.equal(slots[60].download, 40);
  assert.deepEqual(chartFunctions.trafficSlots([], 10), []);
});

test("hover chooses a fixed time slot and does not borrow a distant real sample", () => {
  assert.equal(typeof chartFunctions.trafficHoverIndex, "function");
  const slots = Array.from({ length: 61 }, (_, i) => ({
    placeholder: i !== 60,
  }));
  assert.equal(chartFunctions.trafficHoverIndex(slots, 300, 60, 660), null);
  assert.equal(chartFunctions.trafficHoverIndex(slots, 659, 60, 660), 60);
  assert.equal(chartFunctions.trafficHoverIndex(slots, 59, 60, 660), null);
  assert.equal(chartFunctions.trafficHoverIndex(slots, 661, 60, 660), null);
});

test("normalized slots and time-axis labels keep the server timestamp offset", () => {
  const slots = chartFunctions.trafficSlots(
    [{ at: "2026-10-05T00:20:02.900+08:00", upload: 12, download: null }],
    1,
  );
  assert.equal(slots.at(-1).at, "2026-10-05T00:20:02+08:00");
  assert.equal(slots[0].at, "2026-10-05T00:19:02+08:00");
  const labels = chartFunctions.trafficTimeAxis(slots, 1, 60, 560);
  assert.equal(labels[0].at, "2026-10-05T00:19:02+08:00");
  assert.equal(labels.at(-1).at, "2026-10-05T00:20:02+08:00");
});

test("time axis uses native nominal and minimum-pixel sample steps", () => {
  assert.equal(typeof chartFunctions.trafficTimeAxis, "function");
  const minute = Array.from({ length: 61 }, (_, i) => ({ time: i * 1000 }));
  assert.deepEqual(
    chartFunctions.trafficTimeAxis(minute, 1, 60, 560).map((p) => p.index),
    [0, 12, 24, 36, 48, 60],
  );
  const ten = Array.from({ length: 601 }, (_, i) => ({ time: i * 1000 }));
  assert.deepEqual(
    chartFunctions.trafficTimeAxis(ten, 10, 60, 560).map((p) => p.index),
    [0, 85, 170, 255, 340, 425, 510, 600],
  );
  assert.deepEqual(
    chartFunctions.trafficTimeAxis(ten, 10, 60, 90).map((p) => p.index),
    [0],
  );
});

test("series connects real samples across placeholders without generating zero points", () => {
  assert.equal(typeof chartFunctions.trafficSeries, "function");
  const samples = [
    { time: 0, upload: 10, download: null },
    { time: 1000, upload: null, download: null, placeholder: true },
    { time: 9000, upload: 20, download: 30 },
  ];
  assert.deepEqual(
    chartFunctions.trafficSeries(
      samples,
      "upload",
      (t) => t / 100,
      (v) => 40 - v,
      50,
    ),
    {
      path: "M0,30Q90,20 90,20",
      area: "M0,30Q90,20 90,20L90,50L0,50Z",
      points: [
        [0, 30],
        [90, 20],
      ],
    },
  );
  assert.deepEqual(
    chartFunctions.trafficSeries(
      samples,
      "download",
      (t) => t / 100,
      (v) => 40 - v,
      50,
    ),
    {
      path: "",
      area: "",
      points: [[90, 10]],
    },
  );
  assert.deepEqual(
    trafficScale([
      { upload: null, download: null, placeholder: true },
      { upload: 100, download: 200 },
    ]),
    { min: 90, max: 210 },
  );
  assert.deepEqual(trafficScale([{ upload: null, download: null }]), {
    min: 0,
    max: 1024,
  });
});

test("tooltip tries native right 12px then left 12px and stays within narrow bounds", () => {
  assert.equal(typeof chartFunctions.trafficTooltipPosition, "function");
  assert.deepEqual(
    chartFunctions.trafficTooltipPosition(200, 100, 60, 600, 23, 200, 80),
    { x: 212, y: 31 },
  );
  assert.deepEqual(
    chartFunctions.trafficTooltipPosition(550, 100, 60, 600, 23, 200, 80),
    { x: 438, y: 31 },
  );
  assert.deepEqual(
    chartFunctions.trafficTooltipPosition(80, 100, 60, 140, 23, 100, 80),
    { x: 60, y: 25 },
  );
});
test("traffic curve preserves samples as controls and ends at the latest point", () => {
  assert.equal(
    trafficCurve([
      [0, 10],
      [10, 0],
      [20, 10],
    ]),
    "M0,10Q10,0 15,5Q20,10 20,10",
  );
  assert.equal(trafficCurve([[5, 8]]), "");
});
test("small real traffic stays visible, empty history has a safe scale", () => {
  assert.ok(trafficScale([{ upload: 0, download: 40 }]).max < 100);
  assert.deepEqual(trafficScale([]), { min: 0, max: 1024 });
  assert.deepEqual(trafficScale([{ upload: 0, download: 0 }]), {
    min: 0,
    max: 1024,
  });
});
test("unknown data is not presented as zero and byte units match the desktop", () => {
  assert.equal(rate(null), "—");
  assert.equal(rate(512), "0.50\u2009KB/s");
  assert.equal(rate(7), "0.01\u2009KB/s");
  assert.equal(total(1024 ** 3), "1.00\u2009GB");
  assert.equal(total(0), "0.00\u2009B");
  assert.equal(rate(234567), "229\u2009KB/s");
  assert.equal(total(67891234), "64.7\u2009MB");
});
test("session duration uses both endpoints and handles long sessions", () => {
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-22T03:40:00Z"),
    "2\u2009天\u20093\u2009小时",
  );
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-20T00:00:10Z"),
    "0\u2009分钟",
  );
});
