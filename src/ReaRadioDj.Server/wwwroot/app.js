// REA-Radio-DJ browser studio: renders the state the engine pushes over server-sent events.
// The full studio is the desktop app (ReaRadioDj.Studio); this page is the quick remote view.
"use strict";

const $ = (id) => document.getElementById(id);
const post = (url) => fetch(url, { method: "POST" });
const fmt = (s) => {
  if (s == null || !isFinite(s)) return "—";
  s = Math.max(0, s);
  const m = Math.floor(s / 60);
  return `${m}:${String(Math.floor(s % 60)).padStart(2, "0")}`;
};
const el = (tag, cls, text) => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
};
const typeLabel = { song: "Muziek", jingle: "Jingle", aiBreak: "AI-break" };
const typeClass = { song: "Song", jingle: "Jingle", aiBreak: "AiBreak" };
const postJson = (url, body) => fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

function button(text, title, onClick) {
  const b = el("button", "small", text);
  b.title = title;
  b.addEventListener("click", onClick);
  return b;
}

function renderNow(engine) {
  const cur = engine.current;
  $("onair").textContent = cur ? "ON AIR" : "OFF AIR";
  $("onair").classList.toggle("live", !!cur);
  $("btnPlay").textContent = engine.autoPlay ? "⏸ Naar live-assist" : "▶ Automatisch";
  $("btnPlay").classList.toggle("primary", !engine.autoPlay);
  $("vuL").style.width = `${Math.min(100, engine.levelLeft * 100)}%`;
  $("vuR").style.width = `${Math.min(100, engine.levelRight * 100)}%`;

  if (!cur) {
    $("nowType").textContent = "—";
    $("nowType").className = "badge";
    $("nowTitle").textContent = engine.autoPlay ? "Wachten op materiaal…" : "Live-assist: start het volgende item";
    $("nowArtist").textContent = "";
    $("nowScript").hidden = true;
    $("progFill").style.width = "0";
    $("progRamp").style.width = "0";
    $("nowPos").textContent = $("nowLen").textContent = $("nowToMix").textContent = "—";
    return;
  }
  const isBreak = cur.type === "aiBreak";
  $("nowType").textContent = typeLabel[cur.type] ?? cur.type;
  $("nowType").className = `badge ${typeClass[cur.type] ?? ""}`;
  $("nowTitle").textContent = isBreak ? "AI-presentator" : cur.title || cur.label;
  $("nowArtist").textContent = isBreak ? cur.artist : cur.artist;
  $("nowScript").hidden = !isBreak || !cur.script;
  $("nowScript").textContent = cur.script ? `“${cur.script}”` : "";
  const len = cur.to || 1;
  $("progFill").style.width = `${(cur.position / len) * 100}%`;
  $("progMix").style.left = `${(cur.mixAt / len) * 100}%`;
  $("progRamp").style.width = cur.intro ? `${(cur.intro / len) * 100}%` : "0";
  $("nowPos").textContent = fmt(cur.position);
  $("nowLen").textContent = fmt(len);
  $("nowToMix").textContent = fmt(cur.mixAt - cur.position);
}

function renderItem(i, withActions) {
  const li = el("li");
  li.append(el("span", `badge ${typeClass[i.type] ?? ""}`, typeLabel[i.type] ?? i.type));
  const main = el("div", "main");
  if (i.type === "aiBreak") {
    const t = el("div", "t");
    t.append(document.createTextNode("AI-presentator "));
    t.append(el("span", `state ${i.breakState}`, i.breakState === "ready" ? `klaar · ${i.voice} · ${i.scriptSource}` : i.breakState));
    main.append(t);
    if (i.script) main.append(el("div", "s", i.script));
  } else {
    main.append(el("div", "t", i.title || i.label));
    if (i.artist) main.append(el("div", "a", i.artist));
  }
  if (i.error) main.append(el("div", "err", i.error));
  li.append(main);

  const actions = el("div", "actions");
  if (i.length != null) actions.append(el("span", "a", fmt(i.length)));
  if (withActions) {
    actions.append(
      button("↑", "Omhoog", () => post(`/api/log/${i.id}/up`)),
      button("↓", "Omlaag", () => post(`/api/log/${i.id}/down`)),
      button("✕", "Verwijderen", () => fetch(`/api/log/${i.id}`, { method: "DELETE" })),
    );
  }
  li.append(actions);
  return li;
}

function renderList(id, items, withActions) {
  const list = $(id);
  list.replaceChildren(...items.map((i) => renderItem(i, withActions)));
}

function renderStream(s) {
  $("streamDot").className = `dot ${s.connected ? "on" : s.error ? "err" : ""}`;
  $("streamText").textContent = s.connected
    ? `live naar ${s.server} · ${(s.bytesSent / 1024 / 1024).toFixed(1)} MB`
    : s.error ?? `uit (${s.server})`;
}

let lastLog = "";
function render(state) {
  lastState = state;
  $("btnMic").classList.toggle("live", state.mixer.mic.open);
  $("btnMic").title = `Microfoon: ${state.mixer.mic.status}`;
  $("clockName").textContent = state.clock ? `klok: ${state.clock}` : "geen klok";
  $("station").textContent = state.station;
  document.title = `${state.station} · Studio`;
  $("clock").textContent = new Date(state.time).toLocaleTimeString("nl-NL");
  renderNow(state.engine);

  // Only rebuild the lists when they changed, so buttons stay clickable.
  const logKey = JSON.stringify([state.upcoming, state.history]);
  if (logKey !== lastLog) {
    lastLog = logKey;
    renderList("upcoming", state.upcoming, true);
    renderList("history", state.history, false);
    $("logCount").textContent = `${state.upcoming.length} items`;
  }
  $("libStatus").textContent = state.library.status;
  renderStream(state.stream);
}

async function loadLibrary() {
  const q = $("q").value.trim();
  const res = await fetch(`/api/library?q=${encodeURIComponent(q)}`);
  const items = await res.json();
  $("library").replaceChildren(...items.map((s) => {
    const li = el("li");
    li.append(el("span", `badge ${s.kind === "jingle" ? "Jingle" : "Song"}`, s.kind === "jingle" ? "Jingle" : s.category || "Muziek"));
    const main = el("div", "main");
    main.append(el("div", "t", s.title), el("div", "a", `${s.artist || ""} · ${fmt(s.cues.cueOut - s.cues.cueIn)}${s.lufs != null ? ` · ${s.lufs} LUFS` : ""}`));
    li.append(main);
    const actions = el("div", "actions");
    const add = (next) => postJson("/api/log/media", { mediaId: s.id, position: next ? 0 : null });
    actions.append(button("Nu", "Als volgende", () => add(true)), button("+", "Achteraan", () => add(false)));
    li.append(actions);
    return li;
  }));
}

function connect() {
  const source = new EventSource("/api/events");
  source.onmessage = (e) => render(JSON.parse(e.data));
  source.onerror = () => { source.close(); setTimeout(connect, 2000); };
}

let lastState = null;
$("btnPlay").onclick = () => postJson("/api/transport/autoplay", { on: !(lastState?.engine.autoPlay) });
$("btnNext").onclick = () => post("/api/transport/next");
$("btnMic").onclick = () => post("/api/mixer/mic/toggle");
$("btnStop").onclick = () => post("/api/transport/stop");
$("btnSkip").onclick = () => post("/api/transport/skip");
$("btnBreak").onclick = () => post("/api/log/break");
$("btnScan").onclick = () => post("/api/library/scan").then(() => setTimeout(loadLibrary, 1500));
$("btnStreamStart").onclick = () => post("/api/stream/start");
$("btnStreamStop").onclick = () => post("/api/stream/stop");
let searchTimer;
$("q").addEventListener("input", () => { clearTimeout(searchTimer); searchTimer = setTimeout(loadLibrary, 250); });

connect();
loadLibrary();
setInterval(loadLibrary, 15000);

// Studio keys: F5 start next, F6 automatic / live-assist, F8 microphone, F9 stop.
document.addEventListener("keydown", (e) => {
  if (e.target instanceof HTMLInputElement) return;
  const map = { F5: "btnNext", F6: "btnPlay", F8: "btnMic", F9: "btnStop" };
  if (map[e.key]) { e.preventDefault(); $(map[e.key]).click(); }
});
