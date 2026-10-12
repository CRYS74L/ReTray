import {
  $,
  esc,
  icon,
  deviceIcon,
  hydrateIcons,
  text,
  bytes,
  date,
  clock,
  duration,
  relativeTime,
  dateMarkup,
  sessionsMarkup,
  renderDeviceName,
  deviceNameMarkup,
  metadataLayout,
  fields,
  toast,
  copy,
} from "./ui.js";
import { TrafficChart } from "./chart.js";
import { DesktopPrecision, desktopRound, meshLinkPath } from "./precision.js";
import { shouldCompleteReturn, returnSpringFrames } from "./edge-return.js";
import {
  nativeFrames,
  nativeEase,
  presentation,
  pageTiming,
  deviceTiming,
} from "./motion.js";

hydrateIcons();
for (const card of document.querySelectorAll(".metric-card")) {
  const content = document.createElement("div");
  content.className = "metric-content";
  content.append(
    card.querySelector(".metric-icon"),
    card.querySelector(":scope >div"),
  );
  card.prepend(content);
}
const chart = new TrafficChart($("traffic-chart"));
const reduced = matchMedia("(prefers-reduced-motion: reduce)");
const compact = matchMedia("(max-width:899px)");
const portrait = matchMedia("(orientation:portrait)");
$("device-status").parentElement.hidden = portrait.matches;
const systemDark = matchMedia("(prefers-color-scheme: dark)");
const state = {
  snapshot: null,
  selected: "",
  filter: "online",
  page: "device",
  displayed: "",
  range: compact.matches ? 5 : 10,
  desktopRange: 10,
  detail: null,
  sessions: [],
  total: 0,
  visible: 3,
  cursor: null,
  connected: false,
  nav: 0,
  history: 0,
  compactOpen: false,
};
const rows = new Map();
const rowHistory = new Map();
let detailController,
  historyController,
  selectionGeneration = 0,
  detailGeneration = 0,
  pollTimer,
  polling = false,
  layoutFrame = 0,
  sidebarExpandedWidth = 390;
let pendingNavigation = false,
  skipRefreshMotion = false;
let precision;
const motion = new WeakMap();
function cancelMotion(el) {
  motion.get(el)?.cancel();
  motion.delete(el);
  if (el) delete el.dataset.motion;
}
async function animate(el, keyframes, options) {
  if (!el || reduced.matches) return;
  const previous = motion.get(el);
  previous?.cancel();
  const animation = el.animate(keyframes, { fill: "both", ...options });
  motion.set(el, animation);
  el.dataset.motion = "active";
  try {
    await animation.finished;
  } catch {}
  if (motion.get(el) === animation) {
    motion.delete(el);
    animation.cancel();
    delete el.dataset.motion;
  }
}
async function request(path, signal) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  signal?.addEventListener("abort", abort, { once: true });
  if (signal?.aborted) abort();
  const timeout = setTimeout(abort, 10000);
  try {
    const response = await fetch("/api/" + path, {
      signal: controller.signal,
      cache: "no-store",
    });
    if (!response.ok)
      throw new Error(
        response.status === 404
          ? "该设备已不在列表中"
          : `请求失败（${response.status}）`,
      );
    return await response.json();
  } finally {
    clearTimeout(timeout);
    signal?.removeEventListener("abort", abort);
  }
}
function sortedDevices() {
  return (state.snapshot?.devices || [])
    .filter((d) => d.online === (state.filter === "online"))
    .sort(
      (a, b) =>
        new Date(b.listSortAt || b.statusChangedAt || b.lastSeen) -
          new Date(a.listSortAt || a.statusChangedAt || a.lastSeen) ||
        a.name.localeCompare(b.name, "zh-CN"),
    );
}
function selectedDevice() {
  return (
    state.snapshot?.devices.find(
      (d) => d.id.toLowerCase() === state.selected.toLowerCase(),
    ) || state.detail?.device
  );
}
function selectContext(id) {
  if (id === state.selected) return false;
  chart.clear();
  state.selected = id;
  state.displayed = "";
  ++selectionGeneration;
  detailController?.abort();
  historyController?.abort();
  state.detail = null;
  state.sessions = [];
  state.total = 0;
  state.visible = 3;
  state.cursor = null;
  rowHistory.clear();
  $("session-list").replaceChildren();
  $("history-reveal").dataset.shown = "0";
  cancelMotion($("history-reveal"));
  return true;
}
function setSegment(group, index) {
  group.style.setProperty("--selected", index);
  group
    .querySelectorAll("button")
    .forEach((el, i) => el.setAttribute("aria-pressed", String(i === index)));
}
function applyTheme() {
  const actual = systemDark.matches ? "Dark" : "Light";
  document.documentElement.dataset.theme = actual;
  document.querySelector("meta[name=theme-color]").content =
    actual === "Dark" ? "#1c1c1d" : "#f2f2f6";
}
function renderConnection() {
  const s = state.snapshot;
  const healthy = state.connected && s?.connection === "已连接";
  $("connection-indicator").classList.toggle("connected", healthy);
  if (healthy && s.refreshedAt) {
    const value = clock(s.refreshedAt),
      status = $("connection-status");
    if (status.textContent !== value + " 更新") {
      const time = document.createElement("span");
      time.className = "status-clock";
      for (const character of value) {
        const slot = document.createElement("span");
        slot.className = character === ":" ? "status-colon" : "status-digit";
        slot.textContent = character;
        time.append(slot);
      }
      status.replaceChildren(time, document.createTextNode(" 更新"));
    }
  } else text("connection-status", s?.connection || "尚未连接");
}
function renderList(force = false) {
  if (!force && (pendingNavigation || filterTransition)) return;
  const snapshot = state.snapshot;
  if (!snapshot) return;
  text("online-count", snapshot.devices.filter((d) => d.online).length);
  text("offline-count", snapshot.devices.filter((d) => !d.online).length);
  const visible = sortedDevices(),
    existing = new Set(snapshot.devices.map((d) => d.id));
  for (const [id, row] of rows)
    if (!existing.has(id)) {
      row.remove();
      rows.delete(id);
    }
  const shown = new Set(visible.map((d) => d.id));
  for (const [id, row] of rows) {
    row.hidden = !shown.has(id);
    if (row.hidden) {
      row.tabIndex = -1;
      row.classList.remove("selected");
      row.setAttribute("aria-selected", "false");
    }
  }
  const now = snapshot.refreshedAt || Date.now();
  const focusId = visible.some((d) => d.id === state.selected)
    ? state.selected
    : visible[0]?.id;
  for (const d of visible) {
    let row = rows.get(d.id);
    if (!row) {
      row = document.createElement("button");
      row.className = "device-row";
      row.dataset.id = d.id;
      row.setAttribute("role", "option");
      row.innerHTML =
        '<span class="device-icon"></span><span class="row-name"></span><span class="row-elapsed"></span>';
      rows.set(d.id, row);
    }
    row.hidden = false;
    row.classList.toggle("selected", d.id === state.selected);
    row.classList.toggle("offline", !d.online);
    row.setAttribute("aria-selected", String(d.id === state.selected));
    row.tabIndex = d.id === focusId ? 0 : -1;
    const kind = d.iconType || d.type;
    if (row.dataset.kind !== kind) {
      row.querySelector(".device-icon").innerHTML = deviceIcon(kind);
      row.dataset.kind = kind;
    }
    renderDeviceName(row.querySelector(".row-name"), d.displayName || d.name);
    const since =
      d.statusChangedAt || (d.online ? d.onlineSince : d.offlineSince);
    row.querySelector(".row-elapsed").textContent = relativeTime(since, now);
    row.setAttribute(
      "aria-label",
      `${d.name}，${d.online ? "在线" : "离线"}，${relativeTime(since, now)}`,
    );
  }
  // Move only out-of-order nodes; polling does not recreate focused controls.
  let cursor = $("device-list").firstElementChild;
  for (const d of visible) {
    const row = rows.get(d.id);
    if (row !== cursor) $("device-list").insertBefore(row, cursor);
    cursor = row.nextElementSibling;
  }
  updateScrollMask($("device-list"));
  scheduleLayout();
}
function renderDevice(d, data = state.detail) {
  if (!d) return;
  renderDeviceName($("device-name"), d.displayName || d.name);
  $("device-heading")?.classList.toggle("offline", !d.online);
  const heading = $("device-icon").parentElement;
  heading.classList.toggle("offline", !d.online);
  const kind = d.iconType || d.type;
  if ($("device-icon").dataset.kind !== kind) {
    $("device-icon").innerHTML = deviceIcon(kind);
    $("device-icon").dataset.kind = kind;
  }
  text("device-status", d.online ? "在线" : "离线");
  text(
    "device-connection",
    d.connection === "网线接入" ? "网线" : d.connection,
  );
  text("device-ip", d.ip || "—");
  text("device-mac", d.mac);
  for (const [field, value, speed] of [
    ["download", d.download, true],
    ["upload", d.upload, true],
    ["download-total", d.totalDownload, false],
    ["upload-total", d.totalUpload, false],
  ]) {
    const parts = bytes(value, speed);
    text(field + "-value", parts.value);
    text(field + "-unit", parts.unit);
  }
  const range = dateMarkup(d.recordRange || "暂无记录");
  for (const id of ["download-range", "upload-range"])
    if ($(id).innerHTML !== range) $(id).innerHTML = range;
  chart.update(
    data?.history || [],
    data?.serverTime || state.snapshot?.refreshedAt || Date.now(),
    state.range,
    state.snapshot?.preferences?.refreshIntervalSeconds || 1,
  );
  renderSessions(false);
  scheduleLayout();
}
function mergeSessions(items) {
  const map = new Map(state.sessions.map((s) => [s.onlineAt, s]));
  for (const s of items || []) map.set(s.onlineAt, s);
  state.sessions = [...map.values()]
    .sort((a, b) => new Date(b.onlineAt) - new Date(a.onlineAt))
    .slice(0, 100);
}
async function loadDetail(id) {
  detailController?.abort();
  detailController = new AbortController();
  const controller = detailController,
    generation = ++detailGeneration;
  try {
    const data = await request(
      "device/" + encodeURIComponent(id),
      controller.signal,
    );
    if (
      generation !== detailGeneration ||
      controller.signal.aborted ||
      id !== state.selected
    )
      return null;
    state.detail = data;
    mergeSessions(data.sessions);
    state.total = Math.min(
      100,
      data.totalSessions || data.device.sessionCount || 0,
    );
    if (!state.cursor) state.cursor = data.nextBefore;
    if (state.displayed === id && !pendingNavigation)
      renderDevice(data.device, data);
    return data;
  } catch (error) {
    if (error.name !== "AbortError" && state.displayed === id)
      toast(error.message);
    return null;
  }
}
function renderSessions(userAction = false) {
  const list = $("session-list"),
    oldHeight = $("history-reveal").offsetHeight;
  const now =
    state.detail?.serverTime || state.snapshot?.refreshedAt || Date.now();
  const active = new Set();
  for (const s of state.sessions) {
    active.add(s.onlineAt);
    let row = rowHistory.get(s.onlineAt);
    if (!row) {
      const holder = document.createElement("div");
      holder.innerHTML = sessionsMarkup([s], now, compact.matches);
      row = holder.firstElementChild;
      rowHistory.set(s.onlineAt, row);
      list.append(row);
    } else {
      const replacement = document.createElement("div");
      replacement.innerHTML = sessionsMarkup([s], now, compact.matches);
      const fresh = replacement.firstElementChild;
      const times = row.querySelector(".session-times");
      if (times.innerHTML !== fresh.querySelector(".session-times").innerHTML)
        times.innerHTML = fresh.querySelector(".session-times").innerHTML;
      const badge = row.querySelector(".session-duration"),
        content = duration(s.onlineAt, s.offlineAt || now);
      if (badge.textContent !== content) badge.textContent = content;
    }
  }
  for (const [key, row] of rowHistory)
    if (!active.has(key)) {
      row.remove();
      rowHistory.delete(key);
    }
  let cursor = list.firstElementChild;
  for (const s of state.sessions) {
    const row = rowHistory.get(s.onlineAt);
    if (row !== cursor) list.insertBefore(row, cursor);
    cursor = row.nextElementSibling;
  }
  const count = Math.min(state.visible, state.total, state.sessions.length),
    expanded = state.total > 3 && count === state.total;
  const toggle = $("history-toggle");
  toggle.hidden = state.total <= 3;
  toggle.setAttribute("aria-expanded", String(expanded));
  toggle.setAttribute(
    "aria-label",
    expanded ? "收起到最近三条上下线记录" : "最多再展示十五条上下线记录",
  );
  if (!userAction && motion.has($("history-reveal"))) {
    scheduleLayout();
    return;
  }
  const generation = ++state.history;
  cancelMotion($("history-reveal"));
  const previousShown = [...list.children].filter((row) => !row.hidden).length;
  const duringCount =
    userAction && !reduced.matches ? Math.max(previousShown, count) : count;
  for (let i = 0; i < list.children.length; i++)
    list.children[i].hidden = i >= duringCount;
  layoutSessions();
  const last = list.children[count - 1],
    height = last ? last.offsetTop + last.offsetHeight + 18 : 36;
  $("history-reveal").style.height = height + "px";
  if (userAction && !reduced.matches) {
    if (count < 3) return;
    animate(
      $("history-reveal"),
      Array.from({ length: 121 }, (_, i) => ({
        offset: i / 120,
        height:
          oldHeight +
          (height - oldHeight) *
            (height > oldHeight
              ? nativeEase.quinticOut(i / 120)
              : nativeEase.cubicInOut(i / 120)) +
          "px",
      })),
      {
        duration: height > oldHeight ? 420 : 320,
        easing: "linear",
      },
    ).then(() => {
      if (generation === state.history) {
        for (let i = 0; i < list.children.length; i++)
          list.children[i].hidden = i >= count;
        $("history-reveal").dataset.shown = count;
        $("history-reveal").style.height = height + "px";
        updateScrollMask($("device-page"));
      }
    });
  } else $("history-reveal").dataset.shown = count;
  updateScrollMask($("device-page"));
}
async function toggleHistory() {
  const id = state.selected,
    generation = selectionGeneration;
  historyController?.abort();
  historyController = new AbortController();
  const target =
    state.visible >= state.total
      ? 3
      : Math.min(state.visible + 15, state.total);
  $("history-toggle").disabled = true;
  try {
    while (target > state.sessions.length && state.cursor) {
      const page = await request(
        "device/" +
          encodeURIComponent(id) +
          "?before=" +
          encodeURIComponent(state.cursor),
        historyController.signal,
      );
      if (id !== state.selected || generation !== selectionGeneration) return;
      const previous = state.sessions.length;
      mergeSessions(page.sessions);
      state.cursor = page.hasMore ? page.nextBefore : null;
      if (state.sessions.length === previous) break;
    }
    if (id === state.selected) {
      state.visible = Math.min(target, state.sessions.length);
      renderSessions(true);
    }
  } catch (error) {
    if (error.name !== "AbortError") toast(error.message);
  } finally {
    $("history-toggle").disabled = false;
  }
}
function renderRouter() {
  const info = state.snapshot?.internet,
    mesh = state.snapshot?.mesh || [];
  text(
    "router-state",
    info ? (info.isConnected ? "互联网已连接" : "互联网未连接") : "正在读取",
  );
  $("router-state").classList.toggle("offline", !!info && !info.isConnected);
  $("router-loading").hidden = !!info;
  fields($("ipv4-fields"), [
    ["连接类型", info?.ipv4ConnectionType],
    ["IP 地址", info?.ipv4Address],
    ["子网掩码", info?.ipv4SubnetMask],
    ["默认网关", info?.ipv4Gateway],
    ["DNS", info?.ipv4Dns],
  ]);
  fields($("ipv6-fields"), [
    ["连接类型", info?.ipv6ConnectionType],
    ["WAN 地址", info?.wanIpv6Address],
    ["WAN 网关", info?.wanIpv6Gateway],
    ["LAN 地址", info?.lanIpv6Address],
    ["LAN 前缀", info?.lanIpv6Prefix],
    ["DNS", info?.ipv6Dns],
  ]);
  const signature = JSON.stringify(mesh);
  if ($("mesh-list").dataset.content !== signature) {
    $("mesh-list").dataset.content = signature;
    $("mesh-list").innerHTML = mesh
      .map(
        (n) =>
          `<article class="mesh-node ${n.isGateway ? "gateway" : "child"}" data-node-id="${esc(n.nodeId || (n.isGateway ? "root" : "node-" + mesh.indexOf(n)))}" data-parent-id="${esc(n.parentNodeId || (n.isGateway ? "" : mesh.find((p) => p.isGateway)?.nodeId || "root"))}"><span class="mesh-icon">${deviceIcon("路由器")}</span><div><div class="mesh-title"><strong>${deviceNameMarkup(n.displayName)}</strong><span class="mesh-role">${n.isGateway ? "主路由" : "Mesh 节点"}</span>${!n.isGateway && n.connectionText && n.connectionText !== "Mesh 节点" ? `<span class="mesh-backhaul">${esc(n.connectionText)}</span>` : ""}</div><div class="mesh-ip">${esc(n.ipAddress)}</div><div class="mesh-hardware">${esc(n.hardware)}</div></div><div class="mesh-count"><strong>${Math.max(0, Number(n.connectedDeviceCount) || 0)}</strong><small>连接设备</small></div></article>`,
      )
      .join("");
  }
  scheduleLayout();
}
function updateHash(page, id) {
  const hash =
    page === "device"
      ? id
        ? "#device=" + encodeURIComponent(id)
        : "#devices"
      : "#" + page;
  if (location.hash !== hash)
    history.pushState(
      { lanPage: page, lanFromList: compact.matches && !state.compactOpen },
      "",
      hash,
    );
  lastRouteHash = location.hash;
}
let navigationController,
  activePanel,
  lastRouteHash,
  filterTransition = false;
const visualIdentity = { opacity: 1, scale: 1, x: 0, y: 0 };
async function exitPanel(panel, duration) {
  if (!panel || panel.hidden) return;
  const generation = state.nav;
  await animate(
    panel,
    nativeFrames(
      presentation(panel),
      { opacity: 0, scale: 0.985, x: 0, y: -8 },
      "cubicInOut",
    ),
    { duration, easing: "linear" },
  );
  if (generation === state.nav && !motion.has(panel)) {
    panel.style.opacity = "0";
    panel.style.transform = "translateY(-8px) scale(.985)";
  }
}
async function enterPanel(panel, timing, y) {
  const generation = state.nav;
  await animate(
    panel,
    nativeFrames(
      { opacity: 0, scale: 0.975, x: 0, y },
      visualIdentity,
      "quinticOut",
      timing.opacity / timing.enter,
    ),
    { duration: timing.enter, delay: timing.delay, easing: "linear" },
  );
  if (generation === state.nav && !motion.has(panel)) {
    panel.style.opacity = "";
    panel.style.transform = "";
  }
}
function clearPageVisuals(keep) {
  for (const panel of [
    ...document.querySelectorAll(".page"),
    $("empty-panel"),
  ]) {
    if (panel === keep) continue;
    panel.hidden = true;
    cancelMotion(panel);
    panel.style.zIndex = "";
    panel.style.opacity = "";
    panel.style.transform = "";
  }
}
function resetPanelPresentation(panel) {
  cancelMotion(panel);
  panel.style.opacity = "";
  panel.style.transform = "";
  panel.style.zIndex = "";
}
async function navigate(
  page,
  id = state.selected,
  { animate: play = true, hash = true } = {},
) {
  if (page === "device" && id)
    id =
      state.snapshot?.devices.find(
        (d) => d.id.toLowerCase() === id.toLowerCase(),
      )?.id || id;
  const fromList = compact.matches && !state.compactOpen;
  // Selecting the already displayed device is an identity operation.
  if (
    !pendingNavigation &&
    !fromList &&
    activePanel &&
    !activePanel.hidden &&
    activePanel.getClientRects().length &&
    page === state.page &&
    (page !== "device" || id === state.displayed)
  )
    return;
  const generation = ++state.nav;
  pendingNavigation = true;
  settleDevicePress();
  resetHistoryDisclosure();
  $("detail-surface").setAttribute("aria-busy", "true");
  navigationController?.abort();
  navigationController = new AbortController();
  detailController?.abort();
  historyController?.abort();
  chart.clear();
  closeAllRangeTips();

  const previousPage = state.page;
  const outgoing = fromList
    ? $("sidebar")
    : activePanel ||
      document.querySelector(".page:not([hidden])") ||
      $("empty-panel");
  stopScrollMotion(outgoing);
  stopScrollMotion($("device-page"));
  const samePanel = !fromList && page === "device" && previousPage === "device";
  const timing = fromList || samePanel ? deviceTiming : pageTiming;
  if (fromList && hash) {
    // A layout flush alone does not update Safari's last painted history snapshot.
    // Paint neutral rows before pushState can capture the outgoing list.
    await new Promise((resolve) =>
      requestAnimationFrame(() => requestAnimationFrame(resolve)),
    );
    if (generation !== state.nav) return;
  }
  if (hash) updateHash(page, id);
  // Prefetch does not mutate the outgoing visual tree or its scroll extent.
  const dataPromise =
    page === "device" && id
      ? !play && state.detail?.device?.id === id
        ? Promise.resolve(state.detail)
        : request(
            "device/" + encodeURIComponent(id),
            navigationController.signal,
          ).catch(() => null)
      : Promise.resolve(null);
  if (samePanel || fromList) {
    await Promise.all([
      play ? exitPanel(outgoing, timing.exit) : Promise.resolve(),
      dataPromise,
    ]);
    if (generation !== state.nav) return;
  }
  const data = await dataPromise;
  if (generation !== state.nav) return;
  if (page === "device") {
    selectContext(id || "");
    if (data) {
      state.detail = data;
      mergeSessions(data.sessions);
      state.total = Math.min(
        100,
        data.totalSessions || data.device.sessionCount || 0,
      );
      state.cursor = data.nextBefore;
    }
    renderList(true);
  }
  state.page = page;
  state.compactOpen = compact.matches;
  document.body.classList.toggle("compact-detail", state.compactOpen);
  let target;
  if (page === "device") {
    const d = selectedDevice();
    target = d ? $("device-page") : $("empty-panel");
    if (d) {
      renderDevice(d);
      state.displayed = d.id;
    }
  } else {
    target = $(page + "-page");
    renderRouter();
  }
  clearPageVisuals(!samePanel && !fromList ? outgoing : null);
  if (play && !reduced.matches) {
    target.style.opacity = "0";
    target.dataset.motion = "preparing";
  }
  target.hidden = false;
  target.scrollTop = 0;
  activePanel = target;
  // Resolve responsive columns before starting a visual-only transform.
  scheduleLayout();
  await new Promise((resolve) => requestAnimationFrame(resolve));
  if (generation !== state.nav) return;
  if (play && !reduced.matches) {
    target.style.zIndex = "2";
    const entering = enterPanel(
      target,
      timing,
      fromList || samePanel ? 16 : 18,
    );
    if (
      !samePanel &&
      !fromList &&
      outgoing &&
      outgoing !== target &&
      !outgoing.hidden
    ) {
      outgoing.style.zIndex = "1";
      exitPanel(outgoing, timing.exit).then(() => {
        if (generation === state.nav) {
          outgoing.hidden = true;
          outgoing.style.zIndex = "";
        }
      });
    }
    await entering;
  }
  if (generation !== state.nav) return;
  clearPageVisuals(target);
  target.style.zIndex = "";
  target.style.opacity = "";
  cancelMotion($("sidebar"));
  $("sidebar").style.opacity = "";
  $("sidebar").style.transform = "";
  pendingNavigation = false;
  $("detail-surface").setAttribute("aria-busy", "false");
  scheduleLayout();
  renderList(true);
  if (page === "device" && state.selected && data)
    renderDevice(data.device, data);
}
async function openList(push = true, play = true) {
  if (!state.compactOpen && compact.matches && !pendingNavigation) return;
  const generation = ++state.nav;
  pendingNavigation = true;
  settleDevicePress();
  resetHistoryDisclosure();
  $("detail-surface").setAttribute("aria-busy", "true");
  navigationController?.abort();
  detailController?.abort();
  historyController?.abort();
  chart.clear();
  closeAllRangeTips();

  const outgoing = activePanel || $("device-page");
  stopScrollMotion(outgoing);
  if (push && location.hash !== "#devices")
    history.pushState({ lanPage: "list" }, "", "#devices");
  lastRouteHash = location.hash;
  if (play) await exitPanel(outgoing, deviceTiming.exit);
  if (generation !== state.nav) return;
  clearPageVisuals();
  resetPanelPresentation($("sidebar"));
  state.compactOpen = false;
  document.body.classList.remove("compact-detail");
  if (compact.matches && state.snapshot) {
    if (location.hash !== "#devices")
      history.replaceState({ lanPage: "list" }, "", "#devices");
    lastRouteHash = location.hash;
  }
  renderList(true);
  if (play) await enterPanel($("sidebar"), deviceTiming, 16);
  if (generation !== state.nav) return;
  pendingNavigation = false;
  $("detail-surface").setAttribute("aria-busy", "false");
  updateScrollMask($("device-list"));
  if (document.body.classList.contains("keyboard-input"))
    rows.get(state.selected)?.focus({ preventScroll: true });
}
function route() {
  const hash = location.hash.slice(1);
  if (hash === "settings") {
    history.replaceState({ lanPage: "list" }, "", "#devices");
  }
  if (hash === "router" || hash === "network") return { page: "router" };
  if (hash.startsWith("device="))
    return { page: "device", id: new URLSearchParams(hash).get("device") };
  return { page: compact.matches ? "list" : "device" };
}
function applyRoute(play = true) {
  if (lastRouteHash === location.hash) return;
  const r = route();
  lastRouteHash = location.hash;
  if (r.page === "list") {
    return openList(false, play);
  }
  return navigate(r.page, r.id || state.selected, {
    animate: play,
    hash: false,
  });
}
async function refresh() {
  if (polling) return;
  polling = true;
  try {
    const data = await request("snapshot?view=overview"),
      first = !state.snapshot,
      previousRefresh = state.snapshot?.refreshedAt;
    state.snapshot = data;
    state.connected = true;
    if (first && !state.selected) {
      const r = route();
      const match =
        r.id &&
        data.devices.find((d) => d.id.toLowerCase() === r.id.toLowerCase());
      const chosen = match || sortedDevices()[0];
      selectContext(chosen?.id || "");
    }
    renderList();
    renderConnection();

    renderRouter();
    if (first) applyRoute(false);
    else if (
      state.selected &&
      !sortedDevices().some((d) => d.id === state.selected)
    ) {
      selectContext("");
      if (
        state.page === "device" &&
        !pendingNavigation &&
        (!compact.matches || state.compactOpen)
      )
        navigate("device", "", { animate: false, hash: false });
    } else if (
      state.page === "device" &&
      state.selected &&
      !pendingNavigation &&
      (!compact.matches || state.compactOpen)
    ) {
      renderDevice(selectedDevice());
      await loadDetail(state.selected);
    }
    if (
      !reduced.matches &&
      !skipRefreshMotion &&
      data.refreshedAt &&
      data.refreshedAt !== previousRefresh &&
      $("connection-indicator").classList.contains("connected")
    )
      $("connection-indicator").animate(
        [
          { transform: "scale(1)" },
          { transform: "scale(1.12)", offset: 0.2 },
          { transform: "scale(1)" },
        ],
        {
          duration: Math.min(
            1800,
            (data.preferences?.refreshIntervalSeconds || 1) * 1000,
          ),
          easing: "ease-out",
        },
      );
  } catch (error) {
    state.connected = false;
    text(
      "connection-status",
      state.snapshot ? "连接中断，正在重试" : "尚未连接",
    );
    $("connection-indicator").classList.remove("connected");
  } finally {
    skipRefreshMotion = false;
    polling = false;
    clearTimeout(pollTimer);
    pollTimer = setTimeout(
      refresh,
      document.hidden
        ? 5000
        : Math.max(
            1000,
            (state.snapshot?.preferences?.refreshIntervalSeconds || 1) * 1000,
          ),
    );
  }
}
$("device-list").addEventListener("click", (event) => {
  const row = event.target.closest(".device-row");
  if (!row) return;
  const fromTouch =
    event.pointerType === "touch" ||
    event.sourceCapabilities?.firesTouchEvents ||
    (event.detail > 0 && lastDeviceInput === "touch");
  if (
    fromTouch &&
    (!completedDeviceTouch ||
      completedDeviceTouch.id !== row.dataset.id ||
      pendingNavigation ||
      edgeReturn)
  ) {
    event.preventDefault();
    return;
  }
  completedDeviceTouch = null;
  navigate("device", row.dataset.id);
});
let pressedDevice,
  activeDeviceTouch,
  completedDeviceTouch,
  lastDeviceInput = "pointer",
  listRestoredAt = 0;
function resetHistoryDisclosure() {
  state.visible = 3;
  ++state.history;
  cancelMotion($("history-reveal"));
}
function settleDevicePress() {
  $("device-list").classList.add("press-settled");
  releaseDevicePress();
  activeDeviceTouch = null;
  completedDeviceTouch = null;
  for (const [id, row] of rows) {
    row.classList.remove("pressed");
    row.getAnimations().forEach((animation) => animation.cancel());
    if (compact.matches) {
      // Recreate the renderer rather than reuse a hidden touch-highlight layer.
      const neutral = row.cloneNode(true);
      row.replaceWith(neutral);
      rows.set(id, neutral);
    }
  }
  $("device-list").getBoundingClientRect();
}
function releaseDevicePress() {
  pressedDevice?.row.classList.remove("pressed");
  pressedDevice = null;
}
function cancelDeviceInteraction() {
  activeDeviceTouch = null;
  completedDeviceTouch = null;
  releaseDevicePress();
}
$("device-list").addEventListener("pointerdown", (event) => {
  lastDeviceInput = event.pointerType === "touch" ? "touch" : "pointer";
  // Safari can deliver pointer compatibility events after a history gesture.
  // Touch feedback is owned by the actual touchstart/touchend lifecycle below.
  if (event.pointerType === "touch") return;
  if (event.button !== 0) return;
  if (pendingNavigation || state.compactOpen) return;
  const row = event.target.closest(".device-row");
  if (!row) return;
  releaseDevicePress();
  $("device-list").classList.remove("press-settled");
  pressedDevice = {
    kind: "pointer",
    row,
    id: event.pointerId,
    x: event.clientX,
    y: event.clientY,
  };
  row.classList.add("pressed");
});
document.addEventListener(
  "pointermove",
  (event) => {
    if (
      pressedDevice &&
      pressedDevice.kind === "pointer" &&
      event.pointerId === pressedDevice.id &&
      Math.hypot(
        event.clientX - pressedDevice.x,
        event.clientY - pressedDevice.y,
      ) > 8
    )
      releaseDevicePress();
  },
  { passive: true },
);
const releasePointerPress = (event) => {
  if (pressedDevice?.kind === "pointer" && pressedDevice.id === event.pointerId)
    releaseDevicePress();
};
document.addEventListener("pointerup", releasePointerPress);
document.addEventListener("pointercancel", releasePointerPress);
$("device-list").addEventListener(
  "touchstart",
  (event) => {
    lastDeviceInput = "touch";
    completedDeviceTouch = null;
    if (event.touches.length !== 1) {
      cancelDeviceInteraction();
      return;
    }
    const stamp =
      event.timeStamp > 1e12
        ? event.timeStamp - performance.timeOrigin
        : event.timeStamp;
    if (
      stamp <= listRestoredAt ||
      pendingNavigation ||
      state.compactOpen ||
      edgeReturn
    )
      return;
    const row = event.target.closest(".device-row"),
      touch = event.touches[0];
    if (!row || row.hidden || touch.clientX < 24) return;
    releaseDevicePress();
    $("device-list").classList.remove("press-settled");
    activeDeviceTouch = {
      row,
      id: touch.identifier,
      x: touch.clientX,
      y: touch.clientY,
      moved: false,
    };
    pressedDevice = { ...activeDeviceTouch, kind: "touch" };
    row.classList.add("pressed");
  },
  { passive: true },
);
document.addEventListener(
  "touchmove",
  (event) => {
    const active = activeDeviceTouch;
    if (!active) return;
    const touch = Array.from(event.touches).find(
      (t) => t.identifier === active.id,
    );
    if (
      !touch ||
      Math.hypot(touch.clientX - active.x, touch.clientY - active.y) > 8
    ) {
      active.moved = true;
      releaseDevicePress();
    }
  },
  { passive: true, capture: true },
);
const finishDeviceTouch = (event, cancelled) => {
  const active = activeDeviceTouch;
  if (
    !active ||
    !Array.from(event.changedTouches).some((t) => t.identifier === active.id)
  )
    return;
  if (
    !cancelled &&
    !active.moved &&
    active.row.isConnected &&
    !pendingNavigation &&
    !state.compactOpen &&
    !edgeReturn
  )
    completedDeviceTouch = { id: active.row.dataset.id };
  activeDeviceTouch = null;
  releaseDevicePress();
};
document.addEventListener(
  "touchend",
  (event) => finishDeviceTouch(event, false),
  { passive: true, capture: true },
);
document.addEventListener(
  "touchcancel",
  (event) => finishDeviceTouch(event, true),
  { passive: true, capture: true },
);
window.addEventListener("blur", cancelDeviceInteraction);
let edgeReturn;
function clearEdgeReturn(gesture = edgeReturn) {
  if (!gesture || gesture !== edgeReturn) return;
  cancelMotion($("detail-surface"));
  $("detail-surface").style.removeProperty("--return-offset");
  document.body.classList.remove("edge-return");
  edgeReturn = null;
  pendingNavigation = false;
  $("detail-surface").setAttribute("aria-busy", "false");
  scheduleLayout();
}
document.addEventListener(
  "touchstart",
  (event) => {
    if (edgeReturn && event.touches.length !== 1) {
      clearEdgeReturn();
      return;
    }
    if (!compact.matches || event.touches.length !== 1 || !event.cancelable)
      return;
    const touch = event.touches[0],
      surface = $("detail-surface");
    const resuming = edgeReturn?.settling && !edgeReturn.committing;
    if (
      !resuming &&
      (touch.clientX > 24 ||
        pendingNavigation ||
        !state.compactOpen ||
        !history.state?.lanFromList ||
        !activePanel)
    )
      return;
    event.preventDefault();
    const initial = resuming ? presentation(surface).x : 0;
    if (resuming) cancelMotion(surface);
    const gesture = edgeReturn || {
      width: surface.clientWidth,
      panel: activePanel,
      scroll: activePanel.scrollTop,
      run: 0,
    };
    Object.assign(gesture, {
      id: touch.identifier,
      startX: touch.clientX,
      startY: touch.clientY,
      initial,
      distance: initial,
      velocity: 0,
      lastX: touch.clientX,
      lastTime: event.timeStamp,
      mode: "tracking",
      settling: false,
    });
    ++gesture.run;
    edgeReturn = gesture;
    pendingNavigation = true;
    settleDevicePress();
    chart.clear();
    closeAllRangeTips(true);
    $("detail-surface").setAttribute("aria-busy", "true");
    stopScrollMotion(activePanel);
    surface.style.setProperty("--return-offset", initial + "px");
    document.body.classList.add("edge-return");
    scheduleLayout();
  },
  { passive: false, capture: true },
);
document.addEventListener(
  "touchmove",
  (event) => {
    const gesture = edgeReturn;
    if (!gesture || gesture.settling || gesture.committing) return;
    const touch = Array.from(event.touches).find(
      (t) => t.identifier === gesture.id,
    );
    if (!touch) return;
    event.preventDefault();
    const dx = touch.clientX - gesture.startX,
      dy = touch.clientY - gesture.startY;
    if (
      gesture.mode === "tracking" &&
      Math.abs(dy) > 8 &&
      Math.abs(dy) > Math.abs(dx)
    ) {
      gesture.mode = "scroll";
      document.body.classList.remove("edge-return");
      $("detail-surface").style.removeProperty("--return-offset");
    }
    if (gesture.mode === "scroll") {
      gesture.panel.scrollTop = gesture.scroll - dy;
      return;
    }
    if (Math.abs(dx) > 8) gesture.mode = "swipe";
    const elapsed = event.timeStamp - gesture.lastTime;
    if (elapsed > 0)
      gesture.velocity = (touch.clientX - gesture.lastX) / elapsed;
    gesture.lastTime = event.timeStamp;
    gesture.lastX = touch.clientX;
    gesture.distance = Math.max(
      0,
      Math.min(gesture.width, gesture.initial + dx),
    );
    $("detail-surface").style.setProperty(
      "--return-offset",
      gesture.distance + "px",
    );
  },
  { passive: false, capture: true },
);
async function finishEdgeReturn(event, cancelled) {
  const gesture = edgeReturn;
  if (
    !gesture ||
    gesture.settling ||
    gesture.committing ||
    !Array.from(event.changedTouches).some((t) => t.identifier === gesture.id)
  )
    return;
  if (event.cancelable) event.preventDefault();
  if (gesture.mode === "scroll") {
    clearEdgeReturn(gesture);
    return;
  }
  const velocity =
    event.timeStamp - gesture.lastTime > 100 ? 0 : gesture.velocity;
  const complete =
    !cancelled &&
    shouldCompleteReturn(gesture.distance, velocity, gesture.width);
  gesture.settling = true;
  const run = ++gesture.run,
    target = complete ? gesture.width : 0;
  let frames = returnSpringFrames(
    gesture.distance,
    target,
    cancelled ? 0 : velocity,
    gesture.width,
  );
  const settled = frames.findIndex(
    (frame, index) =>
      index > 0 &&
      Math.abs(new DOMMatrixReadOnly(frame.transform).m41 - target) < 0.05,
  );
  const duration = settled > 0 ? Math.max(80, (400 * settled) / 60) : 400;
  if (settled > 0)
    frames = frames
      .slice(0, settled + 1)
      .map((frame, index) => ({ ...frame, offset: index / settled }));
  await animate($("detail-surface"), frames, { duration, easing: "linear" });
  if (edgeReturn !== gesture || run !== gesture.run) return;
  $("detail-surface").style.setProperty("--return-offset", target + "px");
  if (!complete) {
    clearEdgeReturn(gesture);
    return;
  }
  gesture.committing = true;
  // The real neutral list is already fully visible; traversal only synchronizes history.
  history.back();
}
document.addEventListener(
  "touchend",
  (event) => finishEdgeReturn(event, false),
  { passive: false, capture: true },
);
document.addEventListener(
  "touchcancel",
  (event) => finishEdgeReturn(event, true),
  { passive: false, capture: true },
);
$("device-list").addEventListener("keydown", (event) => {
  if (
    !["ArrowUp", "ArrowDown", "Home", "End", "Enter", " "].includes(event.key)
  )
    return;
  const devices = sortedDevices();
  if (!devices.length) return;
  event.preventDefault();
  const index = Math.max(
    0,
    devices.findIndex((d) => d.id === state.selected),
  );
  const target =
    event.key === "Home"
      ? 0
      : event.key === "End"
        ? devices.length - 1
        : event.key === "ArrowUp"
          ? Math.max(0, index - 1)
          : event.key === "ArrowDown"
            ? Math.min(devices.length - 1, index + 1)
            : index;
  navigate("device", devices[target].id);
  rows.get(devices[target].id)?.focus({ preventScroll: true });
  rows.get(devices[target].id)?.scrollIntoView({ block: "nearest" });
});
function initializeSegments(group, onSelect) {
  const buttons = [...group.querySelectorAll("button")];
  let touchStart;
  const choose = (index) => {
    if (
      index < 0 ||
      index >= buttons.length ||
      buttons[index].getAttribute("aria-pressed") === "true"
    )
      return;
    setSegment(group, index);
    onSelect(buttons[index], index);
  };
  group.addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button) choose(buttons.indexOf(button));
  });
  group.addEventListener("pointerdown", (event) => {
    if (event.button !== 0) return;
    group.setPointerCapture(event.pointerId);
    if (event.pointerType !== "mouse") {
      touchStart = { x: event.clientX, y: event.clientY };
      return;
    }
    const rect = group.getBoundingClientRect();
    choose(
      Math.max(
        0,
        Math.min(
          buttons.length - 1,
          Math.floor(
            (event.clientX - rect.left - 2) /
              ((rect.width - 4) / buttons.length),
          ),
        ),
      ),
    );
  });
  group.addEventListener("pointermove", (event) => {
    if (!group.hasPointerCapture(event.pointerId) || !event.buttons) return;
    if (
      touchStart &&
      (Math.abs(event.clientX - touchStart.x) < 10 ||
        Math.abs(event.clientX - touchStart.x) <
          Math.abs(event.clientY - touchStart.y))
    )
      return;
    const rect = group.getBoundingClientRect();
    choose(
      Math.max(
        0,
        Math.min(
          buttons.length - 1,
          Math.floor(
            (event.clientX - rect.left - 2) /
              ((rect.width - 4) / buttons.length),
          ),
        ),
      ),
    );
  });
  group.addEventListener("pointerup", (event) => {
    touchStart = null;
    if (group.hasPointerCapture(event.pointerId))
      group.releasePointerCapture(event.pointerId);
  });
  group.addEventListener("pointercancel", () => {
    touchStart = null;
  });
  group.addEventListener("keydown", (event) => {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const current = buttons.findIndex(
      (b) => b.getAttribute("aria-pressed") === "true",
    );
    const next =
      event.key === "Home"
        ? 0
        : event.key === "End"
          ? buttons.length - 1
          : Math.max(
              0,
              Math.min(
                buttons.length - 1,
                current + (event.key === "ArrowRight" ? 1 : -1),
              ),
            );
    choose(next);
    buttons[next].focus();
  });
}
let filterGeneration = 0;
const filterGhosts = new Set();
initializeSegments($("filters"), (button) => {
  const filter = button.dataset.filter;
  if (state.filter === filter) return;
  const generation = ++filterGeneration;
  const list = $("device-list"),
    oldStyle = getComputedStyle(list),
    ghost = list.cloneNode(true);
  for (const prior of filterGhosts) {
    cancelMotion(prior);
    prior.remove();
  }
  filterGhosts.clear();
  ghost.removeAttribute("id");
  ghost.classList.add("filter-ghost");
  ghost.setAttribute("aria-hidden", "true");
  ghost.inert = true;
  ghost.style.opacity = oldStyle.opacity;
  ghost.style.transform = oldStyle.transform;
  ghost.querySelectorAll("[id]").forEach((el) => el.removeAttribute("id"));
  ghost.querySelectorAll("[tabindex]").forEach((el) => (el.tabIndex = -1));
  list.parentElement.append(ghost);
  ghost.scrollTop = list.scrollTop;
  filterGhosts.add(ghost);
  cancelMotion(list);
  stopScrollMotion(list);
  state.filter = filter;
  const selected = sortedDevices()[0];
  filterTransition = true;
  renderList(true);
  list.scrollTop = 0;
  if (state.page === "device" && !compact.matches)
    navigate("device", selected?.id || "");
  else {
    selectContext(selected?.id || "");
    renderList(true);
  }
  const direction = filter === "offline" ? 1 : -1;
  animate(
    ghost,
    nativeFrames(
      presentation(ghost),
      { opacity: 0, scale: 1, x: -340 * direction, y: 0 },
      "cubicOut",
    ),
    { duration: 480, easing: "linear" },
  ).then(() => {
    ghost.remove();
    filterGhosts.delete(ghost);
  });
  animate(
    list,
    nativeFrames(
      { opacity: 0, scale: 1, x: 340 * direction, y: 0 },
      visualIdentity,
      "cubicOut",
    ),
    { duration: 480, easing: "linear" },
  ).then(() => {
    if (generation !== filterGeneration) return;
    filterTransition = false;
    if (!pendingNavigation) renderList(true);
  });
});
initializeSegments($("traffic-range"), (button) => {
  if (compact.matches) return;
  state.desktopRange = Number(button.dataset.range);
  state.range = state.desktopRange;
  chart.clear();
  const d = selectedDevice();
  if (d)
    chart.update(
      state.detail?.history || [],
      state.detail?.serverTime || state.snapshot?.refreshedAt || Date.now(),
      state.range,
      state.snapshot?.preferences?.refreshIntervalSeconds || 1,
    );
});
document
  .querySelectorAll("[data-page]")
  .forEach((button) =>
    button.addEventListener("click", () => navigate(button.dataset.page)),
  );
$("history-toggle").addEventListener("click", toggleHistory);
systemDark.addEventListener("change", applyTheme);
document.addEventListener(
  "keydown",
  () => document.body.classList.add("keyboard-input"),
  { capture: true },
);
document.addEventListener(
  "pointerdown",
  (event) => {
    document.body.classList.remove("keyboard-input");
    if (!event.target.closest(".total-card")) closeAllRangeTips(true);
    if (!event.target.closest("#traffic-chart")) chart.clear();
  },
  { capture: true },
);
function restoreBrowserHistory() {
  if (compact.matches && location.hash === "#devices")
    listRestoredAt = performance.now();
  settleDevicePress();
  // Safari owns the interactive back/forward animation. Restore our DOM
  // immediately so its completed list snapshot is not replaced by the old page.
  skipRefreshMotion = true;
  $("connection-indicator")
    .getAnimations()
    .forEach((animation) => animation.cancel());
  const restored = applyRoute(false);
  const gesture = edgeReturn;
  if (gesture) Promise.resolve(restored).then(() => clearEdgeReturn(gesture));
}
window.addEventListener("hashchange", restoreBrowserHistory);
window.addEventListener("popstate", restoreBrowserHistory);
window.addEventListener("pageshow", (event) => {
  if (event.persisted) restoreBrowserHistory();
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden) {
    cancelDeviceInteraction();
    clearEdgeReturn();
  }
  if (!document.hidden) {
    clearTimeout(pollTimer);
    refresh();
  }
});
function sidebarMaximumWidth() {
  return Math.max(280, Math.min(520, innerWidth - 606));
}
function sidebarWidth() {
  return Math.min(sidebarExpandedWidth, sidebarMaximumWidth()) - 8;
}
function updateSidebar() {
  $("workspace").style.setProperty("--sidebar-width", sidebarWidth() + "px");
  $("sidebar-splitter").setAttribute(
    "aria-valuenow",
    String(Math.round(sidebarWidth() + 8)),
  );
  $("sidebar-splitter").setAttribute("aria-valuemin", "280");
  $("sidebar-splitter").setAttribute(
    "aria-valuemax",
    String(sidebarMaximumWidth()),
  );
}
let dragStart;
$("sidebar-splitter").addEventListener("pointerdown", (event) => {
  if (compact.matches || event.button !== 0) return;
  dragStart = {
    x: event.clientX,
    width: sidebarWidth() + 8,
    pointerId: event.pointerId,
  };
  $("sidebar-splitter").setPointerCapture(event.pointerId);
});
$("sidebar-splitter").addEventListener("pointermove", (event) => {
  if (!dragStart || event.pointerId !== dragStart.pointerId) return;
  if (!(event.buttons & 1)) {
    dragStart = null;
    return;
  }
  sidebarExpandedWidth = Math.min(
    sidebarMaximumWidth(),
    Math.max(280, dragStart.width + event.clientX - dragStart.x),
  );
  updateSidebar();
});
function finishSidebarDrag(event) {
  dragStart = null;
  if ($("sidebar-splitter").hasPointerCapture(event.pointerId))
    $("sidebar-splitter").releasePointerCapture(event.pointerId);
}
$("sidebar-splitter").addEventListener("pointerup", finishSidebarDrag);
$("sidebar-splitter").addEventListener("pointercancel", finishSidebarDrag);
$("sidebar-splitter").addEventListener("lostpointercapture", () => {
  dragStart = null;
});
$("sidebar-splitter").addEventListener("keydown", (event) => {
  if (!["ArrowLeft", "ArrowRight"].includes(event.key)) return;
  event.preventDefault();
  sidebarExpandedWidth = Math.min(
    sidebarMaximumWidth(),
    Math.max(280, sidebarWidth() + 8 + (event.key === "ArrowRight" ? 1 : -1)),
  );
  updateSidebar();
});
compact.addEventListener("change", () => {
  clearEdgeReturn();
  state.range = compact.matches ? 5 : state.desktopRange;
  setSegment($("traffic-range"), [1, 5, 10].indexOf(state.desktopRange));
  chart.update(
    state.detail?.history || [],
    state.detail?.serverTime || state.snapshot?.refreshedAt || Date.now(),
    state.range,
    state.snapshot?.preferences?.refreshIntervalSeconds || 1,
  );
  ++state.nav;
  pendingNavigation = false;
  $("detail-surface").setAttribute("aria-busy", "false");
  navigationController?.abort();
  chart.clear();
  closeAllRangeTips();
  for (const el of [
    ...document.querySelectorAll(".page"),
    $("empty-panel"),
    $("sidebar"),
  ]) {
    resetPanelPresentation(el);
    stopScrollMotion(el);
  }
  updateSidebar();
  state.compactOpen = false;
  document.body.classList.remove("compact-detail");
  if (compact.matches && state.snapshot) {
    history.replaceState({ lanPage: "list" }, "", "#devices");
    lastRouteHash = location.hash;
  }
  if (!compact.matches && state.snapshot) {
    const r = route();
    navigate(r.page === "list" ? "device" : r.page, r.id || state.selected, {
      animate: false,
      hash: false,
    });
  }
  scheduleLayout();
});
window.addEventListener("resize", () => {
  updateSidebar();
  scheduleLayout();
});
portrait.addEventListener("change", scheduleLayout);
function updateScrollMask(el) {
  precision?.update(el);
}
function layoutSessions() {
  const canvas =
      layoutMetadata.canvas ||
      (layoutMetadata.canvas = document.createElement("canvas")),
    ctx = canvas.getContext("2d");
  for (const row of $("session-list").children) {
    if (row.hidden) continue;
    const labels = row.querySelectorAll(".session-label");
    labels[0].textContent = compact.matches ? "离线" : "离线时间";
    labels[1].textContent = compact.matches ? "上线" : "上线时间";
    row.classList.remove("stacked", "history-labels-above");
    for (const key of ["marker", "label", "gap", "inline-gap"])
      row.style.removeProperty("--history-" + key);
    const time = row.querySelector(".session-times"),
      badge = row.querySelector(".session-duration");
    ctx.font = getComputedStyle(badge).font;
    const badgeText = ctx.measureText(badge.textContent).width;
    const spanWidths = [...time.querySelectorAll(".session-time")].map((line) =>
      [...line.children].map((span) => span.scrollWidth),
    );
    const natural = Math.max(
      ...spanWidths.map(
        (parts) =>
          parts.reduce((a, b) => a + b, 0) + Math.max(0, parts.length - 1) * 10,
      ),
    );
    const available = row.clientWidth - 24;
    const normalLabel = compact.matches ? 30 : 62;
    if (compact.matches) row.style.setProperty("--history-label", "30px");
    if (24 + normalLabel + 10 + natural + 12 + badgeText + 36 > available) {
      ctx.font = getComputedStyle(time.querySelector(".session-label")).font;
      const label =
        Math.ceil(
          Math.max(
            ...[...time.querySelectorAll(".session-label")].map(
              (el) => ctx.measureText(el.textContent).width,
            ),
          ),
        ) + 1;
      const values = {
        marker: 16,
        label: label,
        gap: 6,
        "inline-gap": 6,
      };
      for (const [key, value] of Object.entries(values))
        row.style.setProperty("--history-" + key, value + "px");
      const left = available - 16 - 6 - badgeText - 36;
      if (left < label + 6 + Math.max(...spanWidths.flat()))
        row.classList.add("history-labels-above");
    }
    row.style.setProperty(
      "--history-track-height",
      Math.max(38, time.offsetHeight - 8) + "px",
    );
  }
}
function layoutMeshLinks() {
  const host = $("mesh-list");
  if (!host.clientWidth) return;
  const nodes = [...host.querySelectorAll(":scope >.mesh-node")],
    map = new Map(nodes.map((node) => [node.dataset.nodeId, node]));
  for (const node of nodes) {
    let parent = map.get(node.dataset.parentId),
      depth = 0;
    const seen = new Set([node]);
    while (parent && !seen.has(parent)) {
      seen.add(parent);
      depth++;
      parent = map.get(parent.dataset.parentId);
    }
    node.style.setProperty("--mesh-depth", depth);
  }
  let svg = host.querySelector(":scope >.mesh-connectors");
  if (!svg) {
    svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.classList.add("mesh-connectors");
    svg.setAttribute("aria-hidden", "true");
    host.prepend(svg);
  }
  const paths = [];
  const radius =
    parseFloat(
      getComputedStyle(document.documentElement).getPropertyValue(
        "--ios-card-radius",
      ),
    ) || 26.6667;
  const bounds = (node) => ({
    x: node.offsetLeft,
    y: node.offsetTop,
    width: node.offsetWidth,
    height: node.offsetHeight,
  });
  for (const node of nodes) {
    const parent = map.get(node.dataset.parentId);
    if (parent && parent !== node) {
      paths.push(meshLinkPath(bounds(parent), bounds(node), radius));
    }
  }
  svg.setAttribute("viewBox", `0 0 ${host.clientWidth} ${host.scrollHeight}`);
  svg.replaceChildren(
    ...paths.map((data) => {
      const path = document.createElementNS(
        "http://www.w3.org/2000/svg",
        "path",
      );
      path.setAttribute("d", data);
      return path;
    }),
  );
}
function layoutMetadata() {
  const host = $("network-metadata");
  const blocks = [...host.children];
  blocks[0].hidden = portrait.matches;
  if (!host.clientWidth) return;
  const canvas =
      layoutMetadata.canvas ||
      (layoutMetadata.canvas = document.createElement("canvas")),
    context = canvas.getContext("2d");
  const widths = blocks.map(
    (block) =>
      Math.max(
        ...[...block.children].map((text) => {
          const cs = getComputedStyle(text);
          const size = text.classList.contains("field-label") ? 12 : 14;
          context.font = `${cs.fontStyle} ${cs.fontWeight} ${size}px ${cs.fontFamily}`;
          return context.measureText(text.textContent).width;
        }),
      ) +
      2 / (devicePixelRatio || 1),
  );
  const layout = metadataLayout(widths, host.clientWidth, portrait.matches);
  host.style.setProperty("--metadata-scale", layout.scale);
  host.style.setProperty("--metadata-gap", `${layout.gap}px`);
  host.style.setProperty(
    "--metadata-columns",
    layout.widths.map(width => `${width}px`).join(" "),
  );
}
function scheduleLayout() {
  if (layoutFrame) return;
  layoutFrame = requestAnimationFrame(() => {
    layoutFrame = 0;
    layoutMetadata();
    const metrics = $("traffic-metrics");
    if (metrics.clientWidth)
      metrics.style.gridTemplateColumns = `repeat(${metrics.clientWidth >= 562 ? 4 : metrics.clientWidth >= 274 ? 2 : 1},minmax(0,1fr))`;
    const header = $("traffic-heading");
    if (header.clientWidth) {
      const title = header.querySelector("h2"),
        cs = getComputedStyle(title);
      const canvas =
          layoutMetadata.canvas ||
          (layoutMetadata.canvas = document.createElement("canvas")),
        ctx = canvas.getContext("2d");
      ctx.font = cs.font;
      header.classList.toggle(
        "wrapped",
        ctx.measureText(title.textContent).width +
          19 +
          228 +
          10 +
          4 / (devicePixelRatio || 1) >
          header.clientWidth && !compact.matches,
      );
    }
    layoutSessions();
    layoutMeshLinks();
    const list = $("session-list"),
      last = [...list.children].filter((row) => !row.hidden).at(-1);
    if (!motion.has($("history-reveal")))
      $("history-reveal").style.height =
        (last ? last.offsetTop + last.offsetHeight + 18 : 36) + "px";
    document
      .querySelectorAll(".scroll-pane,.device-list")
      .forEach(updateScrollMask);
    precision?.alignLabels();
  });
}
const observer = new ResizeObserver(scheduleLayout);
for (const el of [
  $("detail-surface"),
  $("sidebar"),
  $("network-metadata"),
  $("traffic-heading"),
  $("traffic-metrics"),
  $("history-reveal"),
  $("mesh-list"),
])
  observer.observe(el);
document.fonts.ready.then(scheduleLayout);
const wheelStates = new WeakMap();
function stopScrollMotion(el) {
  const run = el && wheelStates.get(el);
  if (run) {
    cancelAnimationFrame(run.frame);
    wheelStates.delete(el);
  }
}
for (const el of document.querySelectorAll(".scroll-pane,.device-list")) {
  el.addEventListener(
    "scroll",
    () => {
      const run = wheelStates.get(el);
      if (
        run &&
        run.expected !== undefined &&
        Math.abs(el.scrollTop - run.expected) > 1
      )
        stopScrollMotion(el);
      updateScrollMask(el);
      if (el === $("device-page")) {
        closeAllRangeTips(true);
        chart.clear();
      }
    },
    { passive: true },
  );
  el.addEventListener("pointerdown", () => stopScrollMotion(el), {
    capture: true,
  });
  el.addEventListener(
    "touchmove",
    () => {
      stopScrollMotion(el);
      closeAllRangeTips(true);
      chart.clear();
    },
    { passive: true },
  );
  el.addEventListener("keydown", () => stopScrollMotion(el), { capture: true });
  el.addEventListener(
    "wheel",
    (event) => {
      if (event.ctrlKey || !event.deltaY || reduced.matches) return;
      const max = el.scrollHeight - el.clientHeight;
      if (max <= 0) return;
      const old = wheelStates.get(el),
        step =
          (el.classList.contains("device-list") ? 170 : 180) *
          Math.sign(event.deltaY) *
          Math.min(1, Math.max(0.05, Math.abs(event.deltaY) / 100));
      const prior = old?.target ?? el.scrollTop;
      const remaining =
        Math.sign(prior - el.scrollTop) === Math.sign(step)
          ? prior - el.scrollTop
          : 0;
      const limit = el.classList.contains("device-list") ? 340 : 360;
      const target = Math.min(
        max,
        Math.max(
          0,
          el.scrollTop + Math.max(-limit, Math.min(limit, remaining + step)),
        ),
      );
      if (target === el.scrollTop) return;
      event.preventDefault();
      if (old) cancelAnimationFrame(old.frame);
      const run = {
        start: el.scrollTop,
        target,
        at: performance.now(),
        frame: 0,
      };
      wheelStates.set(el, run);
      const frame = (now) => {
        if (wheelStates.get(el) !== run) return;
        const progress = Math.min(1, (now - run.at) / 400);
        el.scrollTop =
          run.start + (run.target - run.start) * (1 - (1 - progress) ** 2);
        run.expected = el.scrollTop;
        if (progress < 1) run.frame = requestAnimationFrame(frame);
        else wheelStates.delete(el);
      };
      run.frame = requestAnimationFrame(frame);
    },
    { passive: false },
  );
}
const tipStates = [];
for (const [cardId, tipId] of [
  ["download-total-card", "download-range"],
  ["upload-total-card", "upload-range"],
]) {
  const card = $(cardId),
    tip = $(tipId),
    item = { card, tip, timer: 0, generation: 0, scrollClosing: false };
  tipStates.push(item);
  const show = (delay = 250) => {
    clearTimeout(item.timer);
    if (item.scrollClosing && !tip.hidden) return;
    const generation = ++item.generation;
    if (!tip.hidden) {
      cancelMotion(tip);
      tip.style.opacity = "1";
      return;
    }
    item.timer = setTimeout(() => {
      if (
        generation !== item.generation ||
        !card.isConnected ||
        $("device-page").hidden ||
        !card.getClientRects().length
      )
        return;
      tip.hidden = false;
      item.scrollClosing = false;
      animate(tip, [{ opacity: 0 }, { opacity: 1 }], {
        duration: 150,
        easing: "linear",
      });
    }, delay);
  };
  let touchStart;
  card.addEventListener("pointerenter", (event) => {
    if (event.pointerType === "mouse") show();
  });
  card.addEventListener("focus", () => {
    if (document.body.classList.contains("keyboard-input")) show();
  });
  card.addEventListener("pointerdown", (event) => {
    if (event.pointerType !== "mouse")
      touchStart = { x: event.clientX, y: event.clientY };
  });
  card.addEventListener("pointerup", (event) => {
    if (
      touchStart &&
      Math.hypot(event.clientX - touchStart.x, event.clientY - touchStart.y) < 8
    ) {
      item.scrollClosing = false;
      show(0);
    }
    touchStart = null;
  });
  card.addEventListener("pointercancel", () => {
    touchStart = null;
    closeRangeTip(item, true);
  });
  card.addEventListener("pointerleave", (event) => {
    if (event.pointerType === "mouse") closeRangeTip(item);
  });
  card.addEventListener("blur", () => closeRangeTip(item));
  card.addEventListener("pointermove", (event) => {
    if (event.pointerType === "mouse" && item.scrollClosing && tip.hidden) {
      item.scrollClosing = false;
      show();
    }
  });
}
function closeRangeTip(item, scroll = false) {
  clearTimeout(item.timer);
  if (item.tip.hidden) return;
  if (item.scrollClosing && scroll) return;
  const generation = ++item.generation;
  item.scrollClosing = scroll;
  const opacity = getComputedStyle(item.tip).opacity;
  animate(item.tip, [{ opacity }, { opacity: 0 }], {
    duration: 120,
    easing: "cubic-bezier(0,.667,.333,1)",
  }).then(() => {
    if (generation === item.generation) item.tip.hidden = true;
  });
}
function closeAllRangeTips(scroll = false) {
  for (const item of tipStates) closeRangeTip(item, scroll);
}
precision = new DesktopPrecision(stopScrollMotion);
updateSidebar();
applyTheme();
refresh();
