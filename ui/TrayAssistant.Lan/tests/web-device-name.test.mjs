import { test } from "node:test";
import assert from "node:assert/strict";
import {
  deviceNameParts,
  deviceNameMarkup,
  sessionsMarkup,
} from "../src/LanDeviceMonitor.App/Web/ui.js";

test("mixed device-name boundaries use one-fifth spaces while existing English words remain intact", () => {
  for (const [name, expected] of [
    ["测试设备3", 1],
    ["华为Mate30", 1],
    ["中文+English", 2],
    ["Redmi Note 11T Pro+", 1],
    ["华为 Mate 30", 1],
    ["iPhone 13 Pro Max", 0],
    ["台灯", 0],
    ["é+灯", 2],
    ["A×B2", 2],
    ["设备🙂English", 2],
    ["设备<abc>", 3],
  ]) {
    const parts = deviceNameParts(name);
    assert.equal(parts.filter((p) => p.gap).length, expected, name);
    assert.equal(
      parts
        .map((p) => p.text)
        .join("")
        .replace(/\s/g, ""),
      name.replace(/\s/g, ""),
    );
  }
  assert.equal(
    deviceNameMarkup("设备<abc>"),
    '设备<span class="name-gap"> </span>&lt;<span class="name-gap"> </span>abc<span class="name-gap"> </span>&gt;',
  );
  assert.equal(deviceNameMarkup("iPhone 13 Pro Max"), "iPhone 13 Pro Max");
});
test("history labels are shortened only in compact layout", () => {
  const record = [{ onlineAt: "2026-10-05T09:44:00+08:00", offlineAt: null }];
  assert.match(sessionsMarkup(record, "2026-10-05T10:00:00+08:00"), /离线时间/);
  const compact = sessionsMarkup(record, "2026-10-05T10:00:00+08:00", true);
  assert.match(compact, /class="session-label">上线<\/span>/);
  assert.match(compact, /class="session-label">离线<\/span>/);
  assert.ok(!compact.includes("时间</span>"));
});
